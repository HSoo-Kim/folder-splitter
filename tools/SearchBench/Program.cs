using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuadExplorer.Search;

return await Bench.RunAsync(args);

internal static class Bench
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "QuadExplorer.SearchBench", Guid.NewGuid().ToString("N"));
    private static readonly CancellationTokenSource Stop = new();
    private static string? _retainedDetails;

    internal static async Task<int> RunAsync(string[] args)
    {
        Directory.CreateDirectory(Scratch);
        foreach (var name in new[] { "TMPDIR", "TMP", "TEMP", "NODE_COMPILE_CACHE", "BUN_RUNTIME_TRANSPILER_CACHE_PATH" })
            Environment.SetEnvironmentVariable(name, Scratch);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                Console.WriteLine("""
                    SearchBench build --root C:\ --store <snapshot> --report <json>
                    SearchBench coverage --store <snapshot> [--instance name] --report <json>
                    SearchBench compare --store <snapshot> [--seed 42 --limit 100 --warmup 3 --passes 5 --order abba] [--queries file] --report <json>
                    SearchBench parity --store <snapshot> [--seed 42 --queries file --instance name] --report <json>
                    SearchBench snapshot-parity --store <snapshot> [--seed 42 --instance name] --report <json>
                    SearchBench update [--root <writable-parent> --instance name] --report <json>
                    SearchBench restart --report <json>
                    SearchBench smoke [--instance name] --report <json>
                    Repeated --root values supported. Build writes a .bench.json scope sidecar.
                    Parity writes only aggregate counts to report; raw mismatches stay in owned temp.
                    """);
                return 0;
            }
            var options = new Options(args.Skip(1).ToArray());
            var report = Path.GetFullPath(options.Required("report"));
            object result = args[0] switch
            {
                "build" => await Build(options),
                "coverage" => await Coverage(options),
                "compare" => await Compare(options),
                "parity" => await Parity(options),
                "snapshot-parity" => await SnapshotParity(options),
                "update" => await Update(options),
                "restart" => await Restart(),
                "smoke" => await Smoke(options),
                _ => throw new ArgumentException("Unknown command. Use --help.")
            };
            await AtomicJson(report, new { schemaVersion = 1, command = args[0], utc = DateTime.UtcNow, result });
            Console.WriteLine(JsonSerializer.Serialize(new { command = args[0], reportWritten = true, privateDetails = _retainedDetails }));
            return result is ParityReport parity && parity.MismatchCount != 0 ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Stop.Dispose();
            if (_retainedDetails == null)
            {
                Directory.Delete(Scratch, true);
                if (Directory.Exists(Scratch)) throw new IOException("Benchmark scratch cleanup failed.");
            }
        }
    }

    private static async Task<object> Build(Options options)
    {
        var configuredRoots = options.All("root").Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(root => root.Length).ToArray();
        var roots = configuredRoots.Where((root, index) =>
            !configuredRoots.Take(index).Any(parent => Within(root, parent))).ToArray();
        if (roots.Length == 0) throw new ArgumentException("build requires --root.");
        if (roots.Any(root => !Directory.Exists(root))) throw new DirectoryNotFoundException("Build root missing.");
        var store = Path.GetFullPath(options.Required("store"));
        Directory.CreateDirectory(Path.GetDirectoryName(store)!);
        var staged = store + "." + Guid.NewGuid().ToString("N") + ".build";
        IndexStats builtStats;
        double buildMilliseconds;
        long buildPrivateBytes;
        try
        {
            using (var backend = new IndependentSearchBackend(roots, staged))
            {
                var watch = Stopwatch.StartNew();
                await backend.BuildOrLoadAsync(Stop.Token);
                buildMilliseconds = watch.Elapsed.TotalMilliseconds;
                builtStats = backend.Stats;
                buildPrivateBytes = PrivateBytes();
                if (backend.Stats.SnapshotLoaded) throw new IOException("Fresh build unexpectedly loaded a snapshot.");
            }
            File.Move(staged, store, true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
        await AtomicJson(store + ".bench.json", new ScopeMetadata(roots));
        var loadWatch = Stopwatch.StartNew();
        var loaded = IndexStore.Load(store, roots, Stop.Token)
            ?? throw new IOException("Persisted snapshot did not load.");
        var loadMilliseconds = loadWatch.Elapsed.TotalMilliseconds;
        if (loaded.Count != builtStats.Entries)
            throw new IOException("Persisted snapshot entry count changed.");
        return new { roots = roots.Length, buildMilliseconds, loadMilliseconds, buildPrivateBytes,
            loadMode = "IndexStore.Load: snapshot IO and decode, excluding startup reconciliation",
            loadPrivateBytes = PrivateBytes(), snapshotEntries = loaded.Count,
            snapshotDeniedDirectories = loaded.DeniedPaths.Length, stats = builtStats };
    }

    private static async Task<IndependentSearchBackend> Open(Options options)
    {
        var store = Path.GetFullPath(options.Required("store"));
        if (!File.Exists(store)) throw new FileNotFoundException("Build a real snapshot before comparison.", store);
        var roots = await Roots(options);
        var backend = new IndependentSearchBackend(roots, store);
        var reconciled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, EventArgs e) => reconciled.TrySetResult();
        backend.RescanCompleted += Completed;
        try
        {
            await backend.BuildOrLoadAsync(Stop.Token);
            if (!backend.Stats.SnapshotLoaded) throw new IOException("Comparison requires the persisted snapshot, not an implicit rebuild.");
            await reconciled.Task.WaitAsync(TimeSpan.FromMinutes(30), Stop.Token);
            return backend;
        }
        catch { backend.Dispose(); throw; }
        finally { backend.RescanCompleted -= Completed; }
    }

    private static async Task<string[]> Roots(Options options)
    {
        var explicitRoots = options.All("root");
        if (explicitRoots.Length > 0) return explicitRoots.Select(Path.GetFullPath).ToArray();
        var metadata = await File.ReadAllTextAsync(Path.GetFullPath(options.Required("store")) + ".bench.json", Stop.Token);
        return (JsonSerializer.Deserialize<ScopeMetadata>(metadata, Json)
            ?? throw new IOException("Missing benchmark scope metadata.")).Roots;
    }

    private static async Task<EverythingIpcBackend> Everything(Options options, Func<SearchHit, bool>? includeHit = null)
    {
        var backend = new EverythingIpcBackend(options.Optional("instance"),
            options.Optional("store") is null ? null : await Roots(options), includeHit);
        try
        {
            await backend.QueryAllAsync("regex:\"(?!)\"", Stop.Token);
            if (backend.IsAvailable && backend.IsReady) return backend;
            throw new IOException("Everything IPC window unavailable or database not loaded.");
        }
        catch { backend.Dispose(); throw; }
    }

    private static async Task<object> Coverage(Options options)
    {
        using var independent = await Open(options);
        using var everything = await Everything(options);
        var roots = await Roots(options);
        var own = independent.QueryAll(SearchQuery.Parse(@"\"), Stop.Token);
        var other = await everything.QueryAllAsync(ScopeExpression(roots), Stop.Token);
        var denied = independent.DeniedPaths;
        var exclusions = Exclusions(own, roots, denied, options.Required("store"));
        var scope = new ScopeFilter(roots, exclusions);
        var left = own.Where(hit => scope.Includes(hit.FullPath)).DistinctBy(Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var right = other.Where(hit => scope.Includes(hit.FullPath)).DistinctBy(Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var leftPaths = left.Select(hit => hit.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingSamples = right.Where(hit => !leftPaths.Contains(hit.FullPath)).Take(12)
            .Select(hit => hit.FullPath).ToArray();
        return new { independent = left.Length, everything = right.Length, independentRaw = own.Count,
            everythingRaw = other.Count, everythingUniqueRaw = other.DistinctBy(Key, StringComparer.OrdinalIgnoreCase).Count(),
            deniedScopes = denied.Count, excludedScopes = exclusions.Length,
            privateMissingSamples = missingSamples,
            roots = roots.Select((root, index) => new { scope = index,
                independent = left.Count(hit => Within(hit.FullPath, root)),
                everything = right.Count(hit => Within(hit.FullPath, root)) }).ToArray(),
            stats = independent.Stats };
    }

    private static async Task<object> Compare(Options options)
    {
        using var independent = await Open(options);
        // The folder-index reference also traverses namespace aliases and denied scopes.
        // Filter those before ranking/truncation, not after taking arbitrary top K.
        var corpus = independent.QueryAll(SearchQuery.Parse(@"\"), Stop.Token)
            .Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var everything = await Everything(options, hit => corpus.Contains(Key(hit)));
        var roots = await Roots(options);
        var priorityRoots = options.All("priority-root");
        if (priorityRoots.Length == 0) priorityRoots = roots;
        var queries = await Queries(options, independent);
        var limit = options.Number("limit", 100, 1);
        var warmup = options.Number("warmup", 3, 0);
        var passes = options.Number("passes", 5, 1);
        var order = options.Optional("order") ?? "abba";
        if (order.Length == 0 || order.Any(ch => ch is not ('a' or 'b')))
            throw new ArgumentException("--order must contain only a and b, for example abba.");
        if (!order.Contains('a') || !order.Contains('b')) throw new ArgumentException("--order must exercise both backends.");
        var samples = new List<Timing>();
        for (var pass = -warmup; pass < passes; pass++)
        {
            Console.WriteLine($"COMPARE pass={pass} queries={queries.Length}");
            for (var index = 0; index < queries.Length; index++)
            {
                var query = queries[index];
                string? expectedDigest = null;
                foreach (var backend in order)
                {
                    var watch = Stopwatch.StartNew();
                    var parsed = SearchQuery.Parse(query.Text);
                    var hits = backend == 'a'
                        ? await independent.QueryAsync(parsed.Normalized, priorityRoots, limit, Stop.Token)
                        : await everything.QueryAsync(parsed.Normalized, priorityRoots, limit, Stop.Token);
                    var materialized = hits.ToArray();
                    var elapsed = watch.Elapsed.TotalMilliseconds;
                    if (materialized.Any(hit => !SearchQuery.Parse(query.Text).IsAnchor(hit.FullPath)))
                        throw new IOException("Backend returned a result outside shared anchor semantics.");
                    var digest = Digest(materialized);
                    expectedDigest ??= digest;
                    if (digest != expectedDigest)
                        throw new IOException($"Ranked candidate mismatch: pass={pass}, query={index}, backend={backend}.");
                    if (pass >= 0) samples.Add(new Timing(backend == 'a' ? "independent" : "everything",
                        query.Category, index, pass, elapsed, materialized.Length, digest));
                }
            }
        }
        var summaries = samples.GroupBy(sample => (sample.Backend, sample.Category))
            .Select(group => Summary(group.Key.Backend, group.Key.Category, group)).ToList();
        summaries.AddRange(samples.GroupBy(sample => sample.Backend)
            .Select(group => Summary(group.Key, "overall", group)));
        return new { seed = options.Number("seed", 42, 0), limit, warmup, passes, order,
            operations = "Actual QueryAsync on both backends; identical scope, context, limit and materialized paths",
            sharedCorpusEntries = corpus.Count, rankedParity = true,
            priorityRootCount = priorityRoots.Length, queryCount = queries.Length, queryDigest = QueryDigest(queries), summaries, samples,
            stats = independent.Stats, privateBytes = PrivateBytes() };
    }

    private static async Task<ParityReport> Parity(Options options)
    {
        using var independent = await Open(options);
        using var everything = await Everything(options);
        var roots = await Roots(options);
        var own = independent.QueryAll(SearchQuery.Parse(@"\"), Stop.Token);
        var exclusions = Exclusions(own, roots, independent.DeniedPaths, options.Required("store"));
        var scope = new ScopeFilter(roots, exclusions);
        var all = await everything.QueryAllAsync(ScopeExpression(roots), Stop.Token);
        var scopedEverything = all.Where(hit => scope.Includes(hit.FullPath)).ToArray();
        var scopedIndependent = own.Where(hit => scope.Includes(hit.FullPath)).ToArray();
        var queries = await Queries(options, independent);
        var summaries = new List<object>();
        var details = new List<object>();
        long mismatches = 0;
        foreach (var query in queries)
        {
            var parsed = SearchQuery.Parse(query.Text);
            // All candidates in the exact same scope, evaluated using the same predicate.
            var oracle = scopedEverything.Where(hit => parsed.Matches(hit.FullPath)).ToArray();
            var actual = independent.QueryAll(parsed, Stop.Token)
                .Where(hit => scope.Includes(hit.FullPath)).ToArray();
            var native = (await everything.QueryAllAsync(
                ScopeExpression(roots) + " " + EverythingIpcBackend.SearchExpression(parsed), Stop.Token))
                .Where(hit => scope.Includes(hit.FullPath) && parsed.Matches(hit.FullPath)).ToArray();
            var expectedPaths = oracle.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualPaths = actual.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nativePaths = native.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = expectedPaths.Except(actualPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            var extra = actualPaths.Except(expectedPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            var nativeMissing = expectedPaths.Except(nativePaths, StringComparer.OrdinalIgnoreCase).ToArray();
            var nativeExtra = nativePaths.Except(expectedPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            mismatches += missing.Length + extra.Length + nativeMissing.Length + nativeExtra.Length;
            summaries.Add(new { query.Category, independent = actual.Length, everythingOracle = oracle.Length,
                everythingNative = native.Length, missing = missing.Length, extra = extra.Length,
                nativeMissing = nativeMissing.Length, nativeExtra = nativeExtra.Length });
            if (missing.Length + extra.Length + nativeMissing.Length + nativeExtra.Length > 0)
                details.Add(new { query = parsed.Normalized, missing, extra, nativeMissing, nativeExtra });
        }
        var left = scopedIndependent.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var right = scopedEverything.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coverageMissing = right.Except(left, StringComparer.OrdinalIgnoreCase).ToArray();
        var coverageExtra = left.Except(right, StringComparer.OrdinalIgnoreCase).ToArray();
        mismatches += coverageMissing.Length + coverageExtra.Length;
        if (mismatches != 0)
        {
            _retainedDetails = Path.Combine(Scratch, "private-mismatches.json");
            await AtomicJson(_retainedDetails, new { coverageMissing, coverageExtra, queries = details });
        }
        return new ParityReport(mismatches, scopedIndependent.Length, scopedEverything.Length,
            coverageMissing.Length, coverageExtra.Length, independent.DeniedPaths.Count, exclusions.Length,
            summaries.ToArray(), _retainedDetails != null);
    }

    private static async Task<ParityReport> SnapshotParity(Options options)
    {
        using var independent = await Open(options);
        using var everything = await Everything(options);
        var roots = await Roots(options);
        var snapshot = IndexStore.Load(options.Required("store"), roots, Stop.Token)
            ?? throw new IOException("Reconciled snapshot unavailable.");
        var own = snapshot.Search(SearchQuery.Parse(@"\"), [], int.MaxValue, false, Stop.Token);
        var exclusions = Exclusions(own, roots, snapshot.DeniedPaths, options.Required("store"));
        var scope = new ScopeFilter(roots, exclusions);
        var reference = (await everything.QueryAllAsync(ScopeExpression(roots), Stop.Token))
            .Where(hit => scope.Includes(hit.FullPath)).ToArray();
        var left = own.Where(hit => scope.Includes(hit.FullPath)).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var right = reference.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int coverageMissing = right.Except(left, StringComparer.OrdinalIgnoreCase).Count();
        int coverageExtra = left.Except(right, StringComparer.OrdinalIgnoreCase).Count();
        var shared = left.Intersect(right, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long mismatches = coverageMissing + coverageExtra > Math.Min(left.Count, right.Count) * .01
            ? coverageMissing + coverageExtra : 0;
        var summaries = new List<object>();
        foreach (var query in await Queries(options, independent))
        {
            var parsed = SearchQuery.Parse(query.Text);
            var expected = reference.Where(hit => shared.Contains(Key(hit)) && parsed.Matches(hit.FullPath))
                .Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual = snapshot.Search(parsed, [], int.MaxValue, false, Stop.Token)
                .Where(hit => shared.Contains(Key(hit))).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase).Count();
            int extra = actual.Except(expected, StringComparer.OrdinalIgnoreCase).Count();
            mismatches += missing + extra;
            summaries.Add(new { query.Category, sharedEntries = shared.Count, expected = expected.Count,
                actual = actual.Count, missing, extra });
        }
        return new ParityReport(mismatches, left.Count, right.Count, coverageMissing, coverageExtra,
            snapshot.DeniedPaths.Length, exclusions.Length, summaries.ToArray(), false);
    }

    private static async Task<object> Update(Options options)
    {
        var parent = options.Optional("root");
        var fixture = parent == null ? Path.Combine(Scratch, "update") :
            Path.Combine(Path.GetFullPath(parent), "QuadExplorer.SearchBench." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var store = Path.Combine(Scratch, "updates.index");
        try
        {
            using var independent = new IndependentSearchBackend([fixture], store);
            await independent.BuildOrLoadAsync(Stop.Token);
            using var everything = await Everything(options);
            var first = Path.Combine(fixture, "searchbench_" + Guid.NewGuid().ToString("N") + ".txt");
            var second = Path.Combine(fixture, "searchbench_" + Guid.NewGuid().ToString("N") + ".txt");
            var samples = new List<object>();
            await Change("create", first, true, () => File.WriteAllText(first, "fixture"));
            await Change("rename", second, true, () => File.Move(first, second), first);
            await Change("delete", second, false, () => File.Delete(second));
            return new { samples, notification = "independent IndexChanged; Everything observed by completed IPC queries", stats = independent.Stats };

            async Task Change(string operation, string target, bool exists, Action mutation, string? absent = null)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var signal = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                var watch = new Stopwatch();
                void Changed(object? sender, EventArgs e)
                {
                    if (deadline.IsCancellationRequested) return;
                    try
                    {
                        var result = independent.QueryAll(SearchQuery.Parse(Quote(target)), deadline.Token);
                        if (result.Any(hit => hit.FullPath.Equals(target, StringComparison.OrdinalIgnoreCase)) == exists &&
                            (absent == null || independent.QueryAll(SearchQuery.Parse(Quote(absent)), deadline.Token).Count == 0))
                            signal.TrySetResult(watch.Elapsed.TotalMilliseconds);
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                    catch (Exception exception) { signal.TrySetException(exception); }
                }
                independent.IndexChanged += Changed; // Subscribe before the filesystem operation.
                try
                {
                    watch.Start();
                    mutation();
                    var other = ObserveEverything();
                    var own = signal.Task.WaitAsync(deadline.Token);
                    await Task.WhenAll(own, other);
                    var ownMilliseconds = await own;
                    var otherMilliseconds = await other;
                    samples.Add(new { operation, independentMilliseconds = ownMilliseconds, everythingMilliseconds = otherMilliseconds });
                }
                finally { deadline.Cancel(); independent.IndexChanged -= Changed; }

                async Task<double> ObserveEverything()
                {
                    while (true)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        var hits = await everything.QueryAllAsync(EverythingIpcBackend.LiteralSearch(target), deadline.Token);
                        var old = absent == null ? Array.Empty<SearchHit>() :
                            await everything.QueryAllAsync(EverythingIpcBackend.LiteralSearch(absent), deadline.Token);
                        if (hits.Any(hit => hit.FullPath.Equals(target, StringComparison.OrdinalIgnoreCase)) == exists &&
                            old.All(hit => !hit.FullPath.Equals(absent, StringComparison.OrdinalIgnoreCase)))
                            return watch.Elapsed.TotalMilliseconds;
                        // IPC completion is the bounded observation signal. No timing sleeps.
                    }
                }
            }
        }
        finally { Directory.Delete(fixture, true); }
    }

    private static async Task<object> Restart()
    {
        var fixture = Path.Combine(Scratch, "restart");
        Directory.CreateDirectory(fixture);
        var original = Path.Combine(fixture, "original.txt");
        var deleted = Path.Combine(fixture, "deleted.txt");
        var renamed = Path.Combine(fixture, "renamed.txt");
        var created = Path.Combine(fixture, "created.txt");
        File.WriteAllText(original, "fixture");
        File.WriteAllText(deleted, "fixture");
        var store = Path.Combine(Scratch, "restart.index");
        using (var first = new IndependentSearchBackend([fixture], store))
            await first.BuildOrLoadAsync(Stop.Token);
        File.Move(original, renamed);
        File.Delete(deleted);
        File.WriteAllText(created, "fixture");
        using var second = new IndependentSearchBackend([fixture], store);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var reconciled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.RescanCompleted += (_, _) => reconciled.TrySetResult();
        var watch = Stopwatch.StartNew();
        await second.BuildOrLoadAsync(deadline.Token);
        var loaded = second.Stats.SnapshotLoaded;
        await reconciled.Task.WaitAsync(deadline.Token);
        var hits = second.QueryAll(SearchQuery.Parse(@"\"), deadline.Token).Select(hit => hit.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded || !hits.Contains(created) || !hits.Contains(renamed) || hits.Contains(original) || hits.Contains(deleted))
            throw new IOException("Persisted/offline-mutation restart check failed.");
        return new { snapshotLoaded = loaded, offlineCreate = true, offlineRename = true, offlineDelete = true,
            reconciliationMilliseconds = watch.Elapsed.TotalMilliseconds, stats = second.Stats };
    }

    private static async Task<object> Smoke(Options options)
    {
        string[] scopeRoots = [@"C:\bench", @"D:\other"];
        string[] excluded = [@"C:\bench\skip", @"C:\bench\one.txt"];
        var scope = new ScopeFilter(scopeRoots, excluded);
        foreach (var path in new[] { @"C:\bench", @"C:\bench\ok\a.txt", @"C:\bench\skip",
            @"C:\bench\skip\a.txt", @"C:\bench\skip-more\a.txt", @"C:\bench\one.txt",
            @"C:\bench\one.txt-more", @"C:\benchX\a.txt", @"D:\other\a.txt" })
            if (scope.Includes(path) != (scopeRoots.Any(root => Within(path, root))
                && !excluded.Any(root => Within(path, root))))
                throw new IOException("Indexed scope filter changed path-boundary semantics.");
        using var backend = await Everything(options);
        var counts = new List<object>();
        foreach (var text in new[] { "FolderSplitter", @"공유용\월별연구계획_20261019", "quad_searchbench_absent_" + Guid.NewGuid().ToString("N") })
        {
            var watch = Stopwatch.StartNew();
            var hits = await backend.QueryAllAsync(EverythingIpcBackend.LiteralSearch(text), Stop.Token);
            counts.Add(new { count = hits.Count, milliseconds = watch.Elapsed.TotalMilliseconds,
                pathsValid = hits.All(hit => Path.IsPathFullyQualified(hit.FullPath)) });
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await backend.QueryAllAsync("", canceled.Token); throw new IOException("Cancellation not honored."); }
        catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        using var missing = new EverythingIpcBackend("SearchBench-absent-" + Guid.NewGuid().ToString("N"));
        if (missing.IsAvailable || missing.IsReady || (await missing.QueryAllAsync("", Stop.Token)).Count != 0)
            throw new IOException("Absent instance check failed.");
        return new { counts, cancellationPassed = true, absentInstancePassed = true, scopeFilterPassed = true };
    }

    private static async Task<QueryCase[]> Queries(Options options, IndependentSearchBackend independent)
    {
        if (options.Optional("queries") is { } file)
        {
            var lines = await File.ReadAllLinesAsync(file, Stop.Token);
            var supplied = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line =>
            {
                var split = line.IndexOf('\t');
                return new QueryCase(split < 0 ? "supplied" : line[..split],
                    SearchQuery.Parse(split < 0 ? line : line[(split + 1)..]).Normalized);
            }).ToArray();
            if (supplied.Length == 0) throw new ArgumentException("--queries file contains no queries.");
            return supplied;
        }
        var all = independent.QueryAll(SearchQuery.Parse(@"\"), Stop.Token)
            .OrderBy(hit => hit.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(hit => hit.FullPath, StringComparer.Ordinal).ToArray();
        if (all.Length == 0) throw new IOException("Cannot sample queries from an empty dataset.");
        var random = new Random(options.Number("seed", 42, 0));
        var sampled = all.Select(hit => Path.GetFileName(hit.FullPath.TrimEnd('\\')))
            .Where(leaf => leaf.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sampled.Length == 0) throw new IOException("Dataset has no sampleable leaf names.");
        var queries = new List<QueryCase>();
        foreach (var leaf in sampled.OrderBy(_ => random.Next()).Take(12))
        {
            var length = Math.Min(leaf.Length, random.Next(3, 9));
            var start = random.Next(leaf.Length - length + 1);
            var part = leaf.Substring(start, length);
            queries.Add(new QueryCase("leaf", Quote(part)));
            queries.Add(new QueryCase("case", Quote(part.ToUpperInvariant())));
        }
        foreach (var hit in all.OrderBy(_ => random.Next()).Take(8))
        {
            var parts = hit.FullPath.TrimEnd('\\').Split('\\');
            if (parts.Length < 2) continue;
            var parent = parts[^2];
            var leaf = parts[^1];
            queries.Add(new QueryCase("partialPath", Quote(parent + "\\" + leaf[..Math.Min(leaf.Length, 6)])));
        }
        queries.Add(new QueryCase("korean", Quote(@"공유용\월별연구계획_20261019")));
        queries.Add(new QueryCase("broad", "a"));
        queries.Add(new QueryCase("broad", "e"));
        queries.Add(new QueryCase("broad", "."));
        var zero = "searchbench_zero_" + options.Number("seed", 42, 0).ToString(CultureInfo.InvariantCulture);
        while (all.Any(hit => SearchQuery.Parse(zero).Matches(hit.FullPath))) zero += "_x";
        queries.Add(new QueryCase("zero", zero));
        return queries.Select(query => query with { Text = SearchQuery.Parse(query.Text).Normalized }).ToArray();
    }

    private static string[] Exclusions(IReadOnlyList<SearchHit> entries, string[] roots,
        IReadOnlyList<string> denied, string store)
    {
        var excluded = new HashSet<string>(denied, StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(store), Path.GetFullPath(store) + ".bench.json", Scratch
        };
        // Independent scanner intentionally does not traverse junctions/symlinks.
        foreach (var hit in entries.Where(hit => hit.IsDirectory && !roots.Contains(hit.FullPath, StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                if ((File.GetAttributes(hit.FullPath) & FileAttributes.ReparsePoint) != 0)
                    excluded.Add(hit.FullPath);
            }
            catch (FileNotFoundException) { excluded.Add(hit.FullPath); }
            catch (DirectoryNotFoundException) { excluded.Add(hit.FullPath); }
            catch (UnauthorizedAccessException) { excluded.Add(hit.FullPath); }
        }
        return excluded.ToArray();
    }

    private sealed class ScopeFilter(string[] roots, string[] exclusions)
    {
        private readonly HashSet<string> excluded = exclusions.Select(path => path.TrimEnd('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        internal bool Includes(string path)
        {
            if (!roots.Any(root => Within(path, root))) return false;
            var lookup = excluded.GetAlternateLookup<ReadOnlySpan<char>>();
            var prefix = path.AsSpan().TrimEnd("\\/".AsSpan());
            while (!prefix.IsEmpty)
            {
                if (lookup.Contains(prefix)) return false;
                int slash = prefix.LastIndexOf('\\');
                if (slash < 0) break;
                prefix = prefix[..slash];
            }
            return true;
        }
    }

    private static bool Within(string path, string root)
    {
        root = root.TrimEnd('\\', '/');
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string ScopeExpression(IEnumerable<string> roots) => "<" +
        string.Join("|", roots.Select(root => "regex:\"^" +
            System.Text.RegularExpressions.Regex.Escape(root.TrimEnd('\\', '/')) + @"(\\|$)" + "\"")) + ">";
    private static string Quote(string text) => "\"" + text.Replace("\"", "") + "\"";
    private static string Key(SearchHit hit) => (hit.IsDirectory ? "D:" : "F:") + hit.FullPath.TrimEnd('\\');
    private static string Digest(IEnumerable<SearchHit> hits) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", hits.Select(hit => Key(hit).ToUpperInvariant())))));
    private static string QueryDigest(IEnumerable<QueryCase> queries) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", queries.Select(query => query.Category + "\t" + query.Text)))));
    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }
    private static object Summary(string backend, string category, IEnumerable<Timing> timings)
    {
        var data = timings.Select(timing => timing.Milliseconds).Order().ToArray();
        double Percentile(double percentile) => data[Math.Max(0, (int)Math.Ceiling(data.Length * percentile) - 1)];
        return new { backend, category, samples = data.Length, p50 = Percentile(.50),
            p95 = Percentile(.95), p99 = Percentile(.99), mean = data.Average(), maximum = data[^1] };
    }
    private static async Task AtomicJson(string destination, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, Json, Stop.Token);
                await stream.FlushAsync(Stop.Token);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record ScopeMetadata(string[] Roots);
    private sealed record QueryCase(string Category, string Text);
    private sealed record Timing(string Backend, string Category, int Query, int Pass, double Milliseconds, int Count, string Digest);
    private sealed record ParityReport(long MismatchCount, int IndependentScopeCount, int EverythingScopeCount,
        int CoverageMissing, int CoverageExtra, int DeniedScopes, int ExcludedScopes, object[] Queries, bool PrivateDetailsWritten);

    private sealed class Options
    {
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
        internal Options(string[] args)
        {
            for (var index = 0; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 == args.Length)
                    throw new ArgumentException("Options require --name value pairs.");
                var name = args[index][2..];
                if (!_values.TryGetValue(name, out var values)) _values[name] = values = [];
                values.Add(args[index + 1]);
            }
        }
        internal string Required(string name) => Optional(name) ?? throw new ArgumentException("Missing --" + name);
        internal string? Optional(string name) => _values.TryGetValue(name, out var values) ? values[^1] : null;
        internal string[] All(string name) => _values.TryGetValue(name, out var values) ? values.ToArray() : [];
        internal int Number(string name, int fallback, int minimum)
        {
            if (Optional(name) is not { } value) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < minimum)
                throw new ArgumentException("--" + name + " must be at least " + minimum);
            return result;
        }
    }
}
