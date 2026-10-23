using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using QuadExplorer.Search;

internal static class Program
{
    private static int _assertions;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private static async Task<int> Main()
    {
        var root = Directory.CreateTempSubdirectory("path-search-tests-").FullName;
        var elapsed = Stopwatch.StartNew();
        try
        {
            QuerySemantics();
            Ranking();
            await CompactIndex();
            await RealIndex(root);
            Console.WriteLine($"PASS: {_assertions} assertions; elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F1}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            Directory.Delete(root, true);
            Console.WriteLine($"CLEANUP fixture_removed={!Directory.Exists(root)}");
        }
    }

    private static void QuerySemantics()
    {
        const string target = @"C:\자료\공유용\월별연구계획_20261019";
        foreach (var text in new[]
        {
            "공유용/월별연구계획_20261019", @"공유용\월별연구계획_20261019",
            "  \"공유용//월별연구계획_20261019\"  ", "\"공유용/월별연구계획_20261019",
            "\"공유용/월별연구계획_20261019\" \"월별연구계획\""
        })
        {
            var query = SearchQuery.Parse(text);
            Check(query.Matches(target) && query.IsAnchor(target), "Slash, whitespace and quote variant");
            Check(query.Matches(target.Replace(@"\공유용\", @"\X공유용\")), "First segment suffix allowed");
            Check(!query.Matches(target.Replace(@"\공유용\", @"\공유용X\")), "First segment prefix rejected");
        }
        var path = SearchQuery.Parse("plan/2026/result");
        Check(path.Matches(@"C:\Xplan\2026\resultExtra.txt"), "Suffix first and prefix last segment");
        Check(!path.Matches(@"C:\planX\2026\result.txt"), "First segment must end at slash");
        Check(!path.Matches(@"C:\plan\2026X\result.txt"), "Middle segment must be exact");
        Check(!path.Matches(@"C:\plan\X2026\result.txt"), "Middle segment cannot have prefix");
        Check(path.Matches(@"C:\PLAN\2026\RESULT.txt"), "Ordinal ignore-case path");
        Check(SearchQuery.Parse("résumé").Matches(@"C:\work\RÉSUMÉ.txt"), "Unicode ordinal case");
        Check(!SearchQuery.Parse("é").Matches("C:\\work\\e\u0301.txt"), "No implicit Unicode normalization");
        Check(SearchQuery.Parse("\"meeting notes\" 2026").Matches(@"C:\meeting notes 2026.txt"),
            "Quoted phrase and unquoted term");
        Check(!SearchQuery.Parse("\"meeting notes\" 2026").Matches(@"C:\meeting 2026 notes.txt"),
            "Phrase remains contiguous");
        Check(SearchQuery.Parse("notes 2026").Matches(@"C:\2026 meeting notes.txt"), "Terms are AND, unordered");
        Check(!SearchQuery.Parse("notes 2026").Matches(@"C:\2026\notes.txt"), "Bare terms only search leaf");
        Check(SearchQuery.Parse("plan/2026").Matches(@"C:\plan\2026\child.txt"), "QueryAll includes descendants");
        Check(!SearchQuery.Parse("plan/2026").IsAnchor(@"C:\plan\2026\child.txt"), "Descendant is not anchor");
        Check(SearchQuery.Parse("plan/2026 child").IsAnchor(@"C:\plan\2026\child.txt"), "AND name touches leaf");
        Check(SearchQuery.Parse("plan/2026").IsAnchor(@"C:\plan\2026\plan\2026.txt"), "Later occurrence anchors");
        Check(!SearchQuery.Parse("plan/").IsAnchor(@"C:\plan\child.txt"), "Ancestor separator does not touch leaf");
        const string readmeFolder = @"D:\연구\KMA500m_15분_자료공유_20261020";
        const string readmeFile = readmeFolder + @"\Raw_data_15min_rain\rn_15m_2022_08.nc";
        Check(SearchQuery.Parse("rn 2022_08").IsAnchor(readmeFile) && SearchQuery.Parse("nc 15m 2022").IsAnchor(readmeFile)
            && SearchQuery.Parse(".NC 2022_08").IsAnchor(readmeFile) && SearchQuery.Parse("rn nc").IsAnchor(readmeFile),
            "README examples: name fragments in any order and case");
        Check(SearchQuery.Parse("자료공유").IsAnchor(readmeFolder) && !SearchQuery.Parse("자료공유").Matches(readmeFile),
            "README example: folder name fragment finds the folder");
        Check(SearchQuery.Parse("15min_rain/rn_15m").IsAnchor(readmeFile), "README example: slash follows the path");
        Check(SearchQuery.Parse("\"회의 자료\"").IsAnchor(@"C:\x\회의 자료.pptx"), "README example: quoted name with a space");
        Check(!SearchQuery.Parse("자료공유 nc").Matches(readmeFile), "README example: parent and file fragments do not mix");
        Check(SearchQuery.Parse(@"C:\plan\2026").Matches(@"C:\plan\2026.txt"), "Rooted benchmark term supported");
        Check(!SearchQuery.Parse("  \"\" ").Matches(@"C:\anything"), "Empty terms never match");
        Check(SearchQuery.Parse("  notes \t \"two words\" ").Normalized == "notes \"two words\"",
            "Normalized expression preserves phrase boundaries");
        foreach (var text in new[] { "C:", @"C:\work", "C:relative", @"\\server\share", "/tmp", "%APPDATA%",
            "~/work", "::shell", "shell:Downloads", "\"C:\\work\"" })
            Check(SearchQuery.IsRootedInput(text), "Navigation input discrimination");
        foreach (var text in new[] { "공유용/계획", "plan", "meeting notes", "Cnotes", "" })
            Check(!SearchQuery.IsRootedInput(text), "Relative input remains search");
    }

    private static void Ranking()
    {
        var query = SearchQuery.Parse("공유용/계획");
        SearchHit[] hits =
        [
            new(@"C:\global\공유용\계획", true),
            new(@"C:\favorite\공유용\계획", true),
            new(@"C:\tab\공유용\계획", true),
            new(@"C:\current\X공유용\계획", true),
            new(@"C:\current\공유용\계획.txt", false),
            new(@"C:\current\공유용\계획", true),
            new(@"C:\current-other\공유용\계획", true)
        ];
        var ranked = PathRanker.Rank(query, hits, [@"C:\current\", @"C:\tab", @"C:\favorite"], 7);
        Check(ranked[0] == hits[5] && ranked[1] == hits[4] && ranked[2] == hits[3],
            "Exact ancestor before suffix and directory before file within scope");
        Check(ranked[3] == hits[2] && ranked[4] == hits[1], "Current, tabs, favorites, then global");
        Check(ranked[5] == hits[6], "Root scope uses segment boundary");
        Check(PathRanker.Rank(query, hits, [@"C:\current"], 1)[0] == hits[5],
            "Priority rank is applied before topK truncation");
        Check(PathRanker.Rank(query, hits.Reverse(), [@"C:\current", @"C:\tab", @"C:\favorite"], 7)
            .SequenceEqual(ranked), "Stable order independent of enumeration");
        Check(PathRanker.Rank(query, hits, [], 0).Count == 0, "Zero limit");
        Check(PathRanker.Rank(query, hits, [], -1).Count == 0, "Negative limit");
        SearchHit[] ties = [new(@"C:\work\a", false), new(@"C:\work\A", false)];
        Check(PathRanker.Rank(SearchQuery.Parse("a"), ties, [], 2)[0].FullPath.EndsWith('A'),
            "Ordinal tie-break after case-insensitive ordering");
    }

    private static async Task CompactIndex()
    {
        var directories = Enumerable.Range(0, 80).Select(number =>
            new IndexDirectory($@"C:\fixture\group{number:D2}", Enumerable.Range(0, 2500)
                .Select(file => new IndexEntry($"item{file:D5}.txt", false)).ToArray())).ToArray();
        var index = new FileIndex([], directories, [], DateTime.UtcNow);
        var query = SearchQuery.Parse("item");
        _ = index.Search(query, [], 10, true, CancellationToken.None);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var broad = index.Search(query, [], 10, true, CancellationToken.None);
        var milliseconds = clock.Elapsed.TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(broad.Count == 10 && broad[0].FullPath.EndsWith("item00000.txt"), "Partitioned compact broad query");
        Check(allocated < 1_000_000, "Broad topK does not allocate full paths for every match");
        Console.WriteLine($"METRIC synthetic_entries={index.Count} broad_query_ms={milliseconds:F3} allocated_bytes={allocated}");

        var reference = directories.SelectMany(directory => directory.Entries.Select(entry =>
            new SearchHit(directory.Path + "\\" + entry.Name, entry.IsDirectory))).ToArray();
        var queries = new[] { "item12", "group01/item", "group0", "item005 txt", "\"item00001.txt\"", "missing" };
        await Task.WhenAll(Enumerable.Range(0, 24).Select(number => Task.Run(() =>
        {
            var parsed = SearchQuery.Parse(queries[number % queries.Length]);
            var expected = reference.Where(hit => parsed.Matches(hit.FullPath))
                .OrderBy(hit => hit.FullPath, StringComparer.Ordinal).ToArray();
            var actual = index.Search(parsed, [], int.MaxValue, false, CancellationToken.None)
                .OrderBy(hit => hit.FullPath, StringComparer.Ordinal).ToArray();
            Check(actual.SequenceEqual(expected), "Parallel indexed/reference QueryAll parity");
            var roots = new[] { @"C:\fixture\group39", @"C:\fixture\group03" };
            var ranked = PathRanker.Rank(parsed, reference.Where(hit => parsed.IsAnchor(hit.FullPath)), roots, 9);
            Check(index.Search(parsed, roots, 9, true, CancellationToken.None).SequenceEqual(ranked),
                "Parallel bounded topK/reference rank parity");
        })));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => index.Search(query, [], 1, true, cancellation.Token),
            "Index query cancellation");
    }

    private static async Task RealIndex(string workspace)
    {
        var root = Path.Combine(workspace, "root");
        var external = Path.Combine(workspace, "external");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        Write(Path.Combine(root, "공유용", "월별연구계획_20261019", "descendant.txt"));
        Write(Path.Combine(root, "X공유용", "월별연구계획_20261019.txt"));
        Write(Path.Combine(root, "공유용X", "월별연구계획_20261019.txt"));
        Write(Path.Combine(root, "notes", "meeting notes 2026.txt"));
        Write(Path.Combine(root, "hidden-system.txt"));
        File.SetAttributes(Path.Combine(root, "hidden-system.txt"), FileAttributes.Hidden | FileAttributes.System);
        var longFile = Path.Combine(root, new string('a', 70), new string('b', 70), new string('c', 70),
            new string('d', 70), "long-target.txt");
        Write(longFile);
        Check(longFile.Length > 260, "Long-path fixture actually exceeds MAX_PATH");
        Write(Path.Combine(external, "do-not-follow.txt"));
        var junction = Path.Combine(root, "linked-outside");
        await Junction(junction, external);
        var denied = Path.Combine(root, "denied");
        Write(Path.Combine(denied, "unreadable-secret.txt"));
        var deniedInfo = new DirectoryInfo(denied);
        var originalAcl = deniedInfo.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var restrictedAcl = deniedInfo.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        restrictedAcl.AddAccessRule(new FileSystemAccessRule(
            identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        deniedInfo.SetAccessControl(restrictedAcl);
        var store = Path.Combine(root, "owned.index");
        try
        {
            Throws<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(denied),
                "Denied fixture actually denies enumeration");
            var builder = new IndexBuilder([root], store);
            var index = builder.Build(CancellationToken.None);
            Check(index.DeniedPaths.Contains(denied, StringComparer.OrdinalIgnoreCase), "Denied directory recorded");
            Check(index.Search(SearchQuery.Parse("hidden-system"), [], 9, true, CancellationToken.None).Count == 1,
                "Hidden and system entries included");
            Check(index.Search(SearchQuery.Parse("long-target"), [], 9, true, CancellationToken.None)[0].FullPath
                == longFile, "Long path indexed without truncation");
            Check(index.Search(SearchQuery.Parse("linked-outside"), [], 9, true, CancellationToken.None).Count == 1,
                "Reparse point itself indexed");
            Check(index.Search(SearchQuery.Parse("do-not-follow"), [], 9, true, CancellationToken.None).Count == 0,
                "Reparse point not descended");
            StoreRoundTrip(store, root, index);

            using var backend = new IndependentSearchBackend([root, root.ToUpperInvariant(), Path.Combine(root, "notes")], store);
            Check(backend.IsAvailable && !backend.IsReady, "Available distinct from initial ready");
            using var initial = new Observation(backend, () => backend.IsReady, true);
            var buildClock = Stopwatch.StartNew();
            await backend.BuildOrLoadAsync().WaitAsync(Deadline);
            await initial.Completion.Task.WaitAsync(Deadline);
            Check(backend.Stats.SnapshotLoaded && backend.Stats.DeniedDirectories == 1, "Snapshot loaded with denied coverage");
            Check(backend.LastError is null && backend.Stats.BuiltUtc.Kind == DateTimeKind.Utc, "Ready engine diagnostics");
            Check(backend.Stats.Entries == index.Count, "Overlapping roots deduplicated");
            Console.WriteLine($"METRIC fixture_entries={backend.Stats.Entries} denied={backend.Stats.DeniedDirectories} load_reconcile_ms={buildClock.Elapsed.TotalMilliseconds:F3} snapshot_bytes={new FileInfo(store).Length}");
            const string text = "공유용/월별연구계획_20261019";
            var all = backend.QueryAll(SearchQuery.Parse(text));
            var anchors = await backend.QueryAsync(text, [], 20, CancellationToken.None);
            Check(all.Count == 3 && anchors.Count == 2, "QueryAll descendants versus popup anchors");
            Check(anchors[0].IsDirectory && anchors[0].FullPath.Contains(@"\공유용\"), "Exact Korean ancestor ranks first");
            Check(backend.QueryAll(SearchQuery.Parse("owned.index")).Count == 0, "Store excluded from own index");
            Check(!Directory.EnumerateFiles(root, "owned.index.*.tmp").Any(), "No snapshot temporary leak");

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => backend.QueryAsync("notes", [], 10, canceled.Token),
                "Backend query cancellation");
            Throws<OperationCanceledException>(() => backend.QueryAll(SearchQuery.Parse("notes"), canceled.Token),
                "Backend QueryAll cancellation");
            await WatcherChanges(backend, root, workspace);
            await OverflowRecovery(backend, root);

            backend.Dispose();
            Check(!backend.IsAvailable && !backend.IsReady, "Disposed backend no longer available");
            Throws<ObjectDisposedException>(() => backend.QueryAll(SearchQuery.Parse("notes")), "Disposed query rejected");
            Write(Path.Combine(root, "offline-created.txt"));
            File.Delete(Path.Combine(root, "hidden-system.txt"));
            Directory.Move(Path.Combine(root, "notes"), Path.Combine(root, "offline-renamed"));
            using var restarted = new IndependentSearchBackend([root], store);
            using var cached = new Observation(restarted, () =>
                restarted.IsReady && restarted.Stats.SnapshotLoaded
                && !Has(restarted, "offline-created.txt") && Has(restarted, "hidden-system")
                && restarted.QueryAll(SearchQuery.Parse("notes/meeting")).Count == 1);
            using var rescanned = new Observation(restarted, () =>
                Has(restarted, "offline-created.txt") && !Has(restarted, "hidden-system")
                && restarted.QueryAll(SearchQuery.Parse("meeting notes")).Any(hit => hit.FullPath.Contains("offline-renamed")),
                true);
            await restarted.BuildOrLoadAsync().WaitAsync(Deadline);
            await cached.Completion.Task.WaitAsync(Deadline);
            Check(cached.Completion.Task.IsCompletedSuccessfully, "Ready cached snapshot visible before reconciliation");
            await rescanned.Completion.Task.WaitAsync(Deadline);
            Check(restarted.Stats.SnapshotLoaded, "Restart loaded snapshot before offline reconciliation");
            Check(!restarted.QueryAll(SearchQuery.Parse("notes/meeting")).Any(), "Offline directory rename removes old subtree");
            Check(restarted.QueryAll(SearchQuery.Parse("offline-renamed/meeting")).Count == 1,
                "Offline directory rename adds new subtree");
            Check(!Directory.EnumerateFiles(root, "owned.index.*.tmp").Any(), "No temporary leak after restart");
            await DisposeFromNotification(restarted, root);

            using var neverStarted = new IndependentSearchBackend([root], Path.Combine(workspace, "canceled.index"));
            await ThrowsAsync<OperationCanceledException>(() => neverStarted.BuildOrLoadAsync(canceled.Token),
                "Initial build cancellation");
            Check(!File.Exists(Path.Combine(workspace, "canceled.index")), "Canceled initial build leaves no store");
        }
        finally
        {
            var restoredAcl = deniedInfo.GetAccessControl();
            restoredAcl.SetSecurityDescriptorBinaryForm(originalAcl, AccessControlSections.Access);
            deniedInfo.SetAccessControl(restoredAcl);
            Directory.Delete(junction);
        }
    }

    private static void StoreRoundTrip(string store, string root, FileIndex index)
    {
        IndexStore.Save(store, [root], index, CancellationToken.None);
        var saved = File.ReadAllBytes(store);
        var loaded = IndexStore.Load(store, [root], CancellationToken.None);
        Check(loaded is not null && loaded.Count == index.Count && loaded.BuiltUtc == index.BuiltUtc,
            "Snapshot round-trip metadata");
        Check(loaded!.DeniedPaths.SequenceEqual(index.DeniedPaths), "Snapshot round-trip denied paths");
        var query = SearchQuery.Parse("공유용/월별연구계획_20261019");
        Check(loaded.Search(query, [], 50, false, CancellationToken.None).SequenceEqual(
            index.Search(query, [], 50, false, CancellationToken.None)), "Snapshot round-trip results");
        Check(IndexStore.Load(store, [root + "-other"], CancellationToken.None) is null, "Snapshot root scope validated");
        var corrupt = saved.ToArray();
        corrupt[corrupt.Length / 2] ^= 0x40;
        File.WriteAllBytes(store, corrupt);
        Check(IndexStore.Load(store, [root], CancellationToken.None) is null, "Corrupt checksum rejected");
        File.WriteAllBytes(store, saved[..^17]);
        Check(IndexStore.Load(store, [root], CancellationToken.None) is null, "Truncated store rejected");
        corrupt = saved.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(corrupt.AsSpan(4), 999);
        SHA256.HashData(corrupt.AsSpan(0, corrupt.Length - 32), corrupt.AsSpan(corrupt.Length - 32));
        File.WriteAllBytes(store, corrupt);
        Check(IndexStore.Load(store, [root], CancellationToken.None) is null, "Valid checksum unsupported version rejected");
        File.WriteAllBytes(store, saved);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Throws<OperationCanceledException>(() => IndexStore.Save(store, [root], index, canceled.Token),
            "Atomic save cancellation");
        Check(File.ReadAllBytes(store).SequenceEqual(saved), "Canceled save preserves previous store");
        Check(!Directory.EnumerateFiles(root, "owned.index.*.tmp").Any(), "Canceled save removes temporary file");
        Throws<OperationCanceledException>(() => IndexStore.Load(store, [root], canceled.Token),
            "Snapshot load cancellation");
    }

    private static async Task WatcherChanges(IndependentSearchBackend backend, string root, string workspace)
    {
        var file = Path.Combine(root, "watch-created.txt");
        var latency = Stopwatch.StartNew();
        using (var observed = new Observation(backend, () => Has(backend, "watch-created.txt")))
        {
            Write(file);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Console.WriteLine($"METRIC watcher_create_ms={latency.Elapsed.TotalMilliseconds:F3}");
        Check(Has(backend, "watch-created.txt"), "Watcher file create");
        var renamed = Path.Combine(root, "watch-renamed.txt");
        using (var observed = new Observation(backend, () => Has(backend, "watch-renamed.txt") && !Has(backend, "watch-created.txt")))
        {
            File.Move(file, renamed);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(!Has(backend, "watch-created.txt") && Has(backend, "watch-renamed.txt"), "Watcher file rename");
        using (var observed = new Observation(backend, () => !Has(backend, "watch-renamed.txt")))
        {
            File.Delete(renamed);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(!Has(backend, "watch-renamed.txt"), "Watcher file delete");

        var directory = Path.Combine(root, "watch-tree");
        using (var observed = new Observation(backend, () => Has(backend, "watch-tree") && Has(backend, "deep-child.txt")))
        {
            Write(Path.Combine(directory, "nested", "deep-child.txt"));
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(backend.QueryAll(SearchQuery.Parse("watch-tree/nested")).Count == 2, "Watcher directory create with subtree");
        var newDirectory = Path.Combine(root, "watch-moved");
        using (var observed = new Observation(backend, () =>
            backend.QueryAll(SearchQuery.Parse("watch-moved/nested")).Count == 2 && !Has(backend, "watch-tree")))
        {
            Directory.Move(directory, newDirectory);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(!backend.QueryAll(SearchQuery.Parse("watch-tree/nested")).Any(), "Watcher directory rename removes old subtree");
        var caseDirectory = Path.Combine(root, "WATCH-MOVED");
        using (var observed = new Observation(backend, () =>
            backend.QueryAll(SearchQuery.Parse("deep-child.txt")).Any(hit => hit.FullPath.Contains("WATCH-MOVED", StringComparison.Ordinal))))
        {
            Directory.Move(newDirectory, caseDirectory);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(backend.QueryAll(SearchQuery.Parse("deep-child.txt")).Single().FullPath.Contains("WATCH-MOVED", StringComparison.Ordinal),
            "Case-only directory rename updates stored paths");
        using (var observed = new Observation(backend, () => !Has(backend, "watch-moved") && !Has(backend, "deep-child.txt")))
        {
            Directory.Delete(caseDirectory, true);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(!Has(backend, "deep-child.txt"), "Watcher directory deletion removes entire subtree");

        var incoming = Path.Combine(workspace, "incoming");
        Write(Path.Combine(incoming, "nested", "move-in-child.txt"));
        var destination = Path.Combine(root, "moved-inside");
        using (var observed = new Observation(backend, () => Has(backend, "move-in-child.txt")))
        {
            Directory.Move(incoming, destination);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(backend.QueryAll(SearchQuery.Parse("moved-inside/nested")).Count == 2, "Populated subtree moved into watched root");
        using (var observed = new Observation(backend, () => !Has(backend, "move-in-child.txt")))
        {
            Directory.Move(destination, incoming);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(!Has(backend, "move-in-child.txt"), "Subtree moved outside root removed");
    }

    private static async Task OverflowRecovery(IndependentSearchBackend backend, string root)
    {
        var watcher = (IndexWatcher)typeof(IndependentSearchBackend)
            .GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
        var native = (List<FileSystemWatcher>)typeof(IndexWatcher)
            .GetField("_watchers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(watcher)!;
        foreach (var item in native)
            item.EnableRaisingEvents = false;
        var before = backend.Stats.Overflows;
        using (var observed = new Observation(backend, () =>
            backend.Stats.Overflows > before && Has(backend, "overflow-recovered.txt"), true))
        {
            Write(Path.Combine(root, "overflow-recovered.txt"));
            typeof(IndexWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(watcher, [watcher, new ErrorEventArgs(new InternalBufferOverflowException("Injected dropped event"))]);
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(backend.Stats.Overflows == before + 1 && Has(backend, "overflow-recovered.txt"),
            "Overflow invalidates snapshot and rescans dropped changes");
        using (var observed = new Observation(backend, () => Has(backend, "after-overflow.txt")))
        {
            Write(Path.Combine(root, "after-overflow.txt"));
            await observed.Completion.Task.WaitAsync(Deadline);
        }
        Check(Has(backend, "after-overflow.txt"), "Native watchers rearmed after error");
    }

    private static async Task DisposeFromNotification(IndependentSearchBackend backend, string root)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = 0;
        EventHandler callback = (_, _) =>
        {
            if (Interlocked.Exchange(ref disposing, 1) != 0)
                return;
            backend.Dispose();
            completed.TrySetResult();
        };
        backend.IndexChanged += callback;
        try
        {
            Write(Path.Combine(root, "dispose-notification.txt"));
            await completed.Task.WaitAsync(Deadline);
            Check(!backend.IsReady, "Dispose joins worker without subscriber self-deadlock");
        }
        finally
        {
            backend.IndexChanged -= callback;
        }
    }

    private static async Task Junction(string path, string target)
    {
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", path, target })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync().WaitAsync(Deadline);
        Check(process.ExitCode == 0 && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0,
            "Real unprivileged junction fixture");
    }

    private static bool Has(IndependentSearchBackend backend, string text) =>
        backend.QueryAll(SearchQuery.Parse(text)).Count > 0;

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Interlocked.Increment(ref _assertions);
    }

    private static void Throws<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, description); return; }
        throw new InvalidOperationException(description);
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string description) where T : Exception
    {
        try { await action().WaitAsync(Deadline); }
        catch (T) { Check(true, description); return; }
        throw new InvalidOperationException(description);
    }

    private sealed class Observation : IDisposable
    {
        private readonly IndependentSearchBackend _backend;
        private readonly bool _rescan;
        private readonly EventHandler _handler;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Observation(IndependentSearchBackend backend, Func<bool> predicate, bool rescan = false)
        {
            _backend = backend;
            _rescan = rescan;
            _handler = (_, _) =>
            {
                try
                {
                    if (predicate())
                        Completion.TrySetResult();
                }
                catch (Exception error) { Completion.TrySetException(error); }
            };
            if (rescan)
                backend.RescanCompleted += _handler;
            else
                backend.IndexChanged += _handler;
        }

        public void Dispose()
        {
            if (_rescan)
                _backend.RescanCompleted -= _handler;
            else
                _backend.IndexChanged -= _handler;
        }
    }
}
