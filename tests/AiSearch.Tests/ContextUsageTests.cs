using System.Net.Http;
using System.Text.Json;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static async Task LocalContextUsage()
    {
        var responses = new Queue<string>([
            """{"model":"actual-local","usage":{"prompt_tokens":123,"completion_tokens":400,"total_tokens":523,"prompt_tokens_details":{"cached_tokens":90}}}""",
            "{}",
            """{"usage":{"prompt_tokens":0},"context_window":8192}""",
            """{"usage":{"prompt_tokens":5},"context_window":"8192"}""",
            """{"usage":{"prompt_tokens":"123"}}""",
            """{"usage":{"prompt_tokens":-1}}""",
            """{"usage":{"prompt_tokens":1.5}}""",
            """{"usage":{"prompt_tokens":9223372036854775808}}""",
            """{"usage":{"completion_tokens":123}}""",
            """{"usage":null}""",
            """{"usage":[]}"""
        ]);
        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Response(LocalUsageResponse(responses.Dequeue())))));
        using var transport = new LocalEndpointTransport(new AiSearchSettings { Model = "selected-local" }, client);
        Check(transport.LastContextUsage is null);
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage == new AiContextUsage(123, null, "actual-local")
            { Provider = AiProviderKind.LocalEndpoint });
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage is null);
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage == new AiContextUsage(0, 8192, "selected-local"));
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage == new AiContextUsage(5, null, "selected-local"));
        while (responses.Count > 0)
        {
            await transport.CompleteAsync("s", "u", default);
            Check(transport.LastContextUsage is null);
        }
    }

    private static async Task CodexContextUsage()
    {
        var auth = Path.Combine(Folder("context-codex"), "auth.json");
        await File.WriteAllTextAsync(auth, """{"auth_mode":"chatgpt","tokens":{"access_token":"fixture"}}""");
        var responses = new Queue<string>([
            """{"model":"actual-codex","usage":{"input_tokens":27,"input_tokens_details":{"cached_tokens":20},"output_tokens":9,"total_tokens":36}}""",
            """{"model":"actual-codex","usage":{"input_tokens":10},"context_window":500000}""",
            "{}",
            """{"usage":{"input_tokens":"27"}}""",
            """{"usage":{"input_tokens":-27}}""",
            """{"usage":{"input_tokens":0}}""",
            """{"usage":true}"""
        ]);
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(Response(
            request.Method == HttpMethod.Get
                ? """{"models":[{"slug":"gpt-6.1-sol","visibility":"list","context_window":500000,"supported_reasoning_levels":[{"effort":"high"}]},{"slug":"gpt-6-luna","context_window":272000},{"slug":"gpt-6-astra","context_window":272000},{"slug":"missing"},{"slug":"invalid","context_window":"1050000"},{"slug":"zero","context_window":0},{"slug":"negative","context_window":-1}]}"""
                : CodexUsageResponse(responses.Dequeue())))));
        using var transport = new CodexSubscriptionTransport(new AiSearchSettings
        {
            Provider = AiProviderKind.CodexCli, Model = "gpt-6.1-sol"
        }, client, auth);
        var catalog = await transport.ProbeAsync(default);
        Check(catalog.Ok);
        var sol = catalog.ModelDescriptors.Single(model => model.Id == "gpt-6.1-sol");
        Check(sol.ContextWindow == 500000 && sol.Available && sol.DisplayName == "GPT-6.1 Sol"
            && sol.ReasoningEfforts.SequenceEqual(["default", "high"]));
        Check(catalog.ModelDescriptors.Single(model => model.Id == "gpt-6-luna").ContextWindow == 272000);
        Check(catalog.ModelDescriptors.Single(model => model.Id == "gpt-6-astra").ContextWindow == 272000);
        Check(catalog.ModelDescriptors.Where(model => model.Id is "missing" or "invalid" or "zero" or "negative")
            .All(model => model.ContextWindow is null));
        Check(AiModelCatalog.RequestedModels.All(model => model.ContextWindow is null));
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage == new AiContextUsage(27, null, "actual-codex")
            { Provider = AiProviderKind.CodexCli });
        await transport.CompleteJsonAsync("s", "u", "{}", default);
        Check(transport.LastContextUsage == new AiContextUsage(10, 500000, "actual-codex")
            { Provider = AiProviderKind.CodexCli });
        for (var index = 0; responses.Count > 0; index++)
        {
            await transport.CompleteAsync("s", "u", default);
            Check(index == 3 ? transport.LastContextUsage?.InputTokens == 0 : transport.LastContextUsage is null);
        }
    }

    private static async Task ClaudeContextUsage()
    {
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Folder("context-claude"));
        try
        {
            foreach (var (model, expected, capacity) in new (string, long?, long?)[]
            {
                ("usage-valid", 4329, 1000000), ("usage-cached", 4329, 1000000),
                ("usage-zero", 0, 1000000), ("usage-missing", null, null),
                ("usage-partial", null, null), ("usage-malformed", null, null),
                ("usage-overflow", null, null), ("usage-bad-capacity", 4329, null)
            })
            {
                var transport = new CliTransport(new AiSearchSettings
                {
                    Provider = AiProviderKind.ClaudeCli, Model = model, CliPath = Environment.ProcessPath
                });
                Check(await transport.CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", default) == "{\"ok\":true}");
                var usage = transport.LastContextUsage;
                Check(expected is null ? usage is null
                    : usage == new AiContextUsage(expected.Value, capacity, "claude-haiku-5-5")
                        { Provider = AiProviderKind.ClaudeCli });
                if (expected is null)
                    continue;
                await transport.CompleteJsonAsync("SYSTEM_SENTINEL", "USER_SENTINEL", "{}", default);
                Check(transport.LastContextUsage == usage);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await Throws<OperationCanceledException>(() => transport.CompleteAsync("s", "u", cancellation.Token));
                Check(transport.LastContextUsage is null);
            }
            foreach (var model in new[] { "usage-bad-json", "usage-error", "usage-no-result" })
            {
                var transport = new CliTransport(new AiSearchSettings
                {
                    Provider = AiProviderKind.ClaudeCli, Model = model, CliPath = Environment.ProcessPath
                });
                await Throws<InvalidDataException>(() => transport.CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", default));
                Check(transport.LastContextUsage is null);
            }
        }
        finally { Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous); }
    }

    private static string ClaudeUsageFixture(string model)
    {
        if (model == "usage-bad-json") return "{broken";
        if (model == "usage-error") return """{"is_error":true,"result":"error"}""";
        if (model == "usage-no-result") return """{"usage":{"input_tokens":1}}""";
        object? usage = model switch
        {
            "usage-missing" => null,
            "usage-partial" => new { input_tokens = 2 },
            "usage-malformed" => new { input_tokens = 2, cache_creation_input_tokens = "4327", cache_read_input_tokens = 0 },
            "usage-overflow" => new { input_tokens = long.MaxValue, cache_creation_input_tokens = 1, cache_read_input_tokens = 0 },
            _ => new
            {
                input_tokens = model == "usage-zero" ? 0 : 2,
                cache_creation_input_tokens = model is "usage-cached" or "usage-zero" ? 0 : 4327,
                cache_read_input_tokens = model == "usage-cached" ? 4327 : 0,
                cache_creation = new { ephemeral_5m_input_tokens = 4327 }
            }
        };
        return JsonSerializer.Serialize(new
        {
            type = "result", is_error = false, result = "{\"ok\":true}", usage,
            modelUsage = new Dictionary<string, object>
            {
                ["claude-haiku-5-5"] = new
                {
                    contextWindow = model == "usage-bad-capacity" ? (object)"1000000" : 1000000,
                    inputTokens = 987654, cacheCreationInputTokens = 4327
                }
            }
        });
    }

    private static async Task ContextUsageCancellation()
    {
        var entered = Signal();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            if (++calls == 2)
            {
                entered.TrySetResult();
                return release.Task;
            }
            return Task.FromResult(Response(LocalUsageResponse("{\"usage\":{\"prompt_tokens\":" + calls + "}}")));
        }));
        using var transport = new LocalEndpointTransport(new AiSearchSettings { Model = "fixture" }, client);
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage?.InputTokens == 1);
        using var cancellation = new CancellationTokenSource();
        var older = transport.CompleteAsync("s", "u", cancellation.Token);
        await entered.Task.WaitAsync(Deadline);
        Check(transport.LastContextUsage is null);
        await transport.CompleteAsync("s", "u", default);
        Check(transport.LastContextUsage?.InputTokens == 3);
        cancellation.Cancel();
        release.SetResult(Response(LocalUsageResponse("""{"usage":{"prompt_tokens":200}}""")));
        await Throws<OperationCanceledException>(() => older);
        Check(transport.LastContextUsage?.InputTokens == 3);

        var tracker = new ContextUsageTracker();
        var first = tracker.Begin();
        var second = tracker.Begin();
        var newest = new AiContextUsage(20, null, "fixture");
        tracker.Complete(second, newest, default);
        tracker.Complete(first, new AiContextUsage(10, null, "fixture"), default);
        Check(tracker.Value == newest);
    }

    private static async Task SessionContextUsage()
    {
        using var backend = new Backend([]);
        var calls = 0;
        var transport = new UsageTransport((self, _) =>
        {
            self.LastContextUsage = ++calls == 1 ? new AiContextUsage(44, 1000, "fixture") : null;
            return Task.FromResult(Json(Plan with { Clarification = "fixture" }));
        });
        using var session = new AiSearchSession(backend, new AiSearchSettings(), transport: transport);
        var first = await session.SubmitAsync("first", []);
        var second = await session.SubmitAsync("second", []);
        Check(first?.ContextUsage == new AiContextUsage(44, 1000, "fixture"));
        Check(second is not null && second.ContextUsage is null && first?.ContextUsage?.InputTokens == 44);

        var entered = Signal();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        calls = 0;
        var staleTransport = new UsageTransport((self, _) =>
        {
            if (++calls == 1)
            {
                entered.TrySetResult();
                return release.Task;
            }
            self.LastContextUsage = new AiContextUsage(99, null, "new");
            return Task.FromResult(Json(Plan with { Clarification = "fixture" }));
        });
        using var staleSession = new AiSearchSession(backend, new AiSearchSettings(), transport: staleTransport);
        var stale = staleSession.SubmitAsync("old", []);
        await entered.Task.WaitAsync(Deadline);
        var current = await staleSession.SubmitAsync("new", []);
        staleTransport.LastContextUsage = new AiContextUsage(999, null, "old");
        release.SetResult(Json(Plan));
        Check(await stale is null && current?.ContextUsage?.InputTokens == 99);
        Check(staleSession.Turns.Count == 2 && staleSession.Turns[^1].ContextUsage?.ModelId == "new");
        staleSession.Reset();
        Check(staleSession.Turns.Count == 0);

        calls = 0;
        var documentTransport = new UsageTransport((self, _) =>
        {
            self.LastContextUsage = new AiContextUsage(++calls == 1 ? 100 : 7, null, "document");
            return Task.FromResult(calls == 1
                ? ActionJson(new { action = "search", query = "missing" })
                : ActionJson(new { action = "ask", question = "fixture" }));
        });
        using var documents = new AiSearchSession(backend, Reading, transport: documentTransport);
        var documentTurn = await documents.SubmitAsync("read", []);
        Check(documentTurn?.DocumentResult?.Coverage.Searches == 1 && calls == 2);
        Check(documentTurn?.ContextUsage == new AiContextUsage(7, null, "document"));

        var invalidTransport = new UsageTransport((self, _) =>
        {
            self.LastContextUsage = new AiContextUsage(12, null, "invalid-output");
            return Task.FromResult("{}");
        });
        using var invalidSession = new AiSearchSession(backend, new AiSearchSettings(), transport: invalidTransport);
        var invalid = await invalidSession.SubmitAsync("invalid", []);
        Check(invalid?.Error is not null && invalid.ContextUsage?.InputTokens == 12);
    }

    private static string LocalUsageResponse(string metadata) =>
        "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]"
        + (metadata.Length > 2 ? "," + metadata[1..^1] : "") + "}";

    private static string CodexUsageResponse(string response) =>
        "data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}\n\n"
        + "data: {\"type\":\"response.completed\",\"response\":" + response + "}\n\n";

    private sealed class UsageTransport(Func<UsageTransport, CancellationToken, Task<string>> complete) : IModelTransport
    {
        public string Label => "fixture";
        public AiContextUsage? LastContextUsage { get; set; }
        public Task<string> CompleteAsync(string system, string conversation, CancellationToken token) => complete(this, token);
        public Task<ProviderProbe> ProbeAsync(CancellationToken token) =>
            Task.FromResult(new ProviderProbe(true, "", [], TimeSpan.Zero));
    }
}
