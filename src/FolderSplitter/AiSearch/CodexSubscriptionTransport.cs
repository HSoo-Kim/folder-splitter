using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace QuadExplorer.AiSearch;

/// <summary>Model-only Responses requests using the user's existing ChatGPT subscription login.</summary>
public sealed class CodexSubscriptionTransport : IModelTransport, IDisposable
{
    private static readonly Uri Endpoint = new("https://chatgpt.com/backend-api/codex/");
    private readonly AiSearchSettings settings;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string authenticationPath;
    private readonly ContextUsageTracker usage = new();
    public string Label => "ChatGPT subscription";
    public AiContextUsage? LastContextUsage => usage.Value;

    public CodexSubscriptionTransport(AiSearchSettings settings, HttpClient? client = null, string? authenticationPath = null)
    {
        this.settings = AiSearchSettingsStore.Normalize(settings);
        ownsClient = client is null;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        this.authenticationPath = authenticationPath ?? Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "auth.json");
    }

    public Task<string> CompleteAsync(string system, string conversation, CancellationToken token) =>
        CompleteResponseAsync(system, conversation, null, token);

    public Task<string> CompleteJsonAsync(string system, string conversation, string schema, CancellationToken token) =>
        CompleteResponseAsync(system, conversation, schema, token);

    private async Task<string> CompleteResponseAsync(string system, string conversation, string? schema, CancellationToken token)
    {
        var generation = usage.Begin();
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("Select a ChatGPT model first.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        try
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = settings.Model, ["store"] = false, ["stream"] = true,
                ["tools"] = Array.Empty<object>(), ["tool_choice"] = "none",
                ["instructions"] = system,
                ["input"] = new[] { new { role = "user", content = new[] { new { type = "input_text", text = conversation } } } }
            };
            if (settings.ReasoningEffort != "default") body["reasoning"] = new { effort = settings.ReasoningEffort };
            if (schema is not null)
            {
                using var parsed = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 16 });
                body["text"] = new
                {
                    format = new
                    {
                        type = "json_schema", name = "document_action", strict = true,
                        schema = parsed.RootElement.Clone()
                    }
                };
            }
            using var request = await RequestAsync(HttpMethod.Post, "responses", deadline.Token).ConfigureAwait(false);
            request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            CheckStatus(response);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = new StringBuilder();
            int received = 0;
            bool completed = false;
            AiContextUsage? contextUsage = null;
            while (await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { } line)
            {
                received += line.Length;
                if (received > 2 * 1024 * 1024) throw new InvalidDataException("ChatGPT response exceeds the size limit.");
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line[5..].Trim();
                if (data is "" or "[DONE]") continue;
                using var frame = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 24 });
                var value = frame.RootElement;
                string? type = value.GetProperty("type").GetString();
                if (value.TryGetProperty("item", out var item)
                    && item.TryGetProperty("type", out var itemType)
                    && (itemType.GetString()?.Contains("call", StringComparison.Ordinal) ?? false))
                    throw new InvalidDataException("ChatGPT returned a tool call to a model-only request.");
                switch (type)
                {
                    case "response.output_text.delta":
                        text.Append(value.GetProperty("delta").GetString());
                        if (text.Length > 131072) throw new InvalidDataException("ChatGPT text exceeds the size limit.");
                        break;
                    case "response.completed":
                        completed = true;
                        if (value.TryGetProperty("response", out var result))
                            contextUsage = ContextUsageReader.Read(result, AiProviderKind.CodexCli, settings.Model);
                        break;
                    case "error":
                    case "response.failed":
                    case "response.incomplete":
                        throw new InvalidOperationException("ChatGPT did not complete the response. Check model access and subscription limits.");
                }
            }
            if (!completed || string.IsNullOrWhiteSpace(text.ToString()))
                throw new InvalidDataException("ChatGPT returned an incomplete or empty response.");
            usage.Complete(generation, contextUsage, token);
            return text.ToString();
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("ChatGPT did not respond within 75 seconds.", error);
        }
        catch (JsonException error) { throw new InvalidDataException("ChatGPT returned malformed response data.", error); }
    }

    public async Task<ProviderProbe> ProbeAsync(CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = await RequestAsync(HttpMethod.Get, "models?client_version=0.162.1", deadline.Token).ConfigureAwait(false);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            CheckStatus(response);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("ChatGPT model catalog exceeds the size limit.");
                buffer.Write(bytes, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var descriptors = new List<AiModelDescriptor>();
            foreach (var model in document.RootElement.GetProperty("models").EnumerateArray().Take(100))
            {
                string? id = model.GetProperty("slug").GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.Any(char.IsControl)
                    || model.TryGetProperty("visibility", out var visible) && visible.GetString() == "hide") continue;
                var efforts = new List<string> { "default" };
                if (model.TryGetProperty("supported_reasoning_levels", out var levels))
                    foreach (var level in levels.EnumerateArray())
                        if (level.TryGetProperty("effort", out var effort)
                            && effort.GetString() is "low" or "medium" or "high" or "xhigh" or "max")
                            efforts.Add(effort.GetString()!);
                descriptors.Add(new(AiProviderKind.CodexCli, id,
                    AiModelCatalog.DisplayName(AiProviderKind.CodexCli, id), efforts.Distinct().ToArray(), true,
                    ContextWindow: ContextUsageReader.Capacity(model, "context_window")));
            }
            return new(true, "ChatGPT subscription model catalog loaded.", descriptors.Select(model => model.Id).ToArray(), clock.Elapsed)
            {
                ModelDescriptors = AiModelCatalog.Merge(AiProviderKind.CodexCli, descriptors)
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException
            or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
        {
            string detail = error is OperationCanceledException ? "ChatGPT model discovery timed out." : error.Message;
            return new(false, detail, [], clock.Elapsed)
            {
                ModelDescriptors = AiModelCatalog.Merge(AiProviderKind.CodexCli, [], detail)
            };
        }
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path, CancellationToken token)
    {
        if (!File.Exists(authenticationPath))
            throw new FileNotFoundException("Sign into ChatGPT in Codex first. No API key is required.");
        if (new FileInfo(authenticationPath).Length > 65536)
            throw new InvalidDataException("Codex subscription login file exceeds the size limit.");
        using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(authenticationPath, token).ConfigureAwait(false));
        if (!auth.RootElement.TryGetProperty("auth_mode", out var mode) || mode.GetString() != "chatgpt"
            || !auth.RootElement.TryGetProperty("tokens", out var tokens)
            || !tokens.TryGetProperty("access_token", out var access) || access.GetString() is not { Length: > 0 } accessToken)
            throw new InvalidDataException("A ChatGPT subscription login is required; API-key credentials are not used.");
        var request = new HttpRequestMessage(method, new Uri(Endpoint, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("FolderSplitter/0.8.9");
        if (tokens.TryGetProperty("account_id", out var account) && account.GetString() is { Length: > 0 } accountId)
            request.Headers.Add("ChatGPT-Account-ID", accountId);
        return request;
    }

    private static void CheckStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = response.StatusCode == HttpStatusCode.Unauthorized
            ? "ChatGPT login expired. Refresh your login in Codex and try again."
            : $"ChatGPT returned HTTP {(int)response.StatusCode}. Check model access and subscription limits.";
        throw new HttpRequestException(detail, null, response.StatusCode);
    }

    public void Dispose() { if (ownsClient) client.Dispose(); }
}
