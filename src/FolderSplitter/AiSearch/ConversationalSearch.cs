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
        if (plan.Refine && previous is null)
            throw new InvalidOperationException("There are no previous results to refine.");
        var filters = new List<(SearchPlan Plan, IReadOnlyList<string> Roots)>();
        bool knownHistory = !plan.Refine || previous!.RefinementHistory is { Count: > 0 }
            || previous.Plan.Scope == SearchScope.Everywhere;
        if (plan.Refine && previous!.RefinementHistory is { Count: > 0 } history)
            filters.AddRange(history);
        else if (plan.Refine && previous!.Plan.Scope == SearchScope.Everywhere)
            filters.Add((previous.Plan, Array.Empty<string>()));
        filters.Add((plan, scopeRoots));
        bool requery = plan.Refine && previous!.Truncated && knownHistory;
        var candidates = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var truncated = false;
        if (plan.Refine && !requery)
        {
            candidates.AddRange(previous!.Hits.Take(MaxCandidates).Select(hit => new SearchHit(hit.FullPath, hit.IsDirectory)));
            truncated = previous!.Truncated || previous.Hits.Count > MaxCandidates;
        }
        else
        {
            // A truncated prior result is only a preview. Requery using all previous
            // constraints and the new ones instead of filtering the first 200 hits.
            var seeds = requery ? RefinementSeeds(filters) : plan.AllTerms.Count > 0
                ? new[] { string.Join(" ", plan.AllTerms.Select(term => $"\"{term}\"")) }
                : plan.AnyOf.Count > 0 ? plan.AnyOf[0].Select(term => $"\"{term}\"").ToArray()
                : plan.Extensions.Select(extension => $"\"{extension}\"").ToArray();
            var queryRoots = requery ? RestrictedRoots(filters, priorityRoots) : priorityRoots;
            if (requery && queryRoots.Count == 0 && filters.Any(filter => filter.Plan.Scope != SearchScope.Everywhere))
                seeds = [];
            foreach (var seed in seeds)
            {
                token.ThrowIfCancellationRequested();
                var hits = await backend.QueryAsync(seed, queryRoots, MaxCandidates, token)
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
                    candidates = PathRanker.Rank(SearchQuery.Parse(""), candidates, queryRoots, MaxCandidates).ToList();
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
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            bool matches = true;
            foreach (var (filter, filterRoots) in filters)
            {
                if (filter.Scope != SearchScope.Everywhere && !filterRoots.Any(root => Within(path, root))
                    || !filter.AllTerms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase))
                    || !filter.AnyOf.All(group => group.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    || filter.Excluded.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                { matches = false; break; }
            }
            if (!matches) continue;
            try
            {
                var directory = Directory.Exists(path);
                if (!directory && !File.Exists(path))
                    continue;
                foreach (var (filter, _) in filters)
                {
                    if (filter.Kind == ItemKind.File && directory || filter.Kind == ItemKind.Folder && !directory
                        || filter.Extensions.Count > 0 && (directory
                            || !filter.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                    { matches = false; break; }
                }
                if (!matches) continue;
                FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
                info.Refresh();
                if (!info.Exists)
                    continue;
                long? bytes = info is FileInfo file ? file.Length : null;
                var modified = info.LastWriteTimeUtc;
                foreach (var (filter, _) in filters)
                {
                    if (filter.MinBytes is not null && (bytes is null || bytes < filter.MinBytes)
                        || filter.MaxBytes is not null && (bytes is null || bytes > filter.MaxBytes)
                        || filter.ModifiedAfterUtc is not null && modified < filter.ModifiedAfterUtc
                        || filter.ModifiedBeforeUtc is not null && modified > filter.ModifiedBeforeUtc)
                    { matches = false; break; }
                }
                if (!matches) continue;
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
            truncated ? "Search limits reached; narrow the request to see more specific matches." : null)
        {
            RefinementHistory = knownHistory ? filters.ToArray() : null
        };
    }

    private static string[] RefinementSeeds(IReadOnlyList<(SearchPlan Plan, IReadOnlyList<string> Roots)> filters)
    {
        var terms = filters.SelectMany(filter => filter.Plan.AllTerms)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var extensionSets = filters.Select(filter => filter.Plan.Extensions)
            .Where(extensions => extensions.Count > 0).ToArray();
        string[] extensions = extensionSets.Length == 0 ? []
            : extensionSets.Skip(1).Aggregate(extensionSets[0].ToArray(),
                (current, next) => current.Intersect(next, StringComparer.OrdinalIgnoreCase).ToArray());
        if (extensionSets.Length > 0 && extensions.Length == 0) return [];
        string prefix = string.Join(" ", terms.Select(term => $"\"{term}\""));
        if (extensions.Length > 0)
            return extensions.Select(extension => $"{prefix} \"{extension}\"".TrimStart()).ToArray();
        if (terms.Length > 0) return [prefix];
        var group = filters.SelectMany(filter => filter.Plan.AnyOf)
            .OrderBy(alternatives => alternatives.Count).FirstOrDefault();
        return group?.Select(term => $"\"{term}\"").ToArray() ?? [];
    }

    private static IReadOnlyList<string> RestrictedRoots(
        IReadOnlyList<(SearchPlan Plan, IReadOnlyList<string> Roots)> filters, IReadOnlyList<string> fallback)
    {
        var scoped = filters.Where(filter => filter.Plan.Scope != SearchScope.Everywhere).ToArray();
        if (scoped.Length == 0) return fallback;
        var intersection = scoped[0].Roots.ToArray();
        foreach (var (_, roots) in scoped.Skip(1))
            intersection = intersection.SelectMany(first => roots.Select(second =>
                    Within(first, second) ? first : Within(second, first) ? second : null))
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return intersection;
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
