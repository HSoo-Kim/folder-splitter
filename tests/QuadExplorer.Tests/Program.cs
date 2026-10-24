using QuadExplorer;

internal static class Program
{
    private static readonly string TestDirectory =
        Path.Combine(Path.GetTempPath(), $"QuadExplorer.Tests-{Guid.NewGuid():N}");

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("default session and storage path", Defaults),
            ("supported languages normalize with Korean default", LanguageNormalization),
            ("theme and tree preferences normalize and persist", AppearancePreferences),
            ("all selected languages persist without altering session data", LanguageRetention),
            ("Unicode session roundtrip and atomic replacement", Roundtrip),
            ("invalid and null JSON normalization", InvalidJsonData),
            ("null lists and null session normalization", NullLists),
            ("tab and favorite limits with case-insensitive deduplication", CollectionLimits),
            ("missing session remains missing", MissingFile),
            ("corrupt and null session files remain unchanged", CorruptFile),
            ("inaccessible session returns warning", InaccessibleFile),
            ("save failures propagate and clean temporary files", SaveFailure),
            ("inactive panes survive layout changes", InactivePanes),
            ("explicitly empty panes survive normalization and storage", EmptyPanes),
            ("deleted favorites normalize independently and preserve import state", DeletedFavorites),
            ("window sizes and ratios are bounded", Bounds),
            ("normalization does not mutate caller lists", DoesNotMutate),
            ("split tree sessions preserve sparse pane IDs and ratios", SplitTreeRoundtrip),
            ("legacy and malformed layouts normalize safely", LegacyLayouts)
        }.Concat(PaneLayoutTests.Cases).ToArray();

        var failures = 0;
        Directory.CreateDirectory(TestDirectory);
        try
        {
            foreach (var test in tests)
            {
                try
                {
                    test.Run();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                }
            }
        }
        finally
        {
            Directory.Delete(TestDirectory, recursive: true);
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Defaults()
    {
        var state = SessionStore.Normalize(new SessionState());
        Equal("ko", state.Language);
        Equal("light", state.ColorTheme);
        Equal(true, state.ShowFolderTree);
        Equal(1, state.Layout);
        Equal(PaneLayout.Leaf(0), state.LayoutTree);
        Equal(4, state.Panes.Count);
        Equal(1, state.Panes[0].Tabs.Count);
        Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), state.Panes[0].Tabs[0]);
        Equal(0, state.Panes[0].SelectedTab);
        foreach (var pane in state.Panes.Skip(1))
        {
            Equal(0, pane.Tabs.Count);
            Equal(-1, pane.SelectedTab);
        }
        Equal(1400, state.WindowWidth);
        Equal(900, state.WindowHeight);
        Equal(true, state.ShowSidebar);
        Equal(220, state.SidebarWidth);
        Equal(0, state.DeletedFavorites.Count);
        Equal(false, state.ExplorerFavoritesImported);
        Equal(false, state.Maximized);
        Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuadExplorer", "session.json"), SessionStore.DefaultPath);
    }

    private static void LanguageNormalization()
    {
        foreach (string? invalid in new string?[] { null, "", " ", "fr", "zh", "en-US", "../ja" })
            Equal("ko", SessionStore.Normalize(new SessionState { Language = invalid! }).Language);
        foreach (var (source, expected) in new[] { ("KO", "ko"), (" en ", "en"), ("ZH-cn", "zh-CN"), ("JA", "ja") })
            Equal(expected, SessionStore.Normalize(new SessionState { Language = source }).Language);
        var path = FilePath("language-default", "session.json");
        File.WriteAllText(path, """{"Language":null}""");
        Equal("ko", SessionStore.Load(path).Language);
        File.WriteAllText(path, "{}");
        Equal("ko", SessionStore.Load(path).Language);
    }

    private static void AppearancePreferences()
    {
        foreach (string? invalid in new string?[] { null, "", " ", "unsupported", "system" })
            Equal("light", SessionStore.Normalize(new SessionState { ColorTheme = invalid! }).ColorTheme);
        Equal("dark", SessionStore.Normalize(new SessionState { ColorTheme = " DARK " }).ColorTheme);
        foreach (string theme in SessionStore.ColorThemes)
            Equal(theme, SessionStore.Normalize(new SessionState { ColorTheme = theme.ToUpperInvariant() }).ColorTheme);
        Equal("normal", SessionStore.Normalize(new SessionState { TextSize = "huge" }).TextSize);
        Equal("large", SessionStore.Normalize(new SessionState { TextSize = "large" }).TextSize);
        Equal(null, SessionStore.Normalize(new SessionState { FontFamily = "  " }).FontFamily);
        Equal("Malgun Gothic", SessionStore.Normalize(new SessionState { FontFamily = " Malgun Gothic " }).FontFamily);
        var path = FilePath("appearance", "session.json");
        File.WriteAllText(path, "{}");
        var legacy = SessionStore.Load(path);
        Equal("light", legacy.ColorTheme);
        Equal(true, legacy.ShowFolderTree);
        Equal(true, legacy.ShowCommandBar);
        Equal(false, legacy.ShowVerticalSeparators);
        Equal(true, legacy.ComfortableRows);
        SessionStore.Save(path, legacy with
        {
            ColorTheme = "dark", ShowFolderTree = false, ShowCommandBar = false,
            ShowVerticalSeparators = true, ComfortableRows = false,
            Workspaces = [new Workspace { Name = "Appearance", Panes = legacy.Panes }]
        });
        var restored = SessionStore.Load(path);
        Equal("dark", restored.ColorTheme);
        Equal(false, restored.ShowFolderTree);
        Equal(false, restored.ShowCommandBar);
        Equal(true, restored.ShowVerticalSeparators);
        Equal(false, restored.ComfortableRows);
        Sequence(legacy.Panes[0].Tabs, restored.Workspaces[0].Panes[0].Tabs);
        Sequence(legacy.Panes[0].Tabs, restored.Panes[0].Tabs);
        Equal(legacy.LayoutTree, restored.LayoutTree);
        Equal("light", legacy.ColorTheme);
        Equal(true, legacy.ShowFolderTree);
    }

    private static void LanguageRetention()
    {
        var path = FilePath("languages", "session.json");
        var original = new SessionState
        {
            Panes = [new PaneState { Tabs = ["C:\\한국어\\文件\\日本語", "Z:\\offline"], SelectedTab = 1 }],
            Favorites = ["Z:\\favorite"], DeletedFavorites = ["Z:\\removed"], SidebarWidth = 315,
            LayoutTree = PaneLayout.Leaf(0).Split(0, 3, SplitEdge.Bottom), ActivePane = 3
        };
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            SessionStore.Save(path, original with { Language = language });
            var restored = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(language, restored.Language);
            Sequence(original.Panes[0].Tabs, restored.Panes[0].Tabs);
            Equal(1, restored.Panes[0].SelectedTab);
            Sequence(original.Favorites, restored.Favorites);
            Sequence(original.DeletedFavorites, restored.DeletedFavorites);
            Equal(original.LayoutTree, restored.LayoutTree);
            Equal(315, restored.SidebarWidth);
            Equal("ko", original.Language);
        }
    }

    private static void Roundtrip()
    {
        var path = FilePath("roundtrip", "nested", "session.json");
        var unicodePath = "C:\\\u6587\u4ef6\\\u00e9t\u00e9\\\ud83d\udcc1";
        var offlinePath = "\\\\offline-server\\share\\folder";
        var expected = SessionStore.Normalize(new SessionState
        {
            Layout = 2,
            Panes =
            [
                new PaneState { Tabs = [unicodePath, offlinePath], SelectedTab = 1 },
                new PaneState { Tabs = ["Z:\\disconnected"] },
                new PaneState { Tabs = ["C:\\hidden-left"] },
                new PaneState { Tabs = ["C:\\hidden-right"] }
            ],
            Favorites = [unicodePath, offlinePath],
            DeletedFavorites = ["Z:\\removed", unicodePath],
            ExplorerFavoritesImported = true,
            SidebarWidth = 315,
            ActivePane = 1,
            WindowWidth = 1777,
            WindowHeight = 888,
            Maximized = true,
            ShowSidebar = false,
            ColumnRatio = .33,
            LeftRowRatio = .4,
            RightRowRatio = .7
        });
        SessionStore.Save(path, new SessionState());
        SessionStore.Save(path, expected);
        var actual = SessionStore.Load(path, out var warning);
        Equal<string?>(null, warning);
        Equal(expected.Layout, actual.Layout);
        Equal(expected.ActivePane, actual.ActivePane);
        Equal(expected.WindowWidth, actual.WindowWidth);
        Equal(expected.WindowHeight, actual.WindowHeight);
        Equal(expected.Maximized, actual.Maximized);
        Equal(expected.ShowSidebar, actual.ShowSidebar);
        Equal(expected.SidebarWidth, actual.SidebarWidth);
        Equal(expected.ExplorerFavoritesImported, actual.ExplorerFavoritesImported);
        Equal(expected.ColumnRatio, actual.ColumnRatio);
        Equal(expected.LeftRowRatio, actual.LeftRowRatio);
        Equal(expected.RightRowRatio, actual.RightRowRatio);
        Sequence(expected.Favorites, actual.Favorites);
        Sequence(expected.DeletedFavorites, actual.DeletedFavorites);
        for (var index = 0; index < 4; index++)
        {
            Sequence(expected.Panes[index].Tabs, actual.Panes[index].Tabs);
            Equal(expected.Panes[index].SelectedTab, actual.Panes[index].SelectedTab);
        }
        Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
        Sequence(actual.Favorites, SessionStore.Load(path).Favorites);
    }

    private static void InvalidJsonData()
    {
        var path = FilePath("invalid", "session.json");
        File.WriteAllText(path, """
            {
              "Layout": 5,
              "Panes": [
                null,
                { "Tabs": null, "SelectedTab": 50 },
                { "Tabs": [null, "", "  ", "Z:\\offline"], "SelectedTab": -2 },
                { "Tabs": ["C:\\one", "C:\\two"], "SelectedTab": 999 },
                { "Tabs": ["C:\\extra"] }
              ],
              "Favorites": [null, "", "  ", "C:\\Keep", "c:\\keep", "\\\\offline\\share"],
              "ActivePane": 99,
              "WindowWidth": -1,
              "WindowHeight": 99999
            }
            """);
        var state = SessionStore.Load(path, out var warning);
        Equal<string?>(null, warning);
        Equal(4, state.Layout);
        Equal(4, state.Panes.Count);
        Equal(1, state.Panes[0].Tabs.Count);
        Equal(0, state.Panes[1].Tabs.Count);
        Equal(-1, state.Panes[1].SelectedTab);
        Equal("Z:\\offline", state.Panes[2].Tabs[0]);
        Equal(0, state.Panes[2].SelectedTab);
        Equal(1, state.Panes[3].SelectedTab);
        Sequence(["C:\\Keep", "\\\\offline\\share"], state.Favorites);
        Equal(3, state.ActivePane);
        Equal(900, state.WindowWidth);
        Equal(10000, state.WindowHeight);
    }

    private static void NullLists()
    {
        var path = FilePath("null-lists", "session.json");
        File.WriteAllText(path, """{"Panes":null,"Favorites":null,"DeletedFavorites":null}""");
        var state = SessionStore.Load(path, out var warning);
        Equal<string?>(null, warning);
        Equal(4, state.Panes.Count);
        Equal(0, state.Favorites.Count);
        Equal(0, state.DeletedFavorites.Count);
        Equal(220, state.SidebarWidth);
        Equal(false, state.ExplorerFavoritesImported);
        Equal(4, SessionStore.Normalize(null!).Panes.Count);
        var nullTabs = SessionStore.Normalize(new SessionState
        {
            Panes = [new PaneState { Tabs = null! }, null!],
            Favorites = null!
        });
        Equal(0, nullTabs.Panes[0].Tabs.Count);
        Equal(-1, nullTabs.Panes[0].SelectedTab);
        Equal(0, nullTabs.Panes[1].Tabs.Count);
        Equal(-1, nullTabs.Panes[1].SelectedTab);
    }

    private static void CollectionLimits()
    {
        var tabs = Enumerable.Range(0, 50).Select(index => $"Z:\\offline\\{index}").ToList();
        tabs.Insert(0, null!);
        tabs.Insert(1, " ");
        var favorites = new List<string> { null!, "", "C:\\same", "c:\\SAME" };
        favorites.AddRange(Enumerable.Range(0, 150).Select(index => $"Z:\\favorite\\{index}"));
        var state = SessionStore.Normalize(new SessionState
        {
            Panes = [new PaneState { Tabs = tabs, SelectedTab = int.MaxValue }],
            Favorites = favorites
        });
        Equal(32, state.Panes[0].Tabs.Count);
        Equal("Z:\\offline\\0", state.Panes[0].Tabs[0]);
        Equal("Z:\\offline\\31", state.Panes[0].Tabs[31]);
        Equal(31, state.Panes[0].SelectedTab);
        Equal(100, state.Favorites.Count);
        Equal("C:\\same", state.Favorites[0]);
        Equal("Z:\\favorite\\98", state.Favorites[99]);
    }

    private static void MissingFile()
    {
        var path = FilePath("missing", "uncreated", "session.json");
        var state = SessionStore.Load(path, out var warning);
        HasWarning(warning);
        Equal(4, state.Panes.Count);
        Equal(false, File.Exists(path));
        Equal(false, Directory.Exists(Path.GetDirectoryName(path)));
    }

    private static void CorruptFile()
    {
        var path = FilePath("corrupt", "session.json");
        foreach (var content in new[] { "{not valid JSON", "null", """{"Panes":42}""" })
        {
            File.WriteAllText(path, content);
            var original = File.ReadAllBytes(path);
            var state = SessionStore.Load(path, out var warning);
            HasWarning(warning);
            Equal(4, state.Panes.Count);
            Sequence(original, File.ReadAllBytes(path));
        }
    }

    private static void InaccessibleFile()
    {
        var path = FilePath("inaccessible", "session.json");
        Directory.CreateDirectory(path);
        var state = SessionStore.Load(path, out var warning);
        HasWarning(warning);
        Equal(4, state.Panes.Count);
        Equal(true, Directory.Exists(path));
        Equal(0, Directory.GetFileSystemEntries(path).Length);
    }

    private static void SaveFailure()
    {
        var path = FilePath("save-failure", "session.json");
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
            Throws<UnauthorizedAccessException>(() => SessionStore.Save(path, new SessionState()));
        else
            Throws<IOException>(() => SessionStore.Save(path, new SessionState()));
        Equal(true, Directory.Exists(path));
        Equal(1, Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!).Length);

        var blockedParent = FilePath("save-parent-failure", "file");
        File.WriteAllText(blockedParent, "preserve");
        Throws<IOException>(() => SessionStore.Save(Path.Combine(blockedParent, "session.json"), new SessionState()));
        Equal("preserve", File.ReadAllText(blockedParent));
    }

    private static void InactivePanes()
    {
        var original = new SessionState
        {
            Panes = Enumerable.Range(0, 4).Select(index => new PaneState
            {
                Tabs = [$"Z:\\pane-{index}", $"Z:\\pane-{index}\\second"],
                SelectedTab = 1
            }).ToList(),
            ActivePane = 3
        };
        var path = FilePath("inactive", "session.json");
        foreach (var layout in new[] { 1, 2, 3, 4 })
        {
            SessionStore.Save(path, original with { Layout = layout, LayoutTree = null });
            var state = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(layout, state.Layout);
            Equal(layout - 1, state.ActivePane);
            Equal(4, state.Panes.Count);
            for (var index = 0; index < 4; index++)
            {
                Sequence(original.Panes[index].Tabs, state.Panes[index].Tabs);
                Equal(1, state.Panes[index].SelectedTab);
            }
            original = state with { ActivePane = 3 };
        }
        Equal(0, SessionStore.Normalize(original with { ActivePane = -1 }).ActivePane);
    }

    private static void Bounds()
    {
        var state = SessionStore.Normalize(new SessionState
        {
            WindowWidth = int.MinValue,
            WindowHeight = int.MaxValue,
            SidebarWidth = int.MinValue,
            ColumnRatio = -1,
            LeftRowRatio = 2,
            RightRowRatio = .6
        });
        Equal(900, state.WindowWidth);
        Equal(10000, state.WindowHeight);
        Equal(140, state.SidebarWidth);
        Equal(.15, state.ColumnRatio);
        Equal(.85, state.LeftRowRatio);
        Equal(.6, state.RightRowRatio);
        state = SessionStore.Normalize(state with
        {
            WindowWidth = int.MaxValue, WindowHeight = int.MinValue, SidebarWidth = int.MaxValue
        });
        Equal(10000, state.WindowWidth);
        Equal(600, state.WindowHeight);
        Equal(480, state.SidebarWidth);
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            state = SessionStore.Normalize(state with
            {
                ColumnRatio = invalid,
                LeftRowRatio = invalid,
                RightRowRatio = invalid
            });
            Equal(.5, state.ColumnRatio);
            Equal(.5, state.LeftRowRatio);
            Equal(.5, state.RightRowRatio);
        }
    }

    private static void DoesNotMutate()
    {
        var original = new SessionState
        {
            Panes = [new PaneState { Tabs = ["C:\\one"], SelectedTab = 20 }],
            Favorites = ["C:\\one", "c:\\ONE"],
            DeletedFavorites = ["C:\\removed", "c:\\REMOVED"]
        };
        var normalized = SessionStore.Normalize(original);
        normalized.Panes[0].Tabs.Add("C:\\two");
        normalized.Favorites.Clear();
        normalized.DeletedFavorites.Clear();
        Equal(1, original.Panes.Count);
        Equal(1, original.Panes[0].Tabs.Count);
        Equal(20, original.Panes[0].SelectedTab);
        Equal(2, original.Favorites.Count);
        Equal(2, original.DeletedFavorites.Count);
    }

    private static void EmptyPanes()
    {
        var path = FilePath("empty-panes", "session.json");
        foreach (var layout in new[] { 1, 2, 3, 4 })
        {
            var original = new SessionState
            {
                Layout = layout,
                Panes =
                [
                    new PaneState { Tabs = [], SelectedTab = 99 },
                    new PaneState { Tabs = [" ", "", null!], SelectedTab = int.MinValue },
                    new PaneState { Tabs = ["Z:\\cached", "Z:\\cached\\second"], SelectedTab = 1 },
                    new PaneState()
                ]
            };
            SessionStore.Save(path, original);
            var restored = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(layout, restored.Layout);
            foreach (var index in new[] { 0, 1, 3 })
            {
                Equal(0, restored.Panes[index].Tabs.Count);
                Equal(-1, restored.Panes[index].SelectedTab);
            }
            Sequence(original.Panes[2].Tabs, restored.Panes[2].Tabs);
            Equal(1, restored.Panes[2].SelectedTab);
            var normalizedAgain = SessionStore.Normalize(restored);
            for (var index = 0; index < 4; index++)
            {
                Sequence(restored.Panes[index].Tabs, normalizedAgain.Panes[index].Tabs);
                Equal(restored.Panes[index].SelectedTab, normalizedAgain.Panes[index].SelectedTab);
            }
        }
        SessionStore.Save(path, new SessionState { Panes = [new PaneState()] });
        var allEmpty = SessionStore.Load(path);
        Equal(4, allEmpty.Panes.Count);
        foreach (var pane in allEmpty.Panes)
        {
            Equal(0, pane.Tabs.Count);
            Equal(-1, pane.SelectedTab);
        }
    }

    private static void DeletedFavorites()
    {
        var removed = new List<string> { null!, "", " ", "C:\\removed", "c:\\REMOVED" };
        removed.AddRange(Enumerable.Range(0, 150).Select(index => $"Z:\\removed\\{index}"));
        var normalized = SessionStore.Normalize(new SessionState
        {
            Favorites = ["C:\\removed"],
            DeletedFavorites = removed,
            ExplorerFavoritesImported = true
        });
        Equal(100, normalized.DeletedFavorites.Count);
        Equal("C:\\removed", normalized.DeletedFavorites[0]);
        Equal("Z:\\removed\\98", normalized.DeletedFavorites[99]);
        Sequence(["C:\\removed"], normalized.Favorites);
        Equal(true, normalized.ExplorerFavoritesImported);
        Equal(0, SessionStore.Normalize(new SessionState { DeletedFavorites = null! }).DeletedFavorites.Count);
    }

    private static void SplitTreeRoundtrip()
    {
        var tree = PaneLayout.Leaf(0).Split(0, 1, SplitEdge.Right)
            .Split(1, 3, SplitEdge.Bottom).SetRatio("", .35).SetRatio("1", .62);
        var state = new SessionState
        {
            LayoutTree = tree,
            ActivePane = 3,
            Panes = Enumerable.Range(0, 4).Select(i => new PaneState { Tabs = [$"Z:\\pane-{i}"] }).ToList()
        };
        var path = FilePath("split-tree", "session.json");
        SessionStore.Save(path, state);
        var restored = SessionStore.Load(path, out var warning);
        Equal<string?>(null, warning);
        Equal(3, restored.Layout);
        Equal(3, restored.ActivePane);
        Equal(tree, restored.LayoutTree);
        Equal("Z:\\pane-2", restored.Panes[2].Tabs[0]);
        Equal(0, SessionStore.Normalize(state with { ActivePane = 2 }).ActivePane);
        Equal(3, SessionStore.Normalize(state with { LayoutTree = PaneLayout.Leaf(3) }).ActivePane);
    }

    private static void LegacyLayouts()
    {
        var legacy = SessionStore.Normalize(new SessionState
        {
            Layout = 4, ColumnRatio = .3, LeftRowRatio = .4, RightRowRatio = .7
        });
        Equal(.3, legacy.LayoutTree!.Ratio);
        Equal(.4, legacy.LayoutTree.First!.Ratio);
        Equal(.7, legacy.LayoutTree.Second!.Ratio);
        var three = SessionStore.Normalize(new SessionState { Layout = 3, ActivePane = 99 });
        Equal(3, three.Layout);
        Equal(2, three.ActivePane);
        Equal(PaneLayout.Preset(3), three.LayoutTree);
        var malformed = SessionStore.Normalize(new SessionState
        {
            Layout = 2, LayoutTree = new PaneLayout { First = PaneLayout.Leaf(0), Second = PaneLayout.Leaf(0) }
        });
        Equal(PaneLayout.Preset(2), malformed.LayoutTree);
        var leaf = SessionStore.Normalize(new SessionState { LayoutTree = PaneLayout.Leaf(0) with { Ratio = double.NaN } });
        Equal(.5, leaf.LayoutTree!.Ratio);
        SessionStore.Save(FilePath("leaf-ratio", "session.json"), leaf);
    }

    private static string FilePath(string test, params string[] parts)
    {
        var directory = Path.Combine(TestDirectory, test);
        Directory.CreateDirectory(directory);
        return Path.Combine([directory, .. parts]);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    private static void HasWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            throw new InvalidOperationException("Expected load warning.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
