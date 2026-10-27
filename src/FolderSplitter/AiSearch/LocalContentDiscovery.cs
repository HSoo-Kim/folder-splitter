namespace QuadExplorer.AiSearch;

public interface IRelevantDocumentReader
{
    Task<DocumentReadResult> ReadRelevantAsync(string path, DocumentReadLimits limits,
        IReadOnlyList<string> terms, CancellationToken token);
}

public sealed record LocalContentMatch(string FullPath, IReadOnlyList<DocumentExcerpt> Excerpts, bool Truncated);
public sealed record LocalContentScan(IReadOnlyList<LocalContentMatch> Matches, int FilesScanned, int Failed, bool Limited);

/// <summary>On-demand traversal of one explicitly selected local directory. No content is persisted.</summary>
public static class LocalContentDiscovery
{
    private const int MaxFiles = 200;
    private const int MaxEntries = 4000;
    private const int MaxDepth = 8;
    private const int MaxMatches = 64;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".csv", ".log", ".pdf", ".hwp", ".hwpx", ".hml", ".docx", ".pptx", ".xlsx", ".xls" };

    public static async Task<LocalContentScan> ScanAsync(string folder, IReadOnlyList<string> terms,
        IDocumentReader reader, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var matches = new List<LocalContentMatch>();
        int scanned = 0, failed = 0, entries = 0;
        bool limited = false;
        string root;
        try
        {
            root = Path.GetFullPath(folder);
            if (!Path.IsPathFullyQualified(folder) || root.StartsWith(@"\\", StringComparison.Ordinal)
                || string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(root) || new DriveInfo(Path.GetPathRoot(root)!).DriveType == DriveType.Network
                || HasReparseAncestor(root))
                return new(matches, 0, 1, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(matches, 0, 1, true);
        }
        if (!terms.Any(term => !string.IsNullOrWhiteSpace(term))) return new(matches, 0, 0, true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        try
        {
            while (pending.Count > 0)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var (directory, depth) = pending.Pop();
                try
                {
                    if (HasReparseAncestor(directory)) { limited = true; continue; }
                    foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        if (++entries > MaxEntries) { limited = true; return new(matches, scanned, failed, limited); }
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { limited = true; continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                if (depth >= MaxDepth) limited = true;
                                else pending.Push((entry, depth + 1));
                                continue;
                            }
                            if (!Extensions.Contains(Path.GetExtension(entry))) continue;
                            if (scanned >= MaxFiles || matches.Count >= MaxMatches)
                                return new(matches, scanned, failed, true);
                            // Recheck the complete ancestry just before opening a file.
                            if (HasReparseAncestor(entry)) { limited = true; continue; }
                            scanned++;
                            var limits = new DocumentReadLimits();
                            if (new FileInfo(entry).Length > limits.MaxFileBytes) { failed++; limited = true; continue; }
                            Task<DocumentReadResult> read = reader is IRelevantDocumentReader relevant
                                ? relevant.ReadRelevantAsync(entry, limits, terms, deadline.Token)
                                : reader.ReadAsync(entry, limits, deadline.Token);
                            var result = await read.WaitAsync(deadline.Token).ConfigureAwait(false);
                            if (result.Status != DocumentReadStatus.Success) { failed++; limited = true; continue; }
                            var excerpts = reader is IRelevantDocumentReader ? result.Excerpts
                                : result.Excerpts.Where(excerpt => RelevantExcerpts.ContainsTerm(excerpt.Text, terms)).ToArray();
                            if (excerpts.Count > 0) matches.Add(new(entry, excerpts, result.Truncated));
                            // The legacy reader only inspects a prefix, so it cannot establish absence.
                            limited |= result.Truncated || reader is not IRelevantDocumentReader;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                            or NotSupportedException or InvalidOperationException or TimeoutException)
                        { failed++; limited = true; }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { failed++; limited = true; }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { limited = true; }
        token.ThrowIfCancellationRequested();
        return new(matches, scanned, failed, limited);
    }

    private static bool HasReparseAncestor(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
        }
        return false;
    }
}

internal static class RelevantExcerpts
{
    internal static bool ContainsTerm(string text, IReadOnlyList<string> terms) => terms.Any(term =>
        !string.IsNullOrWhiteSpace(term) && text.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static DocumentReadResult FromText(string text, string locator, IReadOnlyList<string> terms, DocumentReadLimits limits)
    {
        var positions = new List<(int Start, int Length)>();
        foreach (string raw in terms)
        {
            string term = raw.Trim();
            if (term.Length == 0) continue;
            int at = 0;
            while (at < text.Length)
            {
                int found = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                positions.Add((found, term.Length));
                // Enough candidates to fill bounded evidence; report remaining coverage as partial.
                if (positions.Count >= 500) break;
                at = found + Math.Max(1, term.Length);
            }
            if (positions.Count >= 500) break;
        }
        positions.Sort((a, b) => a.Start.CompareTo(b.Start));
        var excerpts = new List<DocumentExcerpt>();
        int remaining = limits.MaxCharacters, lastEnd = 0;
        bool truncated = positions.Count >= 500;
        foreach (var (start, length) in positions)
        {
            if (start < lastEnd) continue;
            if (remaining == 0 || excerpts.Count >= limits.MaxExcerpts) { truncated = true; break; }
            int window = Math.Min(remaining, Math.Max(length, 800));
            int begin = Math.Max(lastEnd, Math.Max(0, start - (window - Math.Min(length, window)) / 2));
            int end = Math.Min(text.Length, begin + window);
            string label = locator == "characters" ? $"characters {begin + 1}-{end}" : $"{locator}, characters {begin + 1}-{end}";
            excerpts.Add(new(text[begin..end], label));
            remaining -= end - begin;
            lastEnd = end;
            truncated |= begin > 0 || end < text.Length || length > window;
        }
        return new(DocumentReadStatus.Success, excerpts, truncated);
    }
}
