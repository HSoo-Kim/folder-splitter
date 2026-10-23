using System.Buffers;
using System.Text;

namespace QuadExplorer.Search;

internal readonly record struct IndexEntry(string Name, bool IsDirectory);
// A folder's entries are packed into one string: a type mark ('d' folder, 'f' file), the name and a
// '\0' separator for each entry. One string per folder instead of one per entry saves the per-object
// overhead of millions of names; Windows file names never contain '\0'.
internal sealed class IndexDirectory
{
    internal const char FolderMark = 'd';
    internal const char FileMark = 'f';
    internal const char Separator = '\0';
    internal string Path { get; }
    internal string Packed { get; }
    internal int Count { get; }

    internal IndexDirectory(string path, string packed, int count)
    {
        Path = path;
        Packed = packed;
        Count = count;
    }

    internal IndexDirectory(string path, IReadOnlyCollection<IndexEntry> entries)
        : this(path, Pack(entries), entries.Count) { }

    internal IEnumerable<IndexEntry> Entries
    {
        get
        {
            for (int position = 0; position < Packed.Length;)
            {
                int end = Packed.IndexOf(Separator, position + 1);
                yield return new IndexEntry(Packed[(position + 1)..end], Packed[position] == FolderMark);
                position = end + 1;
            }
        }
    }

    internal static void Append(StringBuilder packed, string name, bool isDirectory) =>
        packed.Append(isDirectory ? FolderMark : FileMark).Append(name).Append(Separator);

    internal static bool IsWellFormed(string packed, int count) =>
        packed.Length == 0 ? count == 0 : packed[^1] == Separator && packed.AsSpan().Count(Separator) == count;

    private static string Pack(IEnumerable<IndexEntry> entries)
    {
        var packed = new StringBuilder();
        foreach (var entry in entries)
            Append(packed, entry.Name, entry.IsDirectory);
        return packed.ToString();
    }
}

internal sealed class FileIndex
{
    internal static readonly FileIndex Empty = new([], [], [], DateTime.MinValue);
    internal string[] Roots { get; }
    internal IndexDirectory[] Directories { get; }
    internal string[] DeniedPaths { get; }
    internal DateTime BuiltUtc { get; }
    internal int Count { get; }

    internal FileIndex(string[] roots, IndexDirectory[] directories, string[] deniedPaths, DateTime builtUtc)
    {
        Roots = roots;
        Directories = directories;
        DeniedPaths = deniedPaths;
        BuiltUtc = builtUtc;
        Count = roots.Length + directories.Sum(directory => directory.Count);
    }

    internal IReadOnlyList<SearchHit> Search(SearchQuery query, IReadOnlyList<string> priorityRoots,
        int limit, bool anchorsOnly, CancellationToken cancellationToken)
    {
        if (!anchorsOnly || Count < 200_000)
            return SearchRange(query, priorityRoots, limit, anchorsOnly, 0, Directories.Length, true, cancellationToken);
        // Each partition's top K contains every item that can enter the global top K.
        int workers = Math.Min(4, Environment.ProcessorCount);
        var results = new IReadOnlyList<SearchHit>[workers];
        Parallel.For(0, workers, new ParallelOptions
        {
            CancellationToken = cancellationToken, MaxDegreeOfParallelism = workers
        }, worker =>
        {
            results[worker] = SearchRange(query, priorityRoots, limit, true,
                Directories.Length * worker / workers, Directories.Length * (worker + 1) / workers,
                worker == 0, cancellationToken);
        });
        return PathRanker.Rank(query, results.SelectMany(hits => hits), priorityRoots, limit);
    }

    private IReadOnlyList<SearchHit> SearchRange(SearchQuery query, IReadOnlyList<string> priorityRoots,
        int limit, bool anchorsOnly, int start, int end, bool includeRoots, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Terms.Length == 0 || limit <= 0)
            return [];
        var all = anchorsOnly ? null : new List<SearchHit>();
        var top = anchorsOnly ? new PathRanker.TopMatches(query, priorityRoots, limit) : null;
        var buffer = ArrayPool<char>.Shared.Rent(512);
        try
        {
            if (includeRoots)
                foreach (var root in Roots)
                    Add(root.AsSpan(), true);
            for (int number = start; number < end; number++)
            {
                var directory = Directories[number];
                cancellationToken.ThrowIfCancellationRequested();
                var parentLength = directory.Path.Length;
                var separator = directory.Path.EndsWith('\\') ? 0 : 1;
                var prefixLength = parentLength + separator;
                EnsureCapacity(prefixLength);
                directory.Path.AsSpan().CopyTo(buffer);
                if (separator != 0)
                    buffer[parentLength] = '\\';
                var checkedEntries = 0;
                var packed = directory.Packed.AsSpan();
                for (int position = 0; position < packed.Length;)
                {
                    if ((checkedEntries++ & 255) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    var isDirectory = packed[position] == IndexDirectory.FolderMark;
                    var rest = packed[(position + 1)..];
                    var name = rest[..rest.IndexOf(IndexDirectory.Separator)];
                    position += name.Length + 2;
                    if (!query.MatchesName(name, anchorsOnly))
                        continue;
                    EnsureCapacity(prefixLength + name.Length);
                    name.CopyTo(buffer.AsSpan(prefixLength));
                    Add(buffer.AsSpan(0, prefixLength + name.Length), isDirectory);
                }
            }
            return all is not null ? all : top!.Results();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        void Add(ReadOnlySpan<char> path, bool isDirectory)
        {
            if (anchorsOnly ? query.IsAnchor(path) : query.Matches(path))
            {
                if (all is not null)
                    all.Add(new SearchHit(path.ToString(), isDirectory));
                else
                    top!.Add(path, isDirectory);
            }
        }

        void EnsureCapacity(int length)
        {
            if (buffer.Length >= length)
                return;
            var larger = ArrayPool<char>.Shared.Rent(length);
            buffer.CopyTo(larger, 0);
            ArrayPool<char>.Shared.Return(buffer);
            buffer = larger;
        }
    }
}
