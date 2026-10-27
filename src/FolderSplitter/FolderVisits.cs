namespace QuadExplorer;

// One folder the user has opened and how many times.
public sealed record FolderVisit
{
    public string Path { get; init; } = "";
    public int Count { get; init; }
}

// Counts how often each folder is opened so address-bar search can rank files in frequently used folders
// ahead of other matches. Only folder paths and counts are kept, in the local session file. The list is
// bounded, and old habits fade: when the counts grow past a total, every count is halved.
public sealed class FolderVisits
{
    public const int MaxEntries = 100;
    // A folder must be opened this often before it counts as frequent.
    public const int MinVisits = 3;
    public const int MaxFrequent = 24;
    private const int MaxPathLength = 1024;
    private const int MaxCount = 100_000;
    private const int AgingTotal = 1000;
    private readonly List<FolderVisit> entries;

    public FolderVisits(IEnumerable<FolderVisit>? saved = null) => entries = Normalize(saved);

    // Most recently opened first.
    public IReadOnlyList<FolderVisit> Entries => entries;

    public void Record(string? path)
    {
        string? key = NormalizePath(path);
        if (key is null) return;
        int count = 1;
        int index = entries.FindIndex(entry => string.Equals(entry.Path, key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            count = Math.Min(entries[index].Count + 1, MaxCount);
            entries.RemoveAt(index);
        }
        entries.Insert(0, new FolderVisit { Path = key, Count = count });
        Age();
        while (entries.Count > MaxEntries)
        {
            // The folder just opened stays; the least visited, then the oldest, makes room.
            int drop = 1;
            for (int i = 2; i < entries.Count; i++)
                if (entries[i].Count <= entries[drop].Count) drop = i;
            entries.RemoveAt(drop);
        }
    }

    // Folders opened at least MinVisits times: most visited first, ties to the more recent one.
    public IReadOnlyList<string> Frequent(int limit = MaxFrequent) => entries
        .Select((entry, order) => (entry, order)).Where(item => item.entry.Count >= MinVisits)
        .OrderByDescending(item => item.entry.Count).ThenBy(item => item.order)
        .Take(Math.Max(0, limit)).Select(item => item.entry.Path).ToArray();

    public void Clear() => entries.Clear();

    // A folder path to remember: fully qualified, and below a drive or share root.
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string value = path.Trim().Replace('/', '\\').TrimEnd('\\');
        if (value.Length == 0 || value.Length > MaxPathLength || !Path.IsPathFullyQualified(value)
            || value.StartsWith(@"\\?\", StringComparison.Ordinal) || value.StartsWith(@"\\.\", StringComparison.Ordinal))
            return null;
        string root = (Path.GetPathRoot(value) ?? "").TrimEnd('\\');
        return value.Length <= root.Length ? null : value;
    }

    // Drops invalid and duplicate entries and keeps the list within its limits.
    public static List<FolderVisit> Normalize(IEnumerable<FolderVisit>? source)
    {
        var result = new List<FolderVisit>();
        foreach (var entry in source ?? [])
        {
            string? key = entry is null ? null : NormalizePath(entry.Path);
            if (key is null || entry!.Count < 1) continue;
            int count = Math.Min(entry.Count, MaxCount);
            int existing = result.FindIndex(item => string.Equals(item.Path, key, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                if (count > result[existing].Count) result[existing] = result[existing] with { Count = count };
                continue;
            }
            result.Add(new FolderVisit { Path = key, Count = count });
            if (result.Count == MaxEntries) break;
        }
        return result;
    }

    private void Age()
    {
        long total = 0;
        foreach (var entry in entries) total += entry.Count;
        if (total <= AgingTotal) return;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            int half = entries[i].Count / 2;
            // The folder just opened (index 0) keeps at least one visit.
            if (half < 1 && i > 0) entries.RemoveAt(i);
            else entries[i] = entries[i] with { Count = Math.Max(half, 1) };
        }
    }
}
