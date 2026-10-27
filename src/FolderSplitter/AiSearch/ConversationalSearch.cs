using QuadExplorer.Search;

namespace QuadExplorer.AiSearch;

public sealed class ConversationalSearch(IPathSearchBackend backend)
{
    public const int MaxCandidates = 2000;
    public const int MaxResults = 200;

    public async Task<AiSearchResult> RunAsync(SearchPlan plan, AiSearchResult? previous,
        IReadOnlyList<string> roots, CancellationToken token)
    {
        plan = SearchPlanValidator.Normalize(plan);
        token.ThrowIfCancellationRequested();
        if (plan.Clarification is not null)
            return new AiSearchResult(plan, [], 0, false, plan.Clarification);
        if (!backend.IsAvailable || !backend.IsReady)
            throw new InvalidOperationException("The file search index is not ready. Wait for indexing to finish.");
        var priorityRoots = roots.Take(64).ToArray();
        var scopeRoots = priorityRoots.Where(Path.IsPathFullyQualified)
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (plan.Scope == SearchScope.ActiveFolder)
            scopeRoots = scopeRoots.Take(1).ToArray();
        if (plan.Scope != SearchScope.Everywhere && scopeRoots.Length == 0)
            throw new InvalidOperationException("No folder is available for the requested search scope.");
        var candidates = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var truncated = false;
        if (plan.Refine)
        {
            if (previous is null)
                throw new InvalidOperationException("There are no previous results to refine.");
            candidates.AddRange(previous.Hits.Take(MaxCandidates).Select(hit => new SearchHit(hit.FullPath, hit.IsDirectory)));
            truncated = previous.Truncated || previous.Hits.Count > MaxCandidates;
        }
        else
        {
            // The index understands literal AND terms, not OR syntax. One complete OR group is
            // sufficient to seed candidates; all remaining groups are evaluated against real hits.
            var seeds = plan.AllTerms.Count > 0
                ? new[] { string.Join(" ", plan.AllTerms.Select(term => $"\"{term}\"")) }
                : plan.AnyOf.Count > 0 ? plan.AnyOf[0].Select(term => $"\"{term}\"").ToArray()
                : plan.Extensions.Select(extension => $"\"{extension}\"").ToArray();
            foreach (var seed in seeds)
            {
                token.ThrowIfCancellationRequested();
                var hits = await backend.QueryAsync(seed, priorityRoots, MaxCandidates, token)
                    .ConfigureAwait(false);
                truncated |= hits.Count >= MaxCandidates;
                foreach (var hit in hits)
                {
                    if (!seen.Add(hit.FullPath))
                        continue;
                    candidates.Add(hit);
                }
                truncated |= candidates.Count > MaxCandidates;
                // Literal name queries have no ancestor penalty. Reuse the index's ranker
                // across OR branches so a later branch's active-folder hit still ranks first.
                if (seeds.Length > 1)
                    candidates = PathRanker.Rank(SearchQuery.Parse(""), candidates, priorityRoots, MaxCandidates).ToList();
                else if (candidates.Count > MaxCandidates)
                    candidates.RemoveRange(MaxCandidates, candidates.Count - MaxCandidates);
            }
        }
        var results = new List<AiHit>();
        var examined = 0;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            examined++;
            if (candidate.FullPath.Length > 32768 || !Path.IsPathFullyQualified(candidate.FullPath))
                continue;
            var path = Path.GetFullPath(candidate.FullPath);
            if (plan.Scope != SearchScope.Everywhere && !scopeRoots.Any(root => Within(path, root)))
                continue;
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (!plan.AllTerms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase))
                || !plan.AnyOf.All(group => group.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                || plan.Excluded.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                var directory = Directory.Exists(path);
                if (!directory && !File.Exists(path))
                    continue;
                if (plan.Kind == ItemKind.File && directory || plan.Kind == ItemKind.Folder && !directory)
                    continue;
                if (plan.Extensions.Count > 0 && (directory
                    || !plan.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                    continue;
                FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
                info.Refresh();
                if (!info.Exists)
                    continue;
                long? bytes = info is FileInfo file ? file.Length : null;
                var modified = info.LastWriteTimeUtc;
                if (plan.MinBytes is not null && (bytes is null || bytes < plan.MinBytes)
                    || plan.MaxBytes is not null && (bytes is null || bytes > plan.MaxBytes)
                    || plan.ModifiedAfterUtc is not null && modified < plan.ModifiedAfterUtc
                    || plan.ModifiedBeforeUtc is not null && modified > plan.ModifiedBeforeUtc)
                    continue;
                if (results.Count == MaxResults)
                {
                    truncated = true;
                    break;
                }
                results.Add(new AiHit(path, directory, bytes, modified));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        token.ThrowIfCancellationRequested();
        return new AiSearchResult(plan, results.ToArray(), examined, truncated,
            truncated ? "Search limits reached; narrow the request to see more specific matches." : null);
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
