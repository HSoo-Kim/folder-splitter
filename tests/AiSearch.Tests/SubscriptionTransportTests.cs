using System.Net;
using System.Net.Http;
using System.Text.Json;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static async Task CodexCatalog()
    {
        string authPath = Path.Combine(Folder("subscription-wire"), "auth.json");
        const string credentials = """{"auth_mode":"chatgpt","tokens":{"access_token":"fixture-token","account_id":"fixture-account"}}""";
        await File.WriteAllTextAsync(authPath, credentials);
        var settings = new AiSearchSettings { Provider = AiProviderKind.CodexCli, Model = "gpt-6.1-sol", ReasoningEffort = "high" };
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            Check(request.RequestUri!.Host == "chatgpt.com" && request.RequestUri.Scheme == "https");
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "fixture-token");
            Check(request.Headers.GetValues("ChatGPT-Account-ID").Single() == "fixture-account");
            if (request.Method == HttpMethod.Get)
                return Response("""{"models":[{"slug":"gpt-6.1-sol","visibility":"list","supported_reasoning_levels":[{"effort":"low"},{"effort":"high"}]},{"slug":"hidden","visibility":"hide"}]}""");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var value = body.RootElement;
            Check(value.GetProperty("tools").GetArrayLength() == 0 && value.GetProperty("tool_choice").GetString() == "none");
            Check(!value.GetProperty("store").GetBoolean() && value.GetProperty("stream").GetBoolean());
            Check(value.GetProperty("model").GetString() == "gpt-6.1-sol");
            Check(value.GetProperty("reasoning").GetProperty("effort").GetString() == "high");
            Check(value.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString() == "USER_SENTINEL");
            return Response("data: {\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"ok\\\":true}\"}\n\n"
                + "data: {\"type\":\"response.completed\"}\n\n");
        }));
        using var transport = new CodexSubscriptionTransport(settings, client, authPath);
        var catalog = await transport.ProbeAsync(default);
        Check(catalog.Ok && catalog.Models.SequenceEqual(["gpt-6.1-sol"]));
        var sol = catalog.ModelDescriptors.Single(model => model.Id == "gpt-6.1-sol");
        Check(sol.Available && sol.ReasoningEfforts.SequenceEqual(["default", "low", "high"]));
        Check(!catalog.ModelDescriptors.Single(model => model.Id == "gpt-6-luna").Available);
        Check(await transport.CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", default) == "{\"ok\":true}");
        Check(await File.ReadAllTextAsync(authPath) == credentials);

        using var jsonClient = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var format = body.RootElement.GetProperty("text").GetProperty("format");
            Check(format.GetProperty("type").GetString() == "json_schema");
            Check(format.GetProperty("name").GetString() == "document_action");
            Check(format.GetProperty("strict").GetBoolean());
            Check(format.GetProperty("schema").GetProperty("required")[0].GetString() == "command");
            Check(body.RootElement.GetProperty("tools").GetArrayLength() == 0);
            return Response("data: {\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"command\\\":{\\\"action\\\":\\\"ask\\\",\\\"question\\\":\\\"Where?\\\"}}\"}\n\n"
                + "data: {\"type\":\"response.completed\"}\n\n");
        }));
        using var structured = new CodexSubscriptionTransport(settings, jsonClient, authPath);
        Check((await structured.CompleteJsonAsync("s", "u",
            """{"type":"object","properties":{"command":{"type":"object"}},"required":["command"],"additionalProperties":false}""",
            default)).Contains("\"command\"", StringComparison.Ordinal));

        foreach (string invalid in new[]
        {
            "data: {broken}\n",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"incomplete\"}\n",
            "data: {\"type\":\"response.output_item.added\",\"item\":{\"type\":\"function_call\"}}\n",
            new string('x', 2 * 1024 * 1024 + 1)
        })
        {
            using var badClient = new HttpClient(new Handler((_, _) => Task.FromResult(Response(invalid))));
            using var bad = new CodexSubscriptionTransport(settings, badClient, authPath);
            await Throws<InvalidDataException>(() => bad.CompleteAsync("s", "u", default));
        }
        using var failedClient = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Response("data: {\"type\":\"response.failed\"}\n"))));
        using var failed = new CodexSubscriptionTransport(settings, failedClient, authPath);
        await Throws<InvalidOperationException>(() => failed.CompleteAsync("s", "u", default));
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            using var errorClient = new HttpClient(new Handler((_, _) => Task.FromResult(Response("{}", status))));
            using var failing = new CodexSubscriptionTransport(settings, errorClient, authPath);
            await Throws<HttpRequestException>(() => failing.CompleteAsync("s", "u", default));
            Check(!(await failing.ProbeAsync(default)).Ok);
        }
        var entered = Signal();
        using var cancelClient = new HttpClient(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Cancellation was not observed.");
        }));
        using var cancellable = new CodexSubscriptionTransport(settings, cancelClient, authPath);
        using var cancellation = new CancellationTokenSource();
        var pending = cancellable.CompleteAsync("s", "u", cancellation.Token);
        await entered.Task.WaitAsync(Deadline);
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => pending);
        Check(cancellable.LastContextUsage is null);
    }
}
