using System.Net;
using System.Diagnostics;

namespace QuadExplorer.AiSearch;

/// <summary>Queries only configured and well-known loopback services; never starts a server or downloads a model.</summary>
public static class LocalModelDiscovery
{
    public static async Task<ProviderProbe> DiscoverAsync(AiSearchSettings settings,
        Func<AiSearchSettings, CancellationToken, Task<ProviderProbe>> probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        var endpoints = new[] { settings.Endpoint.Trim().TrimEnd('/'), "http://127.0.0.1:11434/v1", "http://127.0.0.1:1234/v1" }
            .Where(endpoint => endpoint.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = await Task.WhenAll(endpoints.Select(async endpoint =>
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
                || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)))
                return (Endpoint: endpoint, Result: new ProviderProbe(false, "Only loopback endpoints are allowed.", [], TimeSpan.Zero));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var result = await probe(settings with { Provider = AiProviderKind.LocalEndpoint, Endpoint = endpoint }, deadline.Token).ConfigureAwait(false);
                return (Endpoint: endpoint, Result: result);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { return (Endpoint: endpoint, Result: new ProviderProbe(false, "Local endpoint timed out.", [], TimeSpan.Zero)); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or System.Net.Http.HttpRequestException)
            { return (Endpoint: endpoint, Result: new ProviderProbe(false, error.Message, [], TimeSpan.Zero)); }
        })).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var models = results.Where(r => r.Result.Ok).SelectMany(r => r.Result.Models.Distinct(StringComparer.Ordinal)
            .Select(id => new AiModelDescriptor(AiProviderKind.LocalEndpoint, id, $"{id}  ({r.Endpoint})",
                AiModelCatalog.GetReasoningEfforts(AiProviderKind.LocalEndpoint, id), true) { Endpoint = r.Endpoint })).ToArray();
        return new ProviderProbe(results.Any(r => r.Result.Ok),
            string.Join("\n", results.Select(r => $"{r.Endpoint}: {r.Result.Detail}")),
            models.Select(m => m.Id).ToArray(), clock.Elapsed) { ModelDescriptors = models };
    }
}
