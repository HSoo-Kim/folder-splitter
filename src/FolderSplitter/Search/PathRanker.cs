namespace QuadExplorer.Search;

public static class PathRanker
{
    public static IReadOnlyList<SearchHit> Rank(SearchQuery query, IEnumerable<SearchHit> hits,
        IReadOnlyList<string> priorityRoots, int limit)
    {
        var top = new TopMatches(query, priorityRoots, limit);
        foreach (var hit in hits)
            top.Add(hit.FullPath.AsSpan(), hit.IsDirectory);
        return top.Results();
    }

    internal sealed class TopMatches
    {
        private readonly SearchQuery _query;
        private readonly string[] _roots;
        private readonly int _limit;
        private readonly PriorityQueue<RankedHit, RankedHit> _heap = new(WorstFirst.Instance);

        internal TopMatches(SearchQuery query, IReadOnlyList<string> roots, int limit)
        {
            _query = query;
            _roots = roots.Select(SearchQuery.NormalizePath).ToArray();
            _limit = Math.Max(0, limit);
        }

        internal void Add(ReadOnlySpan<char> path, bool directory)
        {
            if (_limit == 0)
                return;
            var scope = _roots.Length;
            for (var i = 0; i < _roots.Length; i++)
            {
                var root = _roots[i].AsSpan();
                if (path.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || path.Length > root.Length && path[root.Length] == '\\'
                    && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    scope = i;
                    break;
                }
            }
            var penalty = _query.AncestorPenalty(path);
            if (_heap.Count == _limit)
            {
                var worst = _heap.Peek();
                if (Compare(scope, penalty, directory, path, worst) >= 0)
                    return;
                _heap.Dequeue();
            }
            var item = new RankedHit(new SearchHit(path.ToString(), directory), scope, penalty);
            _heap.Enqueue(item, item);
        }

        internal IReadOnlyList<SearchHit> Results()
        {
            var result = new SearchHit[_heap.Count];
            for (var index = result.Length - 1; index >= 0; index--)
                result[index] = _heap.Dequeue().Hit;
            return result;
        }

        private static int Compare(int scope, int penalty, bool directory, ReadOnlySpan<char> path,
            RankedHit other)
        {
            var result = scope.CompareTo(other.Scope);
            if (result == 0)
                result = penalty.CompareTo(other.Penalty);
            if (result == 0)
                result = other.Hit.IsDirectory.CompareTo(directory);
            if (result == 0)
                result = path.CompareTo(other.Hit.FullPath, StringComparison.OrdinalIgnoreCase);
            if (result == 0)
                result = path.CompareTo(other.Hit.FullPath, StringComparison.Ordinal);
            return result;
        }

        private sealed record RankedHit(SearchHit Hit, int Scope, int Penalty);

        private sealed class WorstFirst : IComparer<RankedHit>
        {
            internal static readonly WorstFirst Instance = new();
            public int Compare(RankedHit? left, RankedHit? right) =>
                -TopMatches.Compare(left!.Scope, left.Penalty, left.Hit.IsDirectory,
                    left.Hit.FullPath, right!);
        }
    }
}
