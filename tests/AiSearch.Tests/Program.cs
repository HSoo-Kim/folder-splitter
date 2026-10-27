using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using QuadExplorer.AiSearch;
using QuadExplorer.Search;

internal static partial class Program
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static string _root = "";
    private static readonly SearchPlan Plan = new(["report"], [], [], [], ItemKind.Any,
        SearchScope.Everywhere, null, null, null, null, false, null);
    private static int _assertions;

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--document-parser", var parserDirectory])
        {
            int result = await BundledDocumentReader(parserDirectory);
            if (result != 0) return result;
            await ContentDiscoveryChecks.BundledAsync(parserDirectory);
            Console.WriteLine("BUNDLED_LATE_DOCUMENT_PASS");
            return 0;
        }
        if (args is ["--subscription-smoke"])
            return await SubscriptionSmoke();
        if (args.Length >= 2 && args[0] == "debug" && args[1] == "models")
            return await CodexCatalogChild(args);
        if (args.Contains("--print") || args.Contains("--input-format"))
            return await CliChild(args);
        if (args.Contains("--claude-models"))
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(80));
            var models = await AiSearchSession.ListModelsAsync(new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli
            }, deadline.Token);
            Console.WriteLine(JsonSerializer.Serialize(models));
            return models.Ok && models.Models.Count > 0 ? 0 : 1;
        }
        _root = Directory.CreateTempSubdirectory("ai-search-tests-").FullName;
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("strict model JSON and path rejection", ModelValidation),
            ("bounded user-only model context", ModelPrivacy),
            ("personal instructions stay bounded redacted user context", FilenamePromptContext),
            ("document personal instructions stay bounded redacted user context", DocumentPersonalInstructions),
            ("plan bounds and normalization", PlanBounds),
            ("local HTTP request and response contract", HttpContract),
            ("local HTTP reasoning effort mapping", HttpEffort),
            ("metadata-only local model catalog", LocalModels),
            ("local HTTP failures and cancellation", HttpFailures),
            ("local endpoint loopback boundary", EndpointBoundary),
            ("real files, OR groups and metadata", FileFiltering),
            ("scope boundaries and literal AND queries", ScopedSearch),
            ("refinement only narrows real previous hits", Refinement),
            ("truncated refinements recover omitted hits without widening scope", TruncatedRefinement),
            ("clarification and index readiness", Readiness),
            ("bounded results and stale index entries", ResultLimits),
            ("real independent search index integration", RealIndex),
            ("generation suppresses stale completions", SessionStale),
            ("session cancellation, reset and disposal", SessionLifecycle),
            ("session settings and provider errors", SessionErrors),
            ("session preserves previous plan and paired turns", SessionContext),
            ("personal instructions reach both modes while document bodies stay isolated", FilenamePromptRouting),
            ("document search reads across folders and grounds citations", DocumentFolders),
            ("document action schema rejects fabricated authority", DocumentActions),
            ("document citations require actual exact evidence", DocumentCitations),
            ("document folder-path continuation accepts spaces", DocumentContinuation),
            ("quotations tolerate formatting but never changed words", CitationFormatting),
            ("rejected citations get a bounded exact-quote retry", CitationRetry),
            ("unsupported answer parts get one repair attempt before the evidence fallback", VerificationRetry),
            ("quotations survive extraction artifacts but not changed numbers", CitationExtractedText),
            ("mislabeled citation sources are matched to the excerpt actually quoted", CitationSourceMislabeled),
            ("filename mode reads zero bodies and mode contexts stay separate", DocumentModeIsolation),
            ("document cancellation reset settings and generation isolation", DocumentCancellation),
            ("mode switches cancel only pending turns and reject stale completions", ModeSwitchCancellation),
            ("visible transcript remains complete with bounded mode contexts", ModeContextBounds),
            ("document candidate listing action and read limits", DocumentBounds),
            ("document parser failures and empty evidence remain honest", DocumentFailures),
            ("document boundary rejects redirected and stale files", DocumentPathBoundary),
            ("batched scoped content discovery", DocumentBatchesAndDiscovery),
            ("content scans stay within authorized folders and their descendants", DocumentContentScope),
            ("contradictory document answers are not published", DocumentContradiction),
            ("document follow-ups and explicit targets", DocumentFollowupTargets),
            ("document parser adapter", DocumentReaderAdapter),
            ("bounded local content discovery and relevant excerpts", ContentDiscoveryChecks.RunAsync),
            ("claims verified against quoted evidence", DocumentVerificationChecks),
            ("local model discovery and explicit login", ProviderAccessChecks),
            ("settings persistence contains settings only", SettingsRoundtrip),
            ("filename prompt persistence normalization and bounds", FilenamePromptSettings),
            ("reasoning settings validation and capability boundaries", ReasoningSettings),
            ("explicit CLI provider errors", CliErrors),
            ("CLI stdin, isolation and process cancellation", CliIsolation),
            ("CLI reasoning effort mapping", CliEffort),
            ("metadata-only CLI model catalog and cancellation", CliModels),
            ("typed provider catalog and explicit model capabilities", ProviderCatalog),
            ("Codex metadata isolation, errors and cancellation", CodexCatalog),
            ("subscription credentials and per-provider executable selection", ProviderCredentials),
            ("per-request local usage and unknown metadata", LocalContextUsage),
            ("per-request Codex usage and catalog capacities", CodexContextUsage),
            ("Claude JSON result and cache-aware input usage", ClaudeContextUsage),
            ("usage publication cancellation and request generation", ContextUsageCancellation),
            ("session last-request usage and stale-turn isolation", SessionContextUsage)
        };
        var failures = 0;
        try
        {
            foreach (var (name, run) in tests)
            {
                try
                {
                    await run().WaitAsync(Deadline);
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {name}: {error}");
                }
            }
        }
        finally
        {
            Directory.Delete(_root, true);
            Console.WriteLine($"CLEANUP fixture_removed={!Directory.Exists(_root)}");
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed; {_assertions} assertions.");
        return failures == 0 ? 0 : 1;
    }

    private static string Json(SearchPlan plan) => JsonSerializer.Serialize(plan, ModelSearchPlanner.JsonOptions);
    private static Task<SearchPlan> Parse(string json) =>
        new ModelSearchPlanner(new StubTransport(json)).PlanAsync([new(ChatRole.User, "reports")], null, default);

    private static async Task ModelValidation()
    {
        var parsed = await Parse("```json\n" + Json(Plan) + "\n```");
        Check(parsed.AllTerms.SequenceEqual(["report"]));
        foreach (var response in new[] { "{}", "null", "[]", "not JSON", "{\"allTerms\":[]}",
            Json(Plan)[..^1] + ",\"hits\":[\"C:\\\\invented.txt\"]}",
            Json(Plan)[..^1] + ",\"kind\":\"File\"}",
            Json(Plan with { AllTerms = [@"C:\invented.txt"] }),
            Json(Plan with { AllTerms = ["../secret"] }),
            Json(Plan with { Excluded = ["a/b"] }),
            Json(Plan with { Extensions = ["*.pdf"] }),
            Json(Plan with { AllTerms = null! }),
            Json(Plan with { AnyOf = [null!] }),
            Json(Plan with { MinBytes = 10, MaxBytes = 1 }),
            "```javascript\n" + Json(Plan) + "\n```",
            new string('x', 32769) })
            await Throws<InvalidDataException>(() => Parse(response));
    }

    private static async Task ModelPrivacy()
    {
        var transport = new StubTransport(Json(Plan));
        var planner = new ModelSearchPlanner(transport);
        var turns = Enumerable.Range(0, 10).Select(index => new ChatTurn(ChatRole.User,
            "turn" + index + " " + new string('x', 2100))).ToList();
        turns.Add(new(ChatRole.Assistant, "secret assistant path", new AiSearchResult(Plan,
            [new AiHit(@"C:\secret\private.txt", false, 3, DateTime.UtcNow)], 1, false, null)));
        turns.Add(new(ChatRole.User, "find \"C:\\secret\\private.txt\" and /home/private"));
        await planner.PlanAsync(turns, Plan with { Clarification = @"C:\private" }, default);
        using var context = JsonDocument.Parse(transport.Conversation!);
        var users = context.RootElement.GetProperty("userTurns");
        Check(users.GetArrayLength() == 6);
        Check(!transport.Conversation!.Contains("private", StringComparison.Ordinal));
        Check(!transport.Conversation.Contains("secret assistant", StringComparison.Ordinal));
        Check(!transport.Conversation.Contains("turn0", StringComparison.Ordinal));
        Check(users.EnumerateArray().All(value => value.GetString()!.Length <= 2000));
        Check(context.RootElement.GetProperty("previousPlan").GetProperty("allTerms")[0].GetString() == "report");
    }

    private static async Task PlanBounds()
    {
        var normalized = SearchPlanValidator.Normalize(Plan with
        {
            AllTerms = [" report ", "REPORT"], Extensions = ["PDF", ".pdf"],
            ModifiedAfterUtc = new DateTime(2026, 1, 1)
        });
        Check(normalized.AllTerms.Count == 1 && normalized.Extensions.SequenceEqual([".pdf"]));
        Check(normalized.ModifiedAfterUtc?.Kind == DateTimeKind.Utc);
        Check(SearchPlanValidator.Normalize(Plan with { AllTerms = [] }).Clarification is not null);
        foreach (var invalid in new[] { Plan with { AllTerms = Enumerable.Repeat("x", 13).ToArray() },
            Plan with { AnyOf = Enumerable.Repeat<IReadOnlyList<string>>(["a"], 5).ToArray() },
            Plan with { AnyOf = [Enumerable.Repeat("x", 9).ToArray()] },
            Plan with { AnyOf = [[]] }, Plan with { Kind = (ItemKind)99 }, Plan with { Scope = (SearchScope)99 },
            Plan with { AllTerms = [new string('x', 81)] }, Plan with { AllTerms = ["a\nb"] },
            Plan with { MinBytes = -1 }, Plan with { Clarification = new string('x', 501) },
            Plan with { ModifiedAfterUtc = DateTime.UtcNow, ModifiedBeforeUtc = DateTime.UnixEpoch } })
            await Throws<InvalidDataException>(() => Task.FromResult(SearchPlanValidator.Normalize(invalid)));
    }

    private static async Task HttpContract()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
            {
                Check(request.RequestUri!.AbsolutePath == "/v1/models");
                return Response("{\"data\":[{\"id\":\"local-model\"}]}");
            }
            Check(request.RequestUri!.AbsolutePath == "/v1/chat/completions");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Check(body.RootElement.GetProperty("model").GetString() == "local-model");
            Check(!body.RootElement.GetProperty("stream").GetBoolean());
            Check(body.RootElement.GetProperty("response_format").GetProperty("type").GetString() == "json_object");
            var messages = body.RootElement.GetProperty("messages");
            Check(messages.GetArrayLength() == 2 && messages[0].GetProperty("role").GetString() == "system");
            Check(messages[1].GetProperty("content").GetString() == "user");
            Check(!body.RootElement.TryGetProperty("tools", out _));
            Check(!body.RootElement.TryGetProperty("reasoning_effort", out _));
            return Response(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = Json(Plan) } } } }));
        }));
        using var transport = new LocalEndpointTransport(new AiSearchSettings { Model = "local-model" }, client);
        var probe = await transport.ProbeAsync(default);
        Check(probe.Ok && probe.Models.SequenceEqual(["local-model"]));
        Check(await transport.CompleteAsync("system", "user", default) == Json(Plan));
        Check(calls == 2);
        transport.Dispose();
        using var after = await client.GetAsync("http://localhost/v1/models");
        Check(after.IsSuccessStatusCode);
    }

    private static async Task HttpEffort()
    {
        foreach (var (model, effort, expected) in new (string, string, string?)[]
        {
            ("gpt-oss:20b", "default", null), ("gpt-oss:20b", "low", "low"),
            ("openai/gpt-oss-20b", "medium", "medium"), ("gpt-oss:120b", "high", "high"),
            ("llama3.2", "high", null), ("qwen3:8b", "medium", null)
        })
        {
            var calls = 0;
            using var client = new HttpClient(new Handler(async (request, token) =>
            {
                calls++;
                Check(request.Method == HttpMethod.Post);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Check(body.RootElement.GetProperty("model").GetString() == model);
                var hasEffort = body.RootElement.TryGetProperty("reasoning_effort", out var actual);
                Check(expected is null ? !hasEffort : hasEffort && actual.GetString() == expected);
                return Response("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");
            }));
            using var transport = new LocalEndpointTransport(new AiSearchSettings
            {
                Model = model, ReasoningEffort = effort
            }, client);
            Check(await transport.CompleteAsync("s", "u", default) == "ok" && calls == 1);
        }
    }

    private static async Task LocalModels()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(Deadline);
        var accepted = listener.AcceptTcpClientAsync(deadline.Token);
        var catalog = AiSearchSession.ListModelsAsync(new AiSearchSettings
        {
            Endpoint = $"http://127.0.0.1:{port}/v1", Model = "not-an-inference"
        }, deadline.Token);
        using var connection = await accepted;
        await using (var stream = connection.GetStream())
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            Check(await reader.ReadLineAsync(deadline.Token) == "GET /v1/models HTTP/1.1");
            for (var line = await reader.ReadLineAsync(deadline.Token); !string.IsNullOrEmpty(line);
                line = await reader.ReadLineAsync(deadline.Token))
                Check(!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            const string json = "{\"data\":[{\"id\":\"actual-installed\"},{\"id\":\"actual-installed\"},{\"id\":\"gpt-oss:20b\"},{\"id\":\"bad\\nname\"}]}";
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteAsync($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}");
        }
        var result = await catalog;
        Check(result.Ok && result.Models.SequenceEqual(["actual-installed", "gpt-oss:20b"]));
        deadline.Cancel();
        await Throws<OperationCanceledException>(() => AiSearchSession.ListModelsAsync(new AiSearchSettings(), deadline.Token));
        var invalid = await AiSearchSession.ListModelsAsync(new AiSearchSettings { Endpoint = "https://example.com" }, default);
        Check(!invalid.Ok && invalid.Models.Count == 0);
    }

    private static async Task HttpFailures()
    {
        foreach (var (status, content) in new[] { (HttpStatusCode.BadRequest, "{\"error\":\"no json mode\"}"),
            (HttpStatusCode.Redirect, ""), (HttpStatusCode.OK, "<html>"), (HttpStatusCode.OK, "{}"),
            (HttpStatusCode.OK, "{\"choices\":[]}"), (HttpStatusCode.OK, new string('x', 131073)) })
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(content, status))));
            using var transport = new LocalEndpointTransport(new AiSearchSettings { Model = "m" }, client);
            await Throws<Exception>(() => transport.CompleteAsync("s", "u", default));
            Check(!(await transport.ProbeAsync(default)).Ok);
        }
        var entered = Signal();
        using var cancel = new CancellationTokenSource();
        using var waitingClient = new HttpClient(new Handler(async (_, token) =>
        {
            var cancelled = Signal();
            using var registration = token.Register(() => cancelled.TrySetCanceled(token));
            entered.TrySetResult();
            await cancelled.Task;
            return Response("{}");
        }));
        using var waiting = new LocalEndpointTransport(new AiSearchSettings { Model = "m" }, waitingClient);
        var task = waiting.CompleteAsync("s", "u", cancel.Token);
        await entered.Task.WaitAsync(Deadline);
        cancel.Cancel();
        await Throws<OperationCanceledException>(() => task);
    }

    private static async Task EndpointBoundary()
    {
        foreach (var endpoint in new[] { "https://example.com/v1", "http://localhost.evil/v1",
            "http://user@localhost/v1", "file:///tmp", "http://localhost/v1?x=y", "http://192.168.1.2/v1" })
            await Throws<InvalidDataException>(() =>
            {
                using var transport = new LocalEndpointTransport(new AiSearchSettings { Endpoint = endpoint });
                return Task.CompletedTask;
            });
        foreach (var endpoint in new[] { "http://localhost:1234/v1", "http://127.0.0.1/v1", "http://[::1]/v1" })
        {
            using var transport = new LocalEndpointTransport(new AiSearchSettings { Endpoint = endpoint });
            Check(transport.Label.Length > 0);
        }
        using var missingModel = new LocalEndpointTransport(new AiSearchSettings());
        await Throws<InvalidOperationException>(() => missingModel.CompleteAsync("s", "u", default));
    }

    private static async Task FileFiltering()
    {
        var root = Folder("filters");
        var alpha = FileAt(root, "alpha-report.pdf", 20);
        var beta = FileAt(root, "beta-report.pdf", 30);
        var skipped = FileAt(root, "skip-report.pdf", 25);
        var text = FileAt(root, "alpha-report.txt", 20);
        var old = FileAt(root, "old-report.pdf", 10);
        var modified = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        foreach (var path in new[] { alpha, beta, skipped, text })
            File.SetLastWriteTimeUtc(path, modified);
        File.SetLastWriteTimeUtc(old, modified.AddYears(-1));
        using var backend = new Backend([alpha, beta, skipped, text, old]);
        var result = await new ConversationalSearch(backend).RunAsync(Plan with
        {
            AnyOf = [["alpha", "beta", "skip"], ["report"]], Excluded = ["skip"], Extensions = ["pdf"],
            Kind = ItemKind.File, MinBytes = 15, MaxBytes = 30,
            ModifiedAfterUtc = modified.AddDays(-1), ModifiedBeforeUtc = modified.AddDays(1)
        }, null, [root], default);
        Check(result.Hits.Select(hit => hit.FullPath).SequenceEqual([alpha, beta]));
        Check(result.Hits[0].Bytes == 20 && result.Hits[0].ModifiedUtc == modified);
        Check(backend.Queries.SequenceEqual(["\"report\""]));
        var orResult = await new ConversationalSearch(backend).RunAsync(Plan with
        {
            AllTerms = [], AnyOf = [["alpha", "beta"], ["report"]], Extensions = ["pdf"]
        }, null, [], default);
        Check(orResult.Hits.Select(hit => hit.FullPath).SequenceEqual([alpha, beta]));
        Check(backend.Queries.TakeLast(2).SequenceEqual(["\"alpha\"", "\"beta\""]));
        var folder = Directory.CreateDirectory(Path.Combine(root, "report-folder")).FullName;
        backend.Paths.Add(folder);
        var folders = await new ConversationalSearch(backend).RunAsync(Plan with { Kind = ItemKind.Folder },
            null, [], default);
        Check(folders.Hits.Single().FullPath == folder && folders.Hits[0].Bytes is null);
        var extensionOnly = await new ConversationalSearch(backend).RunAsync(Plan with
        {
            AllTerms = [], Extensions = ["txt"]
        }, null, [], default);
        Check(extensionOnly.Hits.Single().FullPath == text && backend.Queries.Last() == "\".txt\"");
        var overlap = FileAt(root, "alpha-beta-report.pdf");
        backend.Paths.Add(overlap);
        var deduplicated = await new ConversationalSearch(backend).RunAsync(Plan with
        {
            AllTerms = [], AnyOf = [["alpha", "beta"]]
        }, null, [], default);
        Check(deduplicated.Hits.Count(hit => hit.FullPath == overlap) == 1);
    }

    private static async Task ScopedSearch()
    {
        var root = Folder("scope");
        var sibling = Folder("scope-other");
        var nested = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        var active = FileAt(root, "meeting report.txt");
        var child = FileAt(nested, "meeting report.txt");
        var other = FileAt(sibling, "meeting report.txt");
        using var backend = new Backend([other, child, active]);
        var search = new ConversationalSearch(backend);
        var plan = Plan with { AllTerms = ["meeting report"], Scope = SearchScope.ActiveFolder };
        var result = await search.RunAsync(plan, null, [root, sibling], default);
        Check(result.Hits.Select(hit => hit.FullPath).SequenceEqual([child, active]));
        Check(backend.Queries.Single() == "\"meeting report\"");
        result = await search.RunAsync(plan with { Scope = SearchScope.VisibleFolders }, null, [root, sibling], default);
        Check(result.Hits.Count == 3);
        await Throws<InvalidOperationException>(() => search.RunAsync(plan, null, [], default));
    }

    private static async Task Refinement()
    {
        var root = Folder("refine");
        var first = FileAt(root, "report-a.pdf", 10);
        var second = FileAt(root, "report-b.pdf", 20);
        using var backend = new Backend([first, second]);
        var search = new ConversationalSearch(backend);
        var previous = await search.RunAsync(Plan, null, [], default);
        File.Delete(first);
        var newFile = FileAt(root, "report-c.pdf", 30);
        backend.Paths.Add(newFile);
        var result = await search.RunAsync(Plan with { Refine = true, MinBytes = 15 }, previous, [], default);
        Check(result.Hits.Select(hit => hit.FullPath).SequenceEqual([second]));
        Check(backend.Queries.Count == 1);
        await Throws<InvalidOperationException>(() => search.RunAsync(Plan with { Refine = true }, null, [], default));
    }

    private static async Task TruncatedRefinement()
    {
        string root = Folder("truncated-refinement");
        string other = Folder("truncated-refinement-other");
        var files = new List<string>();
        for (int i = 0; i < 205; i++)
            files.Add(FileAt(root, $"report-{i:D3}.txt"));
        for (int i = 0; i < 205; i++)
            files.Add(FileAt(root, $"report-{i:D3}.pdf"));
        string target = FileAt(root, "report-eligible.pdf", 30);
        files.Add(target);
        files.Add(FileAt(root, "eligible.pdf", 30));
        files.Add(FileAt(other, "report-eligible.pdf", 30));
        using var backend = new Backend(files);
        var search = new ConversationalSearch(backend);
        var initialPlan = Plan with { Kind = ItemKind.File, Scope = SearchScope.ActiveFolder };
        var initial = await search.RunAsync(initialPlan, null, [root], default);
        Check(initial.Truncated && initial.Hits.Count == 200 && !initial.Hits.Any(hit => hit.FullPath == target));
        var pdfs = await search.RunAsync(initialPlan with
        {
            Refine = true, Scope = SearchScope.Everywhere, Extensions = ["pdf"]
        }, initial, [other], default);
        Check(pdfs.Truncated && pdfs.Hits.Count == 200 && !pdfs.Hits.Any(hit => hit.FullPath == target));
        var narrowed = await search.RunAsync(initialPlan with
        {
            Refine = true, Scope = SearchScope.Everywhere, AllTerms = ["eligible"], MinBytes = 20
        }, pdfs, [other], default);
        Check(narrowed.Hits.Select(hit => hit.FullPath).SequenceEqual([target]) && !narrowed.Truncated);
        Check(backend.Queries[^1].Contains("\"report\"", StringComparison.Ordinal)
            && backend.Queries[^1].Contains("\"eligible\"", StringComparison.Ordinal)
            && backend.Queries[^1].Contains("\".pdf\"", StringComparison.Ordinal));
    }

    private static async Task Readiness()
    {
        using var backend = new Backend([]) { IsReady = false };
        var search = new ConversationalSearch(backend);
        var result = await search.RunAsync(Plan with { Clarification = "Which extension?" }, null, [], default);
        Check(result.Hits.Count == 0 && result.Examined == 0 && backend.Queries.Count == 0);
        await Throws<InvalidOperationException>(() => search.RunAsync(Plan, null, [], default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => search.RunAsync(Plan, null, [], cancellation.Token));
    }

    private static async Task ResultLimits()
    {
        var root = Folder("limits");
        var files = Enumerable.Range(0, 205).Select(index => FileAt(root, $"report-{index:D3}.txt")).ToList();
        files.Insert(0, Path.Combine(root, "report-missing.txt"));
        using var backend = new Backend(files);
        var result = await new ConversationalSearch(backend).RunAsync(Plan, null, [], default);
        Check(result.Hits.Count == ConversationalSearch.MaxResults && result.Truncated);
        Check(result.Hits.All(hit => File.Exists(hit.FullPath)));
        Check(result.Examined <= ConversationalSearch.MaxCandidates);
    }

    private static async Task RealIndex()
    {
        var root = Folder("real-index");
        var report = FileAt(root, "report-2026.pdf");
        FileAt(root, "notes.txt");
        var activeRoot = Directory.CreateDirectory(Path.Combine(root, "active")).FullName;
        var active = FileAt(activeRoot, "beta.pdf");
        var global = FileAt(root, "alpha.pdf");
        using var backend = new IndependentSearchBackend([root], Path.Combine(_root, "real.index"));
        await backend.BuildOrLoadAsync().WaitAsync(Deadline);
        var result = await new ConversationalSearch(backend).RunAsync(Plan, null, [root], default);
        Check(result.Hits.Single().FullPath == report);
        result = await new ConversationalSearch(backend).RunAsync(Plan with
        {
            AllTerms = [], AnyOf = [["alpha", "beta"]]
        }, null, [activeRoot], default);
        Check(result.Hits.Select(hit => hit.FullPath).SequenceEqual([active, global]));
    }

    private static async Task SessionStale()
    {
        using var backend = new Backend([]);
        var firstStarted = Signal();
        var firstResult = new TaskCompletionSource<SearchPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var planner = new Planner((_, _, _) =>
        {
            if (++calls == 1)
            {
                firstStarted.TrySetResult();
                return firstResult.Task;
            }
            return Task.FromResult(Plan with { Clarification = "New request" });
        });
        using var session = new AiSearchSession(backend, new AiSearchSettings(), planner);
        var first = session.SubmitAsync("old", []);
        await firstStarted.Task.WaitAsync(Deadline);
        var second = await session.SubmitAsync("new", []);
        firstResult.SetResult(Plan);
        Check(await first == null && second is not null);
        Check(session.Turns.Count == 2 && session.Turns[0].Text == "new");
        Check(session.Turns[1].Role == ChatRole.Assistant && backend.Queries.Count == 0);
    }

    private static async Task SessionLifecycle()
    {
        foreach (var action in new[] { "cancel", "reset", "dispose", "settings", "effort", "prompt" })
        {
            using var backend = new Backend([]);
            var entered = Signal();
            var release = new TaskCompletionSource<SearchPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken captured = default;
            var planner = new Planner((_, _, token) => { captured = token; entered.TrySetResult(); return release.Task; });
            using var session = new AiSearchSession(backend, new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli, Model = "claude-haiku-5-5"
            }, planner);
            var request = session.SubmitAsync("report", []);
            await entered.Task.WaitAsync(Deadline);
            switch (action)
            {
                case "cancel": session.Cancel(); break;
                case "reset": session.Reset(); break;
                case "settings": session.Settings = session.Settings with { Model = "changed" }; break;
                case "effort": session.Settings = session.Settings with { ReasoningEffort = "high" }; break;
                case "prompt": session.Settings = session.Settings with { FilenamePrompt = "Prefer exact names" }; break;
                default: session.Dispose(); break;
            }
            Check(captured.IsCancellationRequested);
            release.SetResult(Plan);
            Check(await request is null && session.Turns.Count == 0 && backend.Queries.Count == 0);
            if (action == "dispose")
                await Throws<ObjectDisposedException>(() => session.SubmitAsync("again", []));
        }
    }

    private static async Task SessionErrors()
    {
        using var backend = new Backend([]);
        var planner = new Planner((_, _, _) => throw new InvalidDataException("invalid model"));
        using var session = new AiSearchSession(backend, new AiSearchSettings(), planner);
        var turn = await session.SubmitAsync("report", []);
        Check(turn?.Error is not null && turn.Result is null && session.Turns.Count == 2);
        Check(turn?.ProviderLabel == "test" && turn.Elapsed is not null);
        session.Settings = session.Settings with { Width = 1, Model = "new" };
        Check(session.Settings.Width == 320 && session.Settings.Model == "new");
        Check(await session.SubmitAsync(" ", []) is null);
        await Throws<ArgumentException>(() => session.SubmitAsync(new string('x', 4001), []));
        var probe = await AiSearchSession.ProbeAsync(new AiSearchSettings { Endpoint = "https://example.com/v1" }, default);
        Check(!probe.Ok);
    }

    private static async Task SessionContext()
    {
        var root = Folder("session-context");
        var file = FileAt(root, "report.pdf");
        using var backend = new Backend([file]);
        var calls = 0;
        var planner = new Planner((turns, previous, _) =>
        {
            calls++;
            Check(turns[^1].Role == ChatRole.User);
            if (calls == 1)
                Check(previous is null && turns.Count == 1);
            else
                Check(previous?.AllTerms.SequenceEqual(["report"]) == true
                    && turns.Count == 3 && turns[1].Role == ChatRole.Assistant);
            return Task.FromResult(Plan with { Refine = calls > 1 });
        });
        using var session = new AiSearchSession(backend, new AiSearchSettings(), planner);
        Check((await session.SubmitAsync("report", [root]))?.Result?.Hits.Count == 1);
        Check((await session.SubmitAsync("only those", [root]))?.Result?.Hits.Count == 1);
        Check(session.Turns.Count == 4 && backend.Queries.Count == 1);
        var snapshot = session.Turns;
        session.Reset();
        Check(session.Turns.Count == 0 && snapshot.Count == 4);
    }

    private static async Task SettingsRoundtrip()
    {
        var path = AiSearchSettingsStore.PathFor(Path.Combine(_root, "session.json"));
        Check(path.EndsWith("session.ai-search.json", StringComparison.Ordinal));
        Check(AiSearchSettingsStore.Load(path) == new AiSearchSettings());
        var settings = new AiSearchSettings { Provider = AiProviderKind.ClaudeCli, Model = " claude-haiku-5-5 ",
            ReasoningEffort = " High ",
            Endpoint = "http://localhost:9999/v1/", CliPath = @"C:\tools\claude.exe", Visible = true,
            Width = 700, Floating = true };
        AiSearchSettingsStore.Save(path, settings);
        Check(AiSearchSettingsStore.Load(path) == AiSearchSettingsStore.Normalize(settings));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Check(!json.RootElement.TryGetProperty("Turns", out _));
        Check(!json.RootElement.TryGetProperty("Conversation", out _));
        Check(!Directory.EnumerateFiles(_root, "*.tmp").Any());
        await Throws<InvalidDataException>(() => Task.FromResult(
            AiSearchSettingsStore.Normalize(settings with { Provider = (AiProviderKind)99 })));
        File.WriteAllText(path, "{broken");
        await Throws<InvalidDataException>(() => Task.FromResult(AiSearchSettingsStore.Load(path)));
    }

    private static async Task ReasoningSettings()
    {
        Check(new AiSearchSettings().ReasoningEffort == "default");
        var legacy = JsonSerializer.Deserialize<AiSearchSettings>("{\"Provider\":1,\"Model\":\"claude-haiku-5-5\"}");
        Check(AiSearchSettingsStore.Normalize(legacy).ReasoningEffort == "default");
        foreach (var model in new[] { "claude-haiku-5-5", "claude-sonnet-4-6", "claude-opus-4-6",
            "claude-opus-4-7[1m]", "claude-opus-4-8", "claude-opus-5", "claude-sonnet-5-5",
            "claude-opus-5-5", "claude-fable-5", "claude-fable-5-1" })
            Check(AiSearchSettingsStore.SupportsReasoning(AiProviderKind.ClaudeCli, model));
        foreach (var model in new[] { "", "haiku", "sonnet", "opus", "claude-haiku-4-5",
            "claude-sonnet-4-5", "claude-opus-4-1", "unknown", "claude-haiku-5-5-typo" })
        {
            Check(!AiSearchSettingsStore.SupportsReasoning(AiProviderKind.ClaudeCli, model));
            Check(AiSearchSettingsStore.Normalize(new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli, Model = model, ReasoningEffort = "high"
            }).ReasoningEffort == "default");
        }
        Check(!AiSearchSettingsStore.SupportsReasoning(AiProviderKind.ClaudeCli, null));
        Check(!AiSearchSettingsStore.SupportsReasoning(AiProviderKind.CodexCli, "claude-haiku-5-5"));
        foreach (var effort in new[] { "default", "low", "medium", "high" })
            Check(AiSearchSettingsStore.Normalize(new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli, Model = "claude-haiku-5-5", ReasoningEffort = $" {effort.ToUpperInvariant()} "
            }).ReasoningEffort == effort);
        Check(AiSearchSettingsStore.Normalize(new AiSearchSettings { ReasoningEffort = null! }).ReasoningEffort == "default");
        foreach (var invalid in new[] { "max", "auto", "none", "high\nlow", new string('x', 1000) })
            await Throws<InvalidDataException>(() => Task.FromResult(AiSearchSettingsStore.Normalize(
                new AiSearchSettings { ReasoningEffort = invalid })));
    }

    private static async Task CliErrors()
    {
        var missing = new CliTransport(new AiSearchSettings { Provider = AiProviderKind.ClaudeCli,
            CliPath = Path.Combine(_root, "missing.exe") });
        await Throws<FileNotFoundException>(() => missing.CompleteAsync("s", "u", default));
        Check(!(await missing.ProbeAsync(default)).Ok);
        var codex = new CliTransport(new AiSearchSettings { Provider = AiProviderKind.CodexCli });
        await Throws<InvalidOperationException>(() => codex.CompleteAsync("s", "u", default));
        Check(!(await codex.ProbeAsync(default)).Ok);
        string? previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", Folder("missing-subscription"));
            Check(!(await AiSearchSession.ListModelsAsync(new AiSearchSettings
                { Provider = AiProviderKind.CodexCli }, default)).Ok);
        }
        finally { Environment.SetEnvironmentVariable("CODEX_HOME", previous); }
    }

    private static async Task CliIsolation()
    {
        var previousConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Folder("empty-cli-config"));
        try
        {
            var settings = new AiSearchSettings { Provider = AiProviderKind.ClaudeCli,
                CliPath = Environment.ProcessPath, Model = "fixture" };
            var transport = new CliTransport(settings);
            Check(await transport.CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", default) == "{\"ok\":true}");
            var pipeName = "ai-test-" + Guid.NewGuid().ToString("N");
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var connected = pipe.WaitForConnectionAsync();
            using var cancel = new CancellationTokenSource();
            var running = new CliTransport(settings with { Model = pipeName })
                .CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", cancel.Token);
            await connected.WaitAsync(Deadline);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var working = await reader.ReadLineAsync().WaitAsync(Deadline);
            Check(working is not null && Directory.Exists(working));
            cancel.Cancel();
            await Throws<OperationCanceledException>(() => running);
            Check(!Directory.Exists(Path.GetDirectoryName(working!)));
        }
        finally { Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig); }
    }

    private static async Task CliEffort()
    {
        var previousConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var previousEffort = Environment.GetEnvironmentVariable("CLAUDE_CODE_EFFORT_LEVEL");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Folder("effort-cli-config"));
        Environment.SetEnvironmentVariable("CLAUDE_CODE_EFFORT_LEVEL", "high");
        try
        {
            foreach (var (model, effort, expected) in new (string, string, string?)[]
            {
                ("claude-haiku-5-5", "default", null), ("claude-haiku-5-5", "low", "low"),
                ("claude-haiku-5-5", "medium", "medium"), ("claude-haiku-5-5", "high", "high"),
                ("claude-haiku-4-5", "high", null)
            })
            {
                var response = await new CliTransport(new AiSearchSettings
                {
                    Provider = AiProviderKind.ClaudeCli, CliPath = Environment.ProcessPath,
                    Model = model, ReasoningEffort = effort
                }).CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", default);
                using var document = JsonDocument.Parse(response);
                Check(document.RootElement.GetProperty("effort").GetString() == expected);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_EFFORT_LEVEL", previousEffort);
        }
    }

    private static async Task CliModels()
    {
        var previousConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Folder("models-cli-config"));
        try
        {
            var settings = new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli, CliPath = Environment.ProcessPath, Model = "catalog",
                ReasoningEffort = "high"
            };
            var result = await AiSearchSession.ListModelsAsync(settings, default);
            Check(result.Ok && result.Models.SequenceEqual(["claude-haiku-5-5", "installed-custom"]));
            foreach (var model in new[] { "catalog-error", "catalog-missing", "catalog-malformed", "catalog-unavailable" })
            {
                result = await AiSearchSession.ListModelsAsync(settings with { Model = model }, default);
                Check(!result.Ok && result.Models.Count == 0 && result.Detail.Length > 0);
            }
            var pipeName = "ai-test-" + Guid.NewGuid().ToString("N");
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var connected = pipe.WaitForConnectionAsync();
            using var cancel = new CancellationTokenSource();
            var running = AiSearchSession.ListModelsAsync(settings with { Model = pipeName }, cancel.Token);
            await connected.WaitAsync(Deadline);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var working = await reader.ReadLineAsync().WaitAsync(Deadline);
            cancel.Cancel();
            await Throws<OperationCanceledException>(() => running);
            Check(working is not null && !Directory.Exists(Path.GetDirectoryName(working)));
        }
        finally { Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig); }
    }

    private static async Task<int> CliChild(string[] args)
    {
        string Value(string flag)
        {
            var inline = args.FirstOrDefault(argument => argument.StartsWith(flag + "=", StringComparison.Ordinal));
            return inline is null ? args[Array.IndexOf(args, flag) + 1] : inline[(flag.Length + 1)..];
        }
        var metadata = Value("--input-format") == "stream-json";
        if (Value("--tools") != "" || !args.Contains("--no-session-persistence")
            || !args.Contains("--strict-mcp-config") || !args.Contains("--safe-mode")
            || !args.Contains("--restricted") || Value("--setting-sources") != ""
            || !metadata && !args.Contains("--system-prompt") || Value("--system-prompt-snapshot") != "off"
            || args.Any(argument => argument.Contains("SENTINEL", StringComparison.Ordinal))
            || Environment.GetEnvironmentVariable("CLAUDE_CODE_EFFORT_LEVEL") is not null
            || Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is not null
            || Directory.EnumerateFileSystemEntries(".").Any())
            return 9;
        var model = Value("--model");
        if (metadata)
        {
            if (Value("--output-format") != "stream-json" || !args.Contains("--verbose") || args.Contains("--effort")
                || Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT") != "sdk-ts"
                || args.Contains("--print") || args.Contains("--mcp-config") || args.Contains("--system-prompt"))
                return 10;
            using var request = JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            if (request.RootElement.GetProperty("type").GetString() != "control_request"
                || request.RootElement.GetProperty("request").GetProperty("subtype").GetString() != "initialize"
                || request.RootElement.GetProperty("request").GetProperty("systemPrompt").ValueKind != JsonValueKind.Array)
                return 11;
        }
        else if (Value("--output-format") != "json"
            || Value("--mcp-config") != "{\"mcpServers\":{}}"
            || await Console.In.ReadToEndAsync() != "SYSTEM_SENTINEL\n\nUSER_SENTINEL")
            return 12;
        if (model.StartsWith("ai-test-", StringComparison.Ordinal))
        {
            await using var pipe = new NamedPipeClientStream(".", model, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(15000);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(Environment.CurrentDirectory);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await reader.ReadLineAsync();
            return 0;
        }
        if (metadata)
        {
            if (model == "catalog-unavailable")
                return 0;
            if (model == "catalog-malformed")
                Console.WriteLine("{broken");
            else if (model == "catalog-error")
                Console.WriteLine("{\"type\":\"control_response\",\"response\":{\"subtype\":\"error\",\"request_id\":\"quadexplorer-models\",\"error\":\"unsupported\"}}");
            else
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    type = "control_response", response = new
                    {
                        subtype = "success", request_id = "quadexplorer-models",
                        response = model == "catalog-missing" ? (object)new { } : new
                        {
                            metadata = new string('x', 5000),
                            models = new[]
                            {
                                new { value = "haiku", resolvedModel = (string?)"claude-haiku-5-5" },
                                new { value = "claude-haiku-5-5", resolvedModel = (string?)null },
                                new { value = "installed-custom", resolvedModel = (string?)null },
                                new { value = "bad\nname", resolvedModel = (string?)null }
                            }
                        }
                    }
                }));
            // A real stream-json session stays alive after initialize. Catalog reading must
            // finish on the response frame, not depend on process exit/EOF.
            await Console.In.ReadLineAsync();
            return 0;
        }
        if (model.StartsWith("claude-haiku-", StringComparison.Ordinal))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result = JsonSerializer.Serialize(new { effort = args.Contains("--effort") ? Value("--effort") : null })
            }));
            return 0;
        }
        Console.WriteLine(model.StartsWith("usage-", StringComparison.Ordinal) ? ClaudeUsageFixture(model)
            : JsonSerializer.Serialize(new { result = "{\"ok\":true}" }));
        return 0;
    }

    private static string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    private static string FileAt(string root, string name, int length = 1)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }
    private static HttpResponseMessage Response(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException("Assertion failed.");
    }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { _assertions++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }

    private sealed class StubTransport(string response) : IModelTransport
    {
        public string Label => "test";
        public string? System { get; private set; }
        public string? Conversation { get; private set; }
        public Task<string> CompleteAsync(string system, string conversation, CancellationToken token)
        {
            System = system;
            Conversation = conversation;
            return Task.FromResult(response);
        }
        public Task<ProviderProbe> ProbeAsync(CancellationToken token) =>
            Task.FromResult(new ProviderProbe(true, "", [], TimeSpan.Zero));
    }

    private sealed class Planner(Func<IReadOnlyList<ChatTurn>, SearchPlan?, CancellationToken, Task<SearchPlan>> plan)
        : ISearchPlanner
    {
        public string Label => "test";
        public Task<SearchPlan> PlanAsync(IReadOnlyList<ChatTurn> turns, SearchPlan? previous, CancellationToken token) =>
            plan(turns, previous, token);
    }

    private sealed class Backend(IEnumerable<string> paths) : IPathSearchBackend
    {
        public List<string> Paths { get; } = paths.ToList();
        public List<string> Queries { get; } = [];
        public bool IsAvailable => true;
        public bool IsReady { get; set; } = true;
        public event EventHandler? IndexChanged { add { } remove { } }
        public Task<IReadOnlyList<SearchHit>> QueryAsync(string text, IReadOnlyList<string> roots, int limit,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Queries.Add(text);
            var parsed = SearchQuery.Parse(text);
            IReadOnlyList<SearchHit> hits = Paths.Where(parsed.Matches).Take(limit)
                .Select(path => new SearchHit(path, Directory.Exists(path))).ToArray();
            return Task.FromResult(hits);
        }
        public void Dispose() { }
    }
}
