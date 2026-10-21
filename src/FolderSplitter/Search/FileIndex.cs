using System.Buffers;

namespace QuadExplorer.Search;

internal readonly record struct IndexEntry(string Name, bool IsDirectory);
internal sealed record IndexDirectory(string Path, IndexEntry[] Entries);

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
        Count = roots.Length + directories.Sum(directory => directory.Entries.Length);
    }

    internal IReadOnlyList<SearchHit> Search(SearchQuery query, IReadOnlyList<string> priorityRoots,
        int limit, bool anchorsOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Terms.Length == 0 || limit <= 0)
            return [];
        var all = anchorsOnly ? null : new List<SearchHit>();
        var top = anchorsOnly ? new PathRanker.TopMatches(query, priorityRoots, limit) : null;
        var buffer = ArrayPool<char>.Shared.Rent(512);
        try
        {
            foreach (var root in Roots)
                Add(root.AsSpan(), true);
            foreach (var directory in Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parentLength = directory.Path.Length;
                var separator = directory.Path.EndsWith('\\') ? 0 : 1;
                var prefixLength = parentLength + separator;
                EnsureCapacity(prefixLength);
                directory.Path.AsSpan().CopyTo(buffer);
                if (separator != 0)
                    buffer[parentLength] = '\\';
                var checkedEntries = 0;
                foreach (var entry in directory.Entries)
                {
                    if ((checkedEntries++ & 255) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (!query.MatchesName(entry.Name.AsSpan(), anchorsOnly))
                        continue;
                    EnsureCapacity(prefixLength + entry.Name.Length);
                    entry.Name.AsSpan().CopyTo(buffer.AsSpan(prefixLength));
                    Add(buffer.AsSpan(0, prefixLength + entry.Name.Length), entry.IsDirectory);
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
