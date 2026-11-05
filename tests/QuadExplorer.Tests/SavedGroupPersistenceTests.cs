using QuadExplorer;
using System.Text.Json;

internal static class SavedGroupPersistenceTests
{
    public static readonly (string Name, Action Run)[] Cases =
    [
        ("saved groups default empty in legacy sessions", Legacy),
        ("saved groups normalize malformed entries without touching caller data", Normalize),
        ("saved groups retain newest thirty and twelve ordered paths including duplicates", Limits),
        ("saved group normalization is idempotent and preserves stable links", StableLinks),
        ("saved groups roundtrip independently of open windows and workspace links", Roundtrip),
        ("saved group sessions tolerate future fields and downgrade readers ignore the catalog", Compatibility)
    ];

    private static void Legacy()
    {
        var legacy = JsonSerializer.Deserialize<SessionState>("""{"Panes":[{"Tabs":["Z:\\offline"]}]}""")!;
        Equal(0, SessionStore.Normalize(legacy).SavedGroups.Count);
        Equal(0, SessionStore.Normalize(new SessionState { SavedGroups = null! }).SavedGroups.Count);
        Equal<string?>(null, SessionStore.Normalize(legacy).Panes[0].Groups.FirstOrDefault()?.SavedId);
    }

    private static void Normalize()
    {
        var source = new List<SavedTabGroup>
        {
            null!, new() { Id = "" }, new() { Id = "empty", Paths = null! },
            new() { Id = " stable ", Name = " " + new string('n', 45) + " ", Color = " purple ",
                Paths = [" ", @"Z:\missing", @"Z:\missing", null!, @"C:\next"] },
            new() { Id = "stable", Name = "duplicate", Paths = [@"C:\ignored"] },
            new() { Id = "other", Name = null!, Color = "future", Paths = [@"C:\last"] },
            new() { Id = new string('x', 129), Paths = [@"C:\invalid"] }
        };
        var result = SessionStore.NormalizeSavedGroups(source);
        Equal(2, result.Count);
        Equal("stable", result[0].Id);
        Equal(new string('n', 40), result[0].Name);
        Equal("Purple", result[0].Color);
        Sequence([@"Z:\missing", @"Z:\missing", @"C:\next"], result[0].Paths);
        Equal("", result[1].Name);
        Equal("Grey", result[1].Color);
        Equal(7, source.Count);
        Equal(" stable ", source[3].Id);
        Equal(5, source[3].Paths.Count);
        if (ReferenceEquals(result[0].Paths, source[3].Paths)) throw new Exception("Normalized catalog reuses caller paths.");
    }

    private static void Limits()
    {
        var source = Enumerable.Range(0, 35).Select(i => new SavedTabGroup
        {
            Id = "newest-" + i, Name = "group-" + i,
            Paths = Enumerable.Range(0, 15).Select(j => @"Z:\" + j).ToList()
        }).ToList();
        var result = SessionStore.NormalizeSavedGroups(source);
        Equal(SessionStore.SavedGroupLimit, result.Count);
        Equal("newest-0", result[0].Id);
        Equal("newest-29", result[^1].Id);
        Sequence(source[0].Paths.Take(12), result[0].Paths);
        Equal(35, source.Count);
        Equal(15, source[0].Paths.Count);
    }

    private static SessionState LinkedState() => new()
    {
        Language = "en",
        SavedGroups = [new() { Id = "stable", Name = "Work", Color = "Cyan", Paths = [@"Z:\offline", @"C:\work"] }],
        Windows = [new() { Panes = [new() { Tabs = [@"Z:\offline", @"C:\work"],
            Groups = [new() { SavedId = " stable ", Name = "Work", Color = "Cyan", Count = 2 }] }] }],
        Workspaces = [new() { Name = "Saved workspace", Panes = [new() { Tabs = [@"C:\work"],
            Groups = [new() { SavedId = "stable", Count = 1 }] }] }]
    };

    private static void StableLinks()
    {
        var first = SessionStore.Normalize(LinkedState());
        Equal("stable", first.Windows[0].Panes[0].Groups[0].SavedId);
        Equal("stable", first.Panes[0].Groups[0].SavedId);
        Equal("stable", first.Workspaces[0].Panes[0].Groups[0].SavedId);
        Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(SessionStore.Normalize(first)));
    }

    private static void Roundtrip()
    {
        WithFile(path =>
        {
            SessionStore.Save(path, LinkedState());
            var state = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal("stable", state.SavedGroups.Single().Id);
            Sequence([@"Z:\offline", @"C:\work"], state.SavedGroups[0].Paths);
            Equal("stable", state.Windows[0].Panes[0].Groups[0].SavedId);
            Equal("stable", state.Workspaces[0].Panes[0].Groups[0].SavedId);
            // Closed groups need not have any corresponding open range.
            SessionStore.Save(path, state with { Windows = [new() { Panes = [new() { Tabs = [] }] }] });
            Equal("stable", SessionStore.Load(path).SavedGroups.Single().Id);
        });
    }

    private static void Compatibility()
    {
        WithFile(path =>
        {
            File.WriteAllText(path, """
                {"Windows":[{"Panes":[{"Tabs":["Z:\\offline"],"Groups":[{"Start":0,"Count":1,"SavedId":"future","Future":true}]}]}],
                 "SavedGroups":[{"Id":"future","Name":"Future","Color":"Pink","Paths":["Z:\\offline"],"Future":{"Version":42}}],
                 "FutureSession":true}
                """);
            var current = SessionStore.Load(path);
            Equal("future", current.SavedGroups.Single().Id);
            Equal("future", current.Windows[0].Panes[0].Groups.Single().SavedId);
            SessionStore.Save(path, current);
            var older = JsonSerializer.Deserialize<PreSavedSession>(File.ReadAllText(path))!;
            Sequence([@"Z:\offline"], older.Panes[0].Tabs);
            File.WriteAllText(path, JsonSerializer.Serialize(older));
            var downgraded = SessionStore.Load(path);
            Equal(0, downgraded.SavedGroups.Count);
            Sequence([@"Z:\offline"], downgraded.Panes[0].Tabs);
        });
    }

    private sealed record PreSavedSession
    {
        public List<PreSavedPane> Panes { get; init; } = [];
    }
    private sealed record PreSavedPane
    {
        public List<string> Tabs { get; init; } = [];
    }
    private static void WithFile(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SavedGroups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "session.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}.");
    }
    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual)) throw new Exception("Sequences differ.");
    }
}
