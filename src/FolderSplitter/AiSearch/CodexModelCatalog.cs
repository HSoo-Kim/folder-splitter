namespace QuadExplorer.AiSearch;

internal static class CodexModelCatalog
{
    public static async Task<ProviderProbe> ListAsync(AiSearchSettings settings, CancellationToken token)
    {
        using var transport = new CodexSubscriptionTransport(settings);
        return await transport.ProbeAsync(token).ConfigureAwait(false);
    }
}
