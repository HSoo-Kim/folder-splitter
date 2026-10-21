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

        var actual = ExplorerFavorites.Read();
        Check(actual.Paths.Count <= 100, "Live import cap");
        Check(actual.Paths.All(Directory.Exists), "Live import contains existing folders only");
        Check(actual.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Paths.Count,
            "Live import contains no duplicate paths");
        Console.WriteLine($"PASS: filtering, normalization, deduplication, cap; live folders={actual.Paths.Count}; warning={actual.Warning ?? "none"}");
        return 0;
    }

    private static void ReadFolder(TestShell shell, List<string> paths,
        HashSet<string> seen, List<string> warnings)
    {
        typeof(ExplorerFavorites).GetMethod("ReadFolder", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [shell, "test", false, paths, seen, warnings]);
    }

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
