using QuadExplorer;

internal static class FolderVisitsTests
{
    public static (string Name, Action Run)[] Cases =
    [
        ("folder visits keep only folders below a drive or share root", PathNormalization),
        ("folder visits count case-insensitively and list the newest first", CountingAndOrder),
        ("frequent folders need three visits and rank by count then recency", FrequentRanking),
        ("folder visit list is bounded and keeps the newest and the busiest folder", Bounds),
        ("folder visit counts age so old habits fade", Aging),
        ("saved folder visits normalize invalid, duplicate and oversized entries", SavedEntries),
        ("folder visits persist in the session and older sessions load without them", SessionRoundtrip),
        ("forgetting folder visits empties the list", Forget)
    ];

    private static void PathNormalization()
    {
        foreach (var (source, expected) in new[]
        {
            (@"C:\a", @"C:\a"), (@"C:\a\b\", @"C:\a\b"), ("  C:\\a  ", @"C:\a"), ("C:/Users/me/", @"C:\Users\me"),
            (@"\\server\share\x", @"\\server\share\x"), ("//server/share/x/", @"\\server\share\x"),
            ("d:\\Donn\u00e9es\\\ud55c\uae00", "d:\\Donn\u00e9es\\\ud55c\uae00")
        })
            Equal<string?>(expected, FolderVisits.NormalizePath(source));
        foreach (string? invalid in new string?[]
        {
            null, "", "  ", "C:", @"C:\", @"C:\\", "C:/", @"\\server\share", @"\\server\share\", "relative\\path", @"..\x",
            "C:folder", @"\rooted-without-drive", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "shell:RecycleBinFolder",
            @"\\?\C:\x", @"\\.\pipe\x", @"C:\" + new string('x', 1030)
        })
            Equal<string?>(null, FolderVisits.NormalizePath(invalid));
        var visits = new FolderVisits();
        foreach (string? invalid in new string?[] { null, "", @"C:\", @"\\server\share", "shell:Downloads" })
            visits.Record(invalid);
        Equal(0, visits.Entries.Count);
    }

    private static void CountingAndOrder()
    {
        var visits = new FolderVisits();
        visits.Record(@"C:\work\One");
        visits.Record(@"C:\work\Two");
        visits.Record(@"c:\WORK\one\");
        visits.Record("C:/work/Two");
        visits.Record(@"C:\work\Three");
        Sequence([@"C:\work\Three", @"C:\work\Two", @"C:\work\One"], visits.Entries.Select(entry => entry.Path),
            StringComparer.OrdinalIgnoreCase);
        Sequence([1, 2, 2], visits.Entries.Select(entry => entry.Count));
    }

    private static void FrequentRanking()
    {
        var visits = new FolderVisits();
        void Visit(string folder, int times)
        {
            for (int i = 0; i < times; i++) visits.Record(folder);
        }
        Visit(@"C:\x\Twice", 2);
        Visit(@"C:\x\ThreeEarly", 3);
        Visit(@"C:\x\Five", 5);
        Visit(@"C:\x\ThreeLate", 3);
        Visit(@"C:\x\Once", 1);
        Sequence([@"C:\x\Five", @"C:\x\ThreeLate", @"C:\x\ThreeEarly"], visits.Frequent());
        Sequence([@"C:\x\Five"], visits.Frequent(1));
        Equal(0, visits.Frequent(0).Count);
        Equal(0, visits.Frequent(-3).Count);
        var many = new FolderVisits();
        for (int folder = 0; folder < 40; folder++)
            for (int i = 0; i < 3; i++) many.Record($@"C:\many\f{folder:D2}");
        Equal(FolderVisits.MaxFrequent, many.Frequent().Count);
        Equal(@"C:\many\f39", many.Frequent()[0]);
        Equal(3, FolderVisits.MinVisits);
    }

    private static void Bounds()
    {
        var visits = new FolderVisits();
        for (int i = 0; i < 50; i++) visits.Record(@"C:\busy");
        for (int i = 0; i < 300; i++) visits.Record($@"C:\single\f{i:D3}");
        Equal(FolderVisits.MaxEntries, visits.Entries.Count);
        Equal(@"C:\single\f299", visits.Entries[0].Path);
        Equal(true, visits.Entries.Any(entry => entry.Path == @"C:\busy" && entry.Count == 50));
        Equal(false, visits.Entries.Any(entry => entry.Path == @"C:\single\f000"));
        Equal(false, visits.Entries.Any(entry => entry.Path == @"C:\single\f200"));
        Equal(true, visits.Entries.Any(entry => entry.Path == @"C:\single\f201"));
        // A folder visited once is never the one evicted to make room for itself.
        var full = new FolderVisits(Enumerable.Range(0, FolderVisits.MaxEntries)
            .Select(i => new FolderVisit { Path = $@"C:\all\f{i:D3}", Count = 5 }));
        full.Record(@"C:\new");
        Equal(FolderVisits.MaxEntries, full.Entries.Count);
        Equal(@"C:\new", full.Entries[0].Path);
        Equal(1, full.Entries[0].Count);
    }

    private static void Aging()
    {
        var visits = new FolderVisits();
        for (int i = 0; i < 1000; i++) visits.Record(@"C:\habit");
        Equal(1000, visits.Entries.Single().Count);
        visits.Record(@"C:\habit");
        Equal(500, visits.Entries.Single().Count);
        var mixed = new FolderVisits();
        for (int i = 0; i < 999; i++) mixed.Record(@"C:\big");
        mixed.Record(@"C:\small");
        Equal(1000, mixed.Entries.Sum(entry => entry.Count));
        mixed.Record(@"C:\fresh");
        Sequence([@"C:\fresh", @"C:\big"], mixed.Entries.Select(entry => entry.Path));
        // Halving drops a folder left with no visits, but never the folder just opened.
        Sequence([1, 499], mixed.Entries.Select(entry => entry.Count));
    }

    private static void SavedEntries()
    {
        Equal(0, FolderVisits.Normalize(null).Count);
        var saved = new List<FolderVisit>
        {
            null!, new() { Path = @"C:\ok\One", Count = 4 }, new() { Path = "", Count = 3 }, new() { Path = null!, Count = 3 },
            new() { Path = @"C:\ok\Zero", Count = 0 }, new() { Path = @"C:\ok\Negative", Count = -2 },
            new() { Path = @"relative\path", Count = 9 }, new() { Path = @"C:\", Count = 9 }, new() { Path = @"C:\ok\Huge", Count = int.MaxValue },
            new() { Path = @"c:\OK\one\", Count = 7 }, new() { Path = @"C:\ok\Two", Count = 2 }
        };
        var normalized = FolderVisits.Normalize(saved);
        Sequence([@"C:\ok\One", @"C:\ok\Huge", @"C:\ok\Two"], normalized.Select(entry => entry.Path));
        Sequence([7, 100_000, 2], normalized.Select(entry => entry.Count));
        Equal(11, saved.Count);
        var oversized = FolderVisits.Normalize(Enumerable.Range(0, 250).Select(i => new FolderVisit { Path = $@"C:\n\f{i:D3}", Count = 1 }));
        Equal(FolderVisits.MaxEntries, oversized.Count);
        Equal(@"C:\n\f000", oversized[0].Path);
        Equal(@"C:\n\f099", oversized[^1].Path);
    }

    private static void SessionRoundtrip()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"QuadExplorer.FolderVisits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "session.json");
            var visits = new FolderVisits();
            for (int i = 0; i < 4; i++) visits.Record(@"C:\data\reports");
            visits.Record("D:\\Donn\u00e9es\\\ud55c\uae00");
            SessionStore.Save(path, new SessionState { FrequentFolders = [.. visits.Entries] });
            var restored = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Sequence(visits.Entries.Select(entry => (entry.Path, entry.Count)), restored.FrequentFolders.Select(entry => (entry.Path, entry.Count)));
            string json = File.ReadAllText(path);
            Equal(true, json.Contains("FrequentFolders", StringComparison.Ordinal));
            Equal(true, new FolderVisits(restored.FrequentFolders).Frequent().SequenceEqual([@"C:\data\reports"]));

            File.WriteAllText(path, "{}");
            Equal(0, SessionStore.Load(path).FrequentFolders.Count);
            File.WriteAllText(path, """{"FrequentFolders":null}""");
            Equal(0, SessionStore.Load(path).FrequentFolders.Count);
            File.WriteAllText(path, """{"FrequentFolders":[null,{"Path":"C:\\a\\b","Count":3},{"Path":"..\\x","Count":3},{"Path":"C:\\a\\b","Count":1},{"Path":"C:\\c","Count":0}]}""");
            var loaded = SessionStore.Load(path, out warning);
            Equal<string?>(null, warning);
            Sequence([@"C:\a\b"], loaded.FrequentFolders.Select(entry => entry.Path));
            Equal(3, loaded.FrequentFolders[0].Count);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void Forget()
    {
        var visits = new FolderVisits();
        for (int i = 0; i < 5; i++) visits.Record(@"C:\x\a");
        Equal(1, visits.Frequent().Count);
        visits.Clear();
        Equal(0, visits.Entries.Count);
        Equal(0, visits.Frequent().Count);
        visits.Record(@"C:\x\a");
        Equal(1, visits.Entries.Single().Count);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null)
    {
        if (!expected.SequenceEqual(actual, comparer))
            throw new InvalidOperationException($"Sequences differ: [{string.Join(", ", expected)}] vs [{string.Join(", ", actual)}].");
    }
}
