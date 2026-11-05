using QuadExplorer;

internal static class TabGroupPersistenceTests
{
    public static (string Name, Action Run)[] Cases =
    [
        ("legacy sessions retain tabs and selection without groups", LegacySessions),
        ("null group data loads without losing otherwise valid panes", NullGroupData),
        ("group ranges reject invalid bounds and overlaps in original slot order", InvalidRanges),
        ("group ranges follow surviving tabs when blank slots are removed", BlankSlotRemapping),
        ("groups obey member and pane limits without claiming unrelated tabs", GroupLimits),
        ("group names and all nine persisted colors normalize", GroupMetadata),
        ("group normalization preserves caller data and is idempotent", DoesNotMutate),
        ("groups roundtrip across active and hidden panes", SessionRoundtrip),
        ("workspace groups share session normalization and storage", WorkspaceRoundtrip)
    ];

    private static void LegacySessions()
    {
        foreach (var pane in SessionStore.Normalize(new SessionState()).Panes)
            Equal(0, pane.Groups.Count);
        WithSessionFile(path =>
        {
            File.WriteAllText(path, """
                {
                  "Panes":[{"Tabs":["Z:\\offline","Z:\\offline","C:\\other"],"SelectedTab":2}],
                  "Workspaces":[{"Name":"legacy","Panes":[{"Tabs":["C:\\legacy"]}]}]
                }
                """);
            var state = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Sequence([@"Z:\offline", @"Z:\offline", @"C:\other"], state.Panes[0].Tabs);
            Equal(2, state.Panes[0].SelectedTab);
            foreach (var pane in state.Panes)
                Equal(0, pane.Groups.Count);
            Sequence([@"C:\legacy"], state.Workspaces[0].Panes[0].Tabs);
            foreach (var pane in state.Workspaces[0].Panes)
                Equal(0, pane.Groups.Count);
            SessionStore.Save(path, state);
            var restored = SessionStore.Load(path);
            EqualPane(state.Panes[0], restored.Panes[0]);
            EqualPane(state.Workspaces[0].Panes[0], restored.Workspaces[0].Panes[0]);
        });
    }

    private static void NullGroupData()
    {
        WithSessionFile(path =>
        {
            File.WriteAllText(path, """
                {
                  "Panes":[
                    {"Tabs":["C:\\one"],"Groups":null},
                    {"Tabs":null,"Groups":[null,{"Start":0,"Count":1}]},
                    {"Tabs":["C:\\two","C:\\three"],"Groups":[null,{},
                      {"Name":null,"Color":null,"Collapsed":true,"Start":0,"Count":2}]},
                    null
                  ]
                }
                """);
            var state = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Sequence([@"C:\one"], state.Panes[0].Tabs);
            Equal(0, state.Panes[0].Groups.Count);
            Equal(0, state.Panes[1].Tabs.Count);
            Equal(0, state.Panes[1].Groups.Count);
            Equal(-1, state.Panes[1].SelectedTab);
            Sequence([new TabGroupState { Color = "Grey", Collapsed = true, Start = 0, Count = 2 }],
                state.Panes[2].Groups);
            Equal(0, state.Panes[3].Groups.Count);
        });
        var empty = NormalizePane(new PaneState { Tabs = null!, Groups = null! });
        Equal(0, empty.Tabs.Count);
        Equal(0, empty.Groups.Count);
    }

    private static void InvalidRanges()
    {
        var pane = NormalizePane(new PaneState
        {
            Tabs = Paths(8),
            Groups =
            [
                null!,
                new() { Start = -1, Count = 1 },
                new() { Start = int.MinValue, Count = int.MaxValue },
                new() { Start = 0, Count = 0 },
                new() { Start = 0, Count = -1 },
                new() { Start = 8, Count = 1 },
                new() { Start = int.MaxValue, Count = 2 },
                new() { Start = 7, Count = 2 },
                new() { Start = 1, Count = int.MaxValue },
                new() { Name = "later", Start = 4, Count = 2 },
                new() { Name = "overlap", Start = 3, Count = 2 },
                new() { Name = "duplicate", Start = 4, Count = 2 },
                new() { Name = "earlier", Start = 0, Count = 4 },
                new() { Name = "last", Start = 6, Count = 2 }
            ]
        });
        Sequence(
        [
            new TabGroupState { Name = "earlier", Start = 0, Count = 4 },
            new TabGroupState { Name = "later", Start = 4, Count = 2 },
            new TabGroupState { Name = "last", Start = 6, Count = 2 }
        ], pane.Groups);
        Sequence(Paths(8), pane.Tabs);
    }

    private static void BlankSlotRemapping()
    {
        var pane = NormalizePane(new PaneState
        {
            Tabs = [null!, @"C:\a", "", @"C:\b", null!, @"C:\c", "  ", @"C:\d", "", @"C:\e"],
            Groups =
            [
                new() { Name = "empty", Start = 0, Count = 1 },
                new() { Name = "first", Start = 1, Count = 4 },
                new() { Name = "overlap", Start = 3, Count = 3 },
                new() { Name = "second", Start = 5, Count = 3 },
                new() { Name = "third", Start = 8, Count = 2 },
                new() { Name = "outside original list", Start = 2, Count = 10 }
            ]
        });
        Sequence([@"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e"], pane.Tabs);
        Sequence(
        [
            new TabGroupState { Name = "first", Start = 0, Count = 2 },
            new TabGroupState { Name = "second", Start = 2, Count = 2 },
            new TabGroupState { Name = "third", Start = 4, Count = 1 }
        ], pane.Groups);
        var blankOnly = NormalizePane(new PaneState
        {
            Tabs = [null!, "", " "], Groups = [new() { Start = 0, Count = 3 }]
        });
        Equal(0, blankOnly.Groups.Count);
        Equal(0, blankOnly.Tabs.Count);
    }

    private static void GroupLimits()
    {
        var pane = NormalizePane(new PaneState
        {
            Tabs = Paths(40),
            Groups =
            [
                new() { Name = "large", Start = 0, Count = 20 },
                new() { Name = "tail", Start = 30, Count = 5 },
                new() { Name = "discarded tabs", Start = 35, Count = 5 }
            ]
        });
        Sequence(Paths(32), pane.Tabs);
        Sequence(
        [
            new TabGroupState { Name = "large", Start = 0, Count = 12 },
            new TabGroupState { Name = "tail", Start = 30, Count = 2 }
        ], pane.Groups);

        var tabs = Paths(33);
        tabs.InsertRange(0, ["", null!, " "]);
        var shifted = NormalizePane(new PaneState
        {
            Tabs = tabs,
            Groups = [new() { Name = "last saved", Start = 33, Count = 3 }]
        });
        Sequence(Paths(32), shifted.Tabs);
        Sequence([new TabGroupState { Name = "last saved", Start = 30, Count = 2 }], shifted.Groups);
        var invalidOversized = NormalizePane(new PaneState
        {
            Tabs = Paths(12), Groups = [new() { Start = 0, Count = 13 }]
        });
        Equal(0, invalidOversized.Groups.Count);
    }

    private static void GroupMetadata()
    {
        string[] colors = ["Grey", "Blue", "Red", "Yellow", "Green", "Pink", "Purple", "Cyan", "Orange"];
        var groups = colors.Select((color, index) => new TabGroupState
        {
            Name = index == 0 ? "  " + new string('\uac00', 45) + "  " : "  Group " + index + "  ",
            Color = " " + color.ToLowerInvariant() + " ",
            Collapsed = index % 2 == 0,
            Start = index,
            Count = 1
        }).ToList();
        groups.Add(new TabGroupState { Name = null!, Color = null!, Start = 9, Count = 1 });
        groups.Add(new TabGroupState { Name = " \t ", Color = "magenta", Start = 10, Count = 1 });
        groups.Add(new TabGroupState { Color = "1", Start = 11, Count = 1 });
        var pane = NormalizePane(new PaneState { Tabs = Paths(12), Groups = groups });
        Sequence(colors.Concat(["Grey", "Grey", "Grey"]), pane.Groups.Select(group => group.Color));
        Equal(new string('\uac00', 40), pane.Groups[0].Name);
        for (int index = 1; index < colors.Length; index++)
            Equal("Group " + index, pane.Groups[index].Name);
        foreach (var group in pane.Groups.Skip(9))
            Equal("", group.Name);
        Sequence(groups.Select(group => group.Collapsed), pane.Groups.Select(group => group.Collapsed));
    }

    private static void DoesNotMutate()
    {
        var tabs = new List<string> { "", @"C:\one", null!, @"C:\two", @"C:\three" };
        var groups = new List<TabGroupState>
        {
            new() { Name = "  second  ", Color = "purple", Start = 4, Count = 1 },
            null!,
            new() { Name = "  first  ", Color = "invalid", Start = 1, Count = 3 }
        };
        var originalTabs = tabs.ToArray();
        var originalGroups = groups.ToArray();
        var source = new PaneState { Tabs = tabs, Groups = groups, SelectedTab = 1 };
        var normalized = NormalizePane(source);
        Sequence(originalTabs, source.Tabs);
        Sequence(originalGroups, source.Groups);
        Equal(1, source.SelectedTab);
        EqualPane(normalized, NormalizePane(normalized));
        Sequence(["first", "second"], normalized.Groups.Select(group => group.Name));
    }

    private static void SessionRoundtrip()
    {
        WithSessionFile(path =>
        {
            var source = new SessionState
            {
                Layout = 1,
                Panes =
                [
                    new()
                    {
                        Tabs = [@"Z:\offline", "", "C:\\\uac00\uacc4", @"C:\ungrouped", @"C:\final"],
                        SelectedTab = 1,
                        Groups =
                        [
                            new() { Name = "\uc5c5\ubb34 \u00e9", Color = "Blue", Collapsed = true, Start = 0, Count = 3 },
                            new() { Color = "Orange", Start = 4, Count = 1 }
                        ]
                    },
                    new() { Tabs = [], Groups = [] },
                    new() { Tabs = [@"C:\alone"] },
                    new()
                    {
                        Tabs = [@"C:\hidden"],
                        Groups = [new() { Name = "hidden pane", Color = "Cyan", Start = 0, Count = 1 }]
                    }
                ]
            };
            var expected = SessionStore.Normalize(source);
            SessionStore.Save(path, source);
            var restored = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            for (int index = 0; index < 4; index++)
                EqualPane(expected.Panes[index], restored.Panes[index]);
            Equal(true, restored.Panes[0].Groups[0].Collapsed);
            Equal("\uc5c5\ubb34 \u00e9", restored.Panes[0].Groups[0].Name);
            Equal(1, restored.Layout);
            Equal("hidden pane", restored.Panes[3].Groups[0].Name);
            SessionStore.Save(path, restored);
            var repeated = SessionStore.Load(path, out warning);
            Equal<string?>(null, warning);
            for (int index = 0; index < 4; index++)
                EqualPane(expected.Panes[index], repeated.Panes[index]);
        });
    }

    private static void WorkspaceRoundtrip()
    {
        var workspace = new Workspace
        {
            Name = "  Work \u5de5\u4f5c  ",
            LayoutTree = new PaneLayout { Vertical = true, Ratio = .4, First = PaneLayout.Leaf(0), Second = PaneLayout.Leaf(3) },
            ActivePane = 3,
            Panes =
            [
                new()
                {
                    Tabs = ["", @"C:\work", null!, @"C:\reference", @"C:\outside"],
                    SelectedTab = 1,
                    Groups =
                    [
                        null!,
                        new() { Name = "  Research  ", Color = "purple", Collapsed = true, Start = 1, Count = 3 },
                        new() { Start = 2, Count = 3 },
                        new() { Start = -1, Count = 1 }
                    ]
                },
                new() { Tabs = null!, Groups = null! },
                null!,
                new()
                {
                    Tabs = [@"Z:\hidden"],
                    Groups = [new() { Name = "Unnamed color", Color = "unknown", Start = 0, Count = 1 }]
                }
            ]
        };
        var expected = SessionStore.NormalizeWorkspace(workspace);
        Equal("Work \u5de5\u4f5c", expected.Name);
        Sequence([new TabGroupState { Name = "Research", Color = "Purple", Collapsed = true, Start = 0, Count = 2 }],
            expected.Panes[0].Groups);
        Equal(0, expected.Panes[1].Groups.Count);
        Equal(0, expected.Panes[2].Groups.Count);
        Equal("Grey", expected.Panes[3].Groups[0].Color);
        Equal(3, expected.ActivePane);
        WithSessionFile(path =>
        {
            SessionStore.Save(path, new SessionState { Workspaces = [null!, workspace] });
            var restored = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(1, restored.Workspaces.Count);
            var actual = restored.Workspaces[0];
            Equal(expected.Name, actual.Name);
            Equal(expected.LayoutTree, actual.LayoutTree);
            Equal(expected.ActivePane, actual.ActivePane);
            for (int index = 0; index < 4; index++)
                EqualPane(expected.Panes[index], actual.Panes[index]);
        });
        Equal(5, workspace.Panes[0].Tabs.Count);
        Equal("  Research  ", workspace.Panes[0].Groups[1].Name);
    }

    private static PaneState NormalizePane(PaneState pane) =>
        SessionStore.Normalize(new SessionState { Panes = [pane] }).Panes[0];

    private static List<string> Paths(int count) =>
        Enumerable.Range(0, count).Select(index => $@"Z:\offline\{index}").ToList();

    private static void EqualPane(PaneState expected, PaneState actual)
    {
        Sequence(expected.Tabs, actual.Tabs);
        Equal(expected.SelectedTab, actual.SelectedTab);
        Sequence(expected.Groups, actual.Groups);
    }

    private static void WithSessionFile(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"QuadExplorer.TabGroups-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "session.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}]; got [{string.Join(", ", actual)}].");
    }
}
