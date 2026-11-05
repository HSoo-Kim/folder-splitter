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
        ("migration backup failures warn without losing loaded data", BackupFailure)
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
