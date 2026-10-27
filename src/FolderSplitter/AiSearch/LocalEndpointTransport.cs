using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace QuadExplorer.AiSearch;

public sealed class LocalEndpointTransport : IModelTransport, IDisposable
{
    private readonly AiSearchSettings _settings;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _endpoint;
    private readonly ContextUsageTracker _usage = new();
    public string Label => $"Local: {_settings.Model}";
    public AiContextUsage? LastContextUsage => _usage.Value;

    public LocalEndpointTransport(AiSearchSettings settings, HttpClient? client = null)
    {
        _settings = AiSearchSettingsStore.Normalize(settings);
        if (!Uri.TryCreate(_settings.Endpoint + "/", UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || !(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
            throw new InvalidDataException("The local AI endpoint must use localhost or a loopback IP address.");
        _endpoint = endpoint;
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }

    public async Task<string> CompleteAsync(string system, string conversation, CancellationToken token)
    {
        var generation = _usage.Begin();
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_settings.Model))
            throw new InvalidOperationException("Select a local model in AI provider settings first.");
        var payload = new Dictionary<string, object>
        {
            ["model"] = _settings.Model, ["stream"] = false,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new[] { new { role = "system", content = system }, new { role = "user", content = conversation } }
        };
        if (_settings.ReasoningEffort != "default")
            payload["reasoning_effort"] = _settings.ReasoningEffort;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, "chat/completions"))
        {
            Content = JsonContent.Create(payload)
        };
        using var document = await SendAsync(request, token).ConfigureAwait(false);
        try
        {
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("The local model returned no text.");
            _usage.Complete(generation,
                ContextUsageReader.Read(document.RootElement, AiProviderKind.LocalEndpoint, _settings.Model), token);
            return content;
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("The local endpoint returned an invalid chat completion.", error);
        }
    }

    public async Task<ProviderProbe> ProbeAsync(CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_endpoint, "models"));
            using var document = await SendAsync(request, token).ConfigureAwait(false);
            var models = document.RootElement.GetProperty("data").EnumerateArray().Take(100)
                .Select(model => model.GetProperty("id").GetString())
                .Where(model => !string.IsNullOrWhiteSpace(model) && model.Length <= 200 && !model.Any(char.IsControl))
                .Select(model => model!).Distinct(StringComparer.Ordinal).ToArray();
            return new ProviderProbe(true, "Local model endpoint responded.", models, clock.Elapsed)
            {
                ModelDescriptors = models.Select(id => new AiModelDescriptor(AiProviderKind.LocalEndpoint,
                    id, id, AiModelCatalog.GetReasoningEfforts(AiProviderKind.LocalEndpoint, id), true)).ToArray()
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or JsonException
            or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
        {
            return new ProviderProbe(false, error.Message, [], clock.Elapsed);
        }
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Local AI endpoint returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null, response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > 131072)
                    throw new InvalidDataException("Local AI response exceeds the size limit.");
                buffer.Write(bytes, 0, count);
            }
            return JsonDocument.Parse(Encoding.UTF8.GetString(buffer.ToArray()), new JsonDocumentOptions { MaxDepth = 20 });
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new HttpRequestException("Local AI endpoint did not respond within 75 seconds.", error);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Local AI endpoint returned malformed JSON.", error);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
