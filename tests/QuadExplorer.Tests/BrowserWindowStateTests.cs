using System.Text.Json;
using QuadExplorer;

internal static class BrowserWindowStateTests
{
    public static (string Name, Action Run)[] Cases =
    [
        ("legacy sessions migrate missing null and empty window lists", LegacyMigration),
        ("browser windows normalize independently without mutating input", IndependentWindows),
        ("browser window defaults preserve blank panes and nullable positions", WindowDefaults),
        ("last active window indices are bounded", LastActiveWindow),
        ("window projection maps every local field and preserves shared state", Projection),
        ("multiwindow sessions roundtrip with first-window legacy projection", Roundtrip),
        ("workspaces normalize independently of browser windows", WorkspaceNormalization),
        ("legacy migration backs up original bytes once without rewriting", MigrationBackup),
        ("modern and corrupt sessions create no migration backup", NoMigrationBackup),
        ("migration backup failures warn without losing loaded data", BackupFailure),
        ("0.8.20 fixture survives upgrade downgrade and reupgrade with original backup", LegacyVersionRoundtrip),
        ("saved physical window bounds survive repeated session roundtrips", PhysicalBoundsRoundtrip),
        ("saved physical bounds restore twice unchanged at 120 and 144 DPI", BrowserWindowGeometryTests.SavedBounds),
        ("fresh window defaults and minimums scale at 120 and 144 DPI", BrowserWindowGeometryTests.FreshDefaults),
        ("new windows preserve source size and cascade within their monitor at 120 and 144 DPI", BrowserWindowGeometryTests.CascadedBounds)
    ];

    private static BrowserWindowState SampleWindow() => new()
    {
        Layout = 3,
        LayoutTree = PaneLayout.Leaf(3).Split(3, 1, SplitEdge.Left).SetRatio("", .35),
        Panes = [new PaneState { Tabs = ["Z:\\hidden"] }, new PaneState { Tabs = ["Z:\\one", "Z:\\two"], SelectedTab = 1 },
            new PaneState(), new PaneState { Tabs = ["\\\\offline\\share"] }],
        ActivePane = 3,
        WindowX = -1600, WindowY = 120, WindowWidth = 1777, WindowHeight = 888,
        Maximized = true, Hidden = true, Minimized = true,
        ShowFolderTree = false, FolderTreeHeight = 340,
        ShowPreview = true, ShowDetails = true, PreviewWidth = 470,
        ShowSidebar = false, SidebarWidth = 315, FavoritesExpanded = false,
        ColumnRatio = .33, LeftRowRatio = .4, RightRowRatio = .7
    };

    private static void LegacyMigration()
    {
        var legacy = SessionStore.ProjectWindow(new SessionState { Language = "en" }, SampleWindow());
        foreach (var windows in new List<BrowserWindowState>?[] { null, [] })
        {
            var migrated = SessionStore.Normalize(legacy with { Windows = windows!, LastActiveWindow = 99 });
            Equal(1, migrated.Windows.Count);
            Equal(0, migrated.LastActiveWindow);
            SameWindow(SessionStore.NormalizeBrowserWindow(SampleWindow()), migrated.Windows[0]);
            SameWindow(migrated.Windows[0], SessionStore.LegacyWindow(migrated));
            Equal("en", migrated.Language);
        }

        InDirectory(path =>
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(legacy));
            var withoutWindows = document.RootElement.EnumerateObject()
                .Where(property => property.Name != nameof(SessionState.Windows))
                .ToDictionary(property => property.Name, property => property.Value.Clone());
            File.WriteAllText(path, JsonSerializer.Serialize(withoutWindows));
            var migrated = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(1, migrated.Windows.Count);
            SameWindow(SessionStore.NormalizeBrowserWindow(SampleWindow()), migrated.Windows[0]);
        });
    }

    private static void IndependentWindows()
    {
        var tabs = Enumerable.Range(0, 40).Select(index => $"Z:\\tab-{index}").ToList();
        tabs.Insert(0, " ");
        var first = SampleWindow() with
        {
            Panes = [new PaneState { Tabs = tabs, SelectedTab = int.MaxValue }, null!],
            ActivePane = 2, WindowWidth = -1, WindowHeight = int.MaxValue,
            SidebarWidth = -1, FolderTreeHeight = 1, ColumnRatio = double.NaN,
            LeftRowRatio = -1, RightRowRatio = 9
        };
        var second = new BrowserWindowState
        {
            Layout = 2, LayoutTree = new PaneLayout { First = PaneLayout.Leaf(0), Second = PaneLayout.Leaf(0) },
            ActivePane = 99, Panes = [new PaneState(), new PaneState { Tabs = ["Z:\\second"] }],
            WindowX = 2300, WindowY = -900, WindowWidth = 99999, WindowHeight = 1,
            SidebarWidth = 9999, FolderTreeHeight = 9999
        };
        var input = new SessionState { Windows = [first, second], Panes = null!, ActivePane = -1, Layout = 4 };
        var result = SessionStore.Normalize(input);
        Equal(2, result.Windows.Count);
        var a = result.Windows[0];
        var b = result.Windows[1];
        Equal(2, a.Layout);
        Equal(1, a.ActivePane);
        Equal(32, a.Panes[0].Tabs.Count);
        Equal(31, a.Panes[0].SelectedTab);
        Equal(0, a.Panes[1].Tabs.Count);
        Equal(-1, a.Panes[1].SelectedTab);
        Equal(900, a.WindowWidth);
        Equal(10000, a.WindowHeight);
        Equal(140, a.SidebarWidth);
        Equal(80, a.FolderTreeHeight);
        Equal(.5, a.ColumnRatio);
        Equal(.15, a.LeftRowRatio);
        Equal(.85, a.RightRowRatio);
        Equal(PaneLayout.Preset(2), b.LayoutTree);
        Equal(1, b.ActivePane);
        Equal(0, b.Panes[0].Tabs.Count);
        Equal("Z:\\second", b.Panes[1].Tabs[0]);
        Equal(10000, b.WindowWidth);
        Equal(600, b.WindowHeight);
        Equal(480, b.SidebarWidth);
        Equal(2000, b.FolderTreeHeight);
        Equal<int?>(2300, b.WindowX);
        Equal<int?>(-900, b.WindowY);
        SameWindow(a, SessionStore.LegacyWindow(result));
        result.Windows[0].Panes[0].Tabs.Clear();
        result.Windows.RemoveAt(1);
        Equal(2, input.Windows.Count);
        Equal(41, tabs.Count);
        Equal(int.MaxValue, first.Panes[0].SelectedTab);
        Equal(2, first.Panes.Count);
        Equal(-1, first.WindowWidth);
    }

    private static void WindowDefaults()
    {
        var defaults = SessionStore.NormalizeBrowserWindow(null!);
        Equal(1, defaults.Layout);
        Equal(PaneLayout.Leaf(0), defaults.LayoutTree);
        Equal(4, defaults.Panes.Count);
        Equal(1, defaults.Panes[0].Tabs.Count);
        Equal<int?>(null, defaults.WindowX);
        Equal<int?>(null, defaults.WindowY);
        Equal(1400, defaults.WindowWidth);
        Equal(900, defaults.WindowHeight);
        Equal(false, defaults.Maximized);
        Equal(false, defaults.Hidden);
        Equal(false, defaults.Minimized);
        Equal(true, defaults.ShowFolderTree);
        Equal(true, defaults.ShowSidebar);
        Equal(true, defaults.FavoritesExpanded);
        Equal(0, defaults.PreviewWidth);
        var blank = SessionStore.NormalizeBrowserWindow(new BrowserWindowState { Panes = [new PaneState()] });
        Equal(0, blank.Panes[0].Tabs.Count);
        Equal(-1, blank.Panes[0].SelectedTab);
        SameWindow(blank, SessionStore.NormalizeBrowserWindow(blank));
        var malformed = SessionStore.Normalize(new SessionState { Windows = [null!] });
        Equal(1, malformed.Windows.Count);
        SameWindow(defaults, malformed.Windows[0]);
    }

    private static void LastActiveWindow()
    {
        foreach (var (input, expected) in new[] { (-1, 0), (0, 0), (1, 1), (99, 1) })
        {
            var normalized = SessionStore.Normalize(new SessionState
            {
                Windows = [new BrowserWindowState(), SampleWindow()], LastActiveWindow = input
            });
            Equal(expected, normalized.LastActiveWindow);
            SameWindow(normalized.Windows[0], SessionStore.LegacyWindow(normalized));
        }
    }

    private static void Projection()
    {
        var source = new SessionState
        {
            Language = "ja", ColorTheme = "dark", Favorites = ["Z:\\favorite"],
            Workspaces = [new Workspace { Name = "Shared" }],
            Windows = [new BrowserWindowState()], LastActiveWindow = 12,
            GlobalToolbarPinned = false, KeepInTray = false
        };
        var window = SampleWindow();
        var projected = SessionStore.ProjectWindow(source, window);
        SameWindow(window, SessionStore.LegacyWindow(projected));
        Equal("ja", projected.Language);
        Equal("dark", projected.ColorTheme);
        Equal(false, projected.GlobalToolbarPinned);
        Equal(false, projected.KeepInTray);
        Equal(true, ReferenceEquals(source.Favorites, projected.Favorites));
        Equal(true, ReferenceEquals(source.Workspaces, projected.Workspaces));
        Equal(true, ReferenceEquals(source.Windows, projected.Windows));
        Equal(12, projected.LastActiveWindow);
        Equal(1400, source.WindowWidth);
        Equal(true, source.ShowFolderTree);
    }

    private static void Roundtrip()
    {
        InDirectory(path =>
        {
            var source = new SessionState
            {
                Language = "en", Windows = [SampleWindow(), new BrowserWindowState
                {
                    Layout = 1, LayoutTree = PaneLayout.Leaf(2), ActivePane = 2,
                    Panes = [new PaneState(), new PaneState(), new PaneState { Tabs = ["Z:\\other"] }],
                    WindowX = 2500, WindowY = -100, WindowWidth = 1200, WindowHeight = 700,
                    ShowPreview = false, ShowSidebar = true
                }],
                LastActiveWindow = 1, Layout = 4, WindowWidth = 9999,
                Panes = [new PaneState { Tabs = ["Z:\\stale-legacy"] }]
            };
            var expected = SessionStore.Normalize(source);
            SessionStore.Save(path, source);
            var restored = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(2, restored.Windows.Count);
            Equal(1, restored.LastActiveWindow);
            for (var index = 0; index < 2; index++)
                SameWindow(expected.Windows[index], restored.Windows[index]);
            SameWindow(restored.Windows[0], SessionStore.LegacyWindow(restored));
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var legacy = JsonSerializer.Deserialize<BrowserWindowState>(document.RootElement.GetRawText())!;
            SameWindow(restored.Windows[0], legacy);
            Equal(false, File.Exists(path + ".pre-multiwindow.bak"));
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
            Equal(9999, source.WindowWidth);
        });
    }

    private static void WorkspaceNormalization()
    {
        var workspace = new Workspace
        {
            Name = " Project ", LayoutTree = PaneLayout.Leaf(3), ActivePane = 2,
            Panes = [new PaneState { Tabs = ["Z:\\hidden"] }, new PaneState(), new PaneState(),
                new PaneState { Tabs = ["Z:\\project"], SelectedTab = 99 }]
        };
        var direct = SessionStore.NormalizeWorkspace(workspace);
        Equal("Project", direct.Name);
        Equal(3, direct.ActivePane);
        Equal(0, direct.Panes[3].SelectedTab);
        var session = SessionStore.Normalize(new SessionState
        {
            Windows = [SampleWindow(), new BrowserWindowState()], Workspaces = [workspace]
        });
        Equal(2, session.Windows.Count);
        Equal(JsonSerializer.Serialize(direct), JsonSerializer.Serialize(session.Workspaces[0]));
        Equal(99, workspace.Panes[3].SelectedTab);
        Equal(" Project ", workspace.Name);
    }

    private static void LegacyVersionRoundtrip()
    {
        InDirectory(path =>
        {
            var original = System.Text.Encoding.UTF8.GetBytes(Legacy0820SessionFixture.Json);
            File.WriteAllBytes(path, original);
            var legacy = Legacy0820SessionFixture.Read(path);
            var migrated = SessionStore.Load(path, out var warning);
            var backupPath = path + ".pre-multiwindow.bak";
            Equal<string?>(null, warning);
            Equal(1, migrated.Windows.Count);
            Equal(0, migrated.LastActiveWindow);
            Equal(1943, migrated.Windows[0].WindowWidth);
            Equal(1129, migrated.Windows[0].WindowHeight);
            Equal(true, migrated.Windows[0].Maximized);
            Equal<int?>(null, migrated.Windows[0].WindowX);
            Equal<int?>(null, migrated.Windows[0].WindowY);
            Equal(3, migrated.Windows[0].ActivePane);
            Equal("D:\\Projects\\release", migrated.Windows[0].Panes[0].Tabs[1]);
            Equal(1, migrated.Workspaces.Count);
            Equal(2, migrated.FrequentFolders.Count);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path)));
            Equal(true, original.SequenceEqual(File.ReadAllBytes(backupPath)));

            var first = migrated.Windows[0] with
            {
                WindowX = -1943, WindowY = 44, Hidden = true, Minimized = true
            };
            SessionStore.Save(path, migrated with
            {
                Windows = [first, SampleWindow()], LastActiveWindow = 1
            });
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                Equal(2, document.RootElement.GetProperty("Windows").GetArrayLength());
                Equal(1, document.RootElement.GetProperty("LastActiveWindow").GetInt32());
                Equal(1943, document.RootElement.GetProperty("WindowWidth").GetInt32());
                Equal(1129, document.RootElement.GetProperty("WindowHeight").GetInt32());
            }
            var modern = SessionStore.Load(path, out warning);
            Equal<string?>(null, warning);
            Equal(2, modern.Windows.Count);
            SameWindow(first, modern.Windows[0]);
            SameWindow(modern.Windows[0], SessionStore.LegacyWindow(modern));
            Equal(true, original.SequenceEqual(File.ReadAllBytes(backupPath)));

            // The frozen old schema reads every old field from the first-window copy,
            // ignores the extra windows, and loses new fields on its next serialization.
            var oldRead = Legacy0820SessionFixture.Read(path);
            Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(oldRead));
            var oldResaved = oldRead with { WindowWidth = 1820, Maximized = false };
            Legacy0820SessionFixture.Resave(path, oldResaved);
            var downgradedBytes = File.ReadAllBytes(path);
            using (var document = JsonDocument.Parse(downgradedBytes))
            {
                foreach (var addedField in new[] { "Windows", "LastActiveWindow", "WindowX", "WindowY", "Hidden", "Minimized" })
                    Equal(false, document.RootElement.TryGetProperty(addedField, out _));
            }
            Equal(true, original.SequenceEqual(File.ReadAllBytes(backupPath)));

            var reupgraded = SessionStore.Load(path, out warning);
            Equal<string?>(null, warning);
            Equal(1, reupgraded.Windows.Count);
            Equal(0, reupgraded.LastActiveWindow);
            var restored = reupgraded.Windows[0];
            SameWindow(first with
            {
                WindowWidth = 1820, Maximized = false,
                WindowX = null, WindowY = null, Hidden = false, Minimized = false
            }, restored);
            Equal("en", reupgraded.Language);
            Equal("custom", reupgraded.ColorTheme);
            Equal(false, reupgraded.GlobalToolbarPinned);
            Equal(true, downgradedBytes.SequenceEqual(File.ReadAllBytes(path)));
            Equal(true, original.SequenceEqual(File.ReadAllBytes(backupPath)));

            SessionStore.Save(path, reupgraded);
            var final = SessionStore.Load(path, out warning);
            Equal<string?>(null, warning);
            Equal(1, final.Windows.Count);
            SameWindow(restored, final.Windows[0]);
            Equal(JsonSerializer.Serialize(oldResaved), JsonSerializer.Serialize(Legacy0820SessionFixture.Read(path)));
            Equal(true, original.SequenceEqual(File.ReadAllBytes(backupPath)));
            Equal(2, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
        });
    }

    private static void PhysicalBoundsRoundtrip()
    {
        InDirectory(path =>
        {
            // Saved sizes are physical pixels. DPI scaling belongs only to fresh UI
            // defaults, never session normalization, migration or later session saves.
            File.WriteAllText(path, Legacy0820SessionFixture.Json);
            var state = SessionStore.Load(path);
            Equal(1943, state.Windows[0].WindowWidth);
            Equal(1129, state.Windows[0].WindowHeight);
            state = state with
            {
                Windows = [state.Windows[0] with
                {
                    WindowX = -2200, WindowY = -180, Maximized = false
                }, new BrowserWindowState
                {
                    WindowX = 2200, WindowY = 120, WindowWidth = 1943, WindowHeight = 1129,
                    Maximized = true, Hidden = true, Minimized = true
                }],
                LastActiveWindow = 1
            };
            var expected = SessionStore.Normalize(state);
            for (var save = 0; save < 2; save++)
            {
                SessionStore.Save(path, state);
                state = SessionStore.Load(path, out var warning);
                Equal<string?>(null, warning);
                Equal(2, state.Windows.Count);
                Equal(1, state.LastActiveWindow);
                for (var index = 0; index < state.Windows.Count; index++)
                {
                    Equal(1943, state.Windows[index].WindowWidth);
                    Equal(1129, state.Windows[index].WindowHeight);
                    SameWindow(expected.Windows[index], state.Windows[index]);
                }
                SameWindow(state.Windows[0], SessionStore.LegacyWindow(state));
            }
        });
    }

    private static void MigrationBackup()
    {
        InDirectory(path =>
        {
            var original = System.Text.Encoding.UTF8.GetBytes("{\r\n  \"Layout\": 2, \"WindowWidth\": 1750\r\n}");
            File.WriteAllBytes(path, original);
            var state = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(1750, state.Windows[0].WindowWidth);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path)));
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path + ".pre-multiwindow.bak")));
            File.WriteAllText(path, "{\"Layout\":3}");
            Equal(3, SessionStore.Load(path).Windows[0].Layout);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path + ".pre-multiwindow.bak")));
            SessionStore.Save(path, state);
            SessionStore.Load(path);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path + ".pre-multiwindow.bak")));
        });
    }

    private static void NoMigrationBackup()
    {
        InDirectory(path =>
        {
            foreach (var content in new[] { "null", "{invalid", "{\"Windows\":42}" })
            {
                File.WriteAllText(path, content);
                SessionStore.Load(path);
                Equal(false, File.Exists(path + ".pre-multiwindow.bak"));
                Equal(content, File.ReadAllText(path));
            }
            File.Delete(path);
            SessionStore.Load(path);
            Equal(false, File.Exists(path + ".pre-multiwindow.bak"));
            SessionStore.Save(path, new SessionState());
            SessionStore.Load(path);
            Equal(false, File.Exists(path + ".pre-multiwindow.bak"));
        });
    }

    private static void BackupFailure()
    {
        InDirectory(path =>
        {
            const string content = "{\"WindowWidth\":1750}";
            File.WriteAllText(path, content);
            Directory.CreateDirectory(path + ".pre-multiwindow.bak");
            var state = SessionStore.Load(path, out var warning);
            Equal(false, string.IsNullOrWhiteSpace(warning));
            Equal(1750, state.Windows[0].WindowWidth);
            Equal(content, File.ReadAllText(path));
        });
    }

    private static void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"QuadExplorer.WindowTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "session.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void SameWindow(BrowserWindowState expected, BrowserWindowState actual) =>
        Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
}
