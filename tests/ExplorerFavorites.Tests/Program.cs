using System.Reflection;
using QuadExplorer;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var other = Environment.SystemDirectory;
        var items = new[]
        {
            new TestItem(path, pinned: false),
            new TestItem(path, pinned: null),
            new TestItem(path, pinned: true, isFolder: false),
            new TestItem(path, pinned: true, isFileSystem: false),
            new TestItem(path, pinned: true),
            new TestItem(path.ToUpperInvariant() + Path.DirectorySeparatorChar, pinned: true),
            new TestItem(other, pinned: true)
        };
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        ReadFolder(new TestShell(items), paths, seen, warnings);
        Check(paths.Count == 2, "Pinned folder filtering and case-insensitive deduplication");
        Check(paths[0] == Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
            "Path normalization and Explorer order");
        Check(warnings.Count == 1, "Missing pinned property produces a warning");

        paths = Enumerable.Range(0, 99).Select(index => $"seed-{index}").ToList();
        seen = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        ReadFolder(new TestShell([
            new TestItem(path, pinned: true),
            new TestItem(other, pinned: true)
        ]), paths, seen, []);
        Check(paths.Count == 100, "Imported favorites stop at 100");

        var missing = Path.Combine(Path.GetTempPath(), "folder-splitter-missing-" + Guid.NewGuid().ToString("N"));
        var plan = ExplorerFavorites.PlanExport(
            [path, path.ToUpperInvariant() + Path.DirectorySeparatorChar, other, missing,
                "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "relative\\folder", ""],
            [other.ToUpperInvariant() + Path.DirectorySeparatorChar]);
        Check(plan.ToPin.SequenceEqual([Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))]),
            "Export pins each existing folder once regardless of case or trailing separator");
        Check(plan.AlreadyPinned == 1, "Export skips folders File Explorer already pins");
        Check(plan.Unavailable == 4, "Export counts missing, virtual, relative and blank favorites as not pinnable");
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var shortName = new System.Text.StringBuilder(1024);
        uint shortLength = GetShortPathNameW(programs, shortName, (uint)shortName.Capacity);
        string shortPrograms = shortName.ToString(0, (int)Math.Min(shortLength, (uint)shortName.Capacity));
        if (shortLength > 0 && !string.Equals(shortPrograms, programs, StringComparison.OrdinalIgnoreCase))
            Check(ExplorerFavorites.PlanExport([shortPrograms], [programs]) is { ToPin.Count: 0, AlreadyPinned: 1 },
                "Export recognizes an 8.3 short name of a folder File Explorer already pins");
        Console.WriteLine($"SHORT_NAME programs={programs} short={shortPrograms}");

        var actual = ExplorerFavorites.Read();
        Check(actual.Paths.Count <= 100, "Live import cap");
        Check(actual.Paths.All(Directory.Exists), "Live import contains existing folders only");
        Check(actual.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Paths.Count,
            "Live import contains no duplicate paths");
        var pinned = ExplorerFavorites.ReadPinnedQuickAccess();
        Check(pinned is not null && pinned.All(Directory.Exists)
            && pinned.Distinct(StringComparer.OrdinalIgnoreCase).Count() == pinned.Count,
            "Live Quick Access pins are known, distinct existing folders");
        Console.WriteLine($"PASS: filtering, normalization, deduplication, cap, export plan; live folders={actual.Paths.Count}; live pins={pinned!.Count}; warning={actual.Warning ?? "none"}");
        return 0;
    }

    private static void ReadFolder(TestShell shell, List<string> paths,
        HashSet<string> seen, List<string> warnings)
    {
        typeof(ExplorerFavorites).GetMethod("ReadFolder", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [shell, "test", false, paths, seen, warnings, 100]);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathNameW(string path, System.Text.StringBuilder shortPath, uint length);

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }
}

public sealed class TestShell(TestItem[] items)
{
    public TestFolder NameSpace(string location) => new(items);
}

public sealed class TestFolder(TestItem[] items)
{
    public TestItems Items() => new(items);
}

public sealed class TestItems(TestItem[] items)
{
    public int Count => items.Length;
    public TestItem Item(int index) => items[index];
}

public sealed class TestItem(string path, object? pinned, bool isFolder = true, bool isFileSystem = true)
{
    public string Path => path;
    public bool IsFolder => isFolder;
    public bool IsFileSystem => isFileSystem;
    public object? ExtendedProperty(string property) => pinned;
}
