using QuadExplorer.AiSearch;
using System.Net;
using System.Net.Http;

internal static partial class Program
{
    private static async Task ProviderAccessChecks()
    {
        const string custom = "http://127.0.0.1:4321/v1";
        const string ollama = "http://127.0.0.1:11434/v1";
        const string studio = "http://127.0.0.1:1234/v1";
        var seen = new List<string>();
        Task<ProviderProbe> Fixture(AiSearchSettings settings, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            seen.Add(settings.Endpoint);
            return Task.FromResult(settings.Endpoint switch
            {
                custom => new ProviderProbe(true, "custom", ["shared", "custom-only"], TimeSpan.Zero),
                ollama => new ProviderProbe(true, "ollama", ["shared"], TimeSpan.Zero),
                studio => new ProviderProbe(true, "empty", [], TimeSpan.Zero),
                _ => new ProviderProbe(false, "no listener", [], TimeSpan.Zero)
            });
        }
        var result = await LocalModelDiscovery.DiscoverAsync(new AiSearchSettings { Endpoint = custom }, Fixture, default);
        Check(seen.Count == 3 && seen.Contains(custom) && seen.Contains(ollama) && seen.Contains(studio));
        Check(result.ModelDescriptors.Count == 3 && result.ModelDescriptors.Count(m => m.Id == "shared") == 2);
        Check(result.ModelDescriptors.Any(m => m.Id == "shared" && m.Endpoint == custom));
        Check(result.ModelDescriptors.Any(m => m.Id == "shared" && m.Endpoint == ollama));
        var selected = new AiSearchSettings { Endpoint = result.ModelDescriptors.Single(m => m.Id == "shared" && m.Endpoint == ollama).Endpoint!, Model = "shared" };
        string file = Path.Combine(Folder("provider-endpoint-persistence"), "settings.json");
        AiSearchSettingsStore.Save(file, selected);
        var loaded = AiSearchSettingsStore.Load(file);
        Check(loaded.Provider == AiProviderKind.LocalEndpoint && loaded.Endpoint == ollama && loaded.Model == "shared");
        seen.Clear();
        var rejected = await LocalModelDiscovery.DiscoverAsync(new AiSearchSettings { Endpoint = "http://example.com/v1" }, Fixture, default);
        Check(!seen.Contains("http://example.com/v1") && rejected.Detail.Contains("Only loopback"));
        var none = await LocalModelDiscovery.DiscoverAsync(new AiSearchSettings { Endpoint = custom },
            (_, _) => Task.FromResult(new ProviderProbe(false, "no listener", [], TimeSpan.Zero)), default);
        Check(!none.Ok && none.ModelDescriptors.Count == 0);
        var empty = await LocalModelDiscovery.DiscoverAsync(new AiSearchSettings { Endpoint = custom },
            (_, _) => Task.FromResult(new ProviderProbe(true, "no models", [], TimeSpan.Zero)), default);
        Check(empty.Ok && empty.ModelDescriptors.Count == 0);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool cancelledObserved = false;
        try { await LocalModelDiscovery.DiscoverAsync(new AiSearchSettings(), Fixture, cancelled.Token); }
        catch (OperationCanceledException) { cancelledObserved = true; }
        Check(cancelledObserved);

        using var http = new HttpClient(new LocalProviderFixtureHandler());
        Task<ProviderProbe> HttpFixture(AiSearchSettings settings, CancellationToken token)
        {
            using var transport = new LocalEndpointTransport(settings, http);
            return transport.ProbeAsync(token);
        }
        var wire = await LocalModelDiscovery.DiscoverAsync(new AiSearchSettings { Endpoint = custom }, HttpFixture, default);
        Check(wire.ModelDescriptors.Count == 2 && wire.ModelDescriptors.All(m => m.Id == "same"));
        Check(wire.ModelDescriptors.Any(m => m.Endpoint == custom) && wire.ModelDescriptors.Any(m => m.Endpoint == studio));
        Check(wire.Detail.Contains("HTTP 503"));

        ProviderProbe Failure(string detail) => new(false, detail, [], TimeSpan.Zero);
        Check(ProviderAccess.ClassifyCodex(Failure("Sign into ChatGPT in Codex first. No API key is required.")) == ProviderAuthentication.Unauthenticated);
        Check(ProviderAccess.ClassifyCodex(Failure("ChatGPT login expired. Refresh your login in Codex and try again.")) == ProviderAuthentication.Unauthenticated);
        Check(ProviderAccess.ClassifyCodex(Failure("ChatGPT returned HTTP 403. Check model access and subscription limits.")) == ProviderAuthentication.Unknown);
        Check(ProviderAccess.ClassifyCodex(Failure("Network unavailable")) == ProviderAuthentication.Unknown);
        Check(ProviderAccess.ParseClaudeStatus("{\"loggedIn\":false}") == ProviderAuthentication.Unauthenticated);
        Check(ProviderAccess.ParseClaudeStatus("{\"loggedIn\":true}") == ProviderAuthentication.Authenticated);
        Check(ProviderAccess.ParseClaudeStatus("{\"error\":\"network\"}") == ProviderAuthentication.Unknown);
        Check(ProviderAccess.ParseClaudeStatus("invalid") == ProviderAuthentication.Unknown);
        string executable = Path.GetFullPath(Path.Combine(Folder("provider-login-arguments"), "provider & unsafe.exe"));
        bool localRejected = false;
        try { ProviderAccess.CreateLoginStartInfo(AiProviderKind.LocalEndpoint, executable); }
        catch (ArgumentException) { localRejected = true; }
        Check(localRejected);
    }

    private sealed class LocalProviderFixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/v1/models")
                throw new InvalidOperationException("Unexpected fixture request.");
            return Task.FromResult(request.RequestUri!.Port == 11434
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"same\"}]}") });
        }
    }
}
