namespace QuadExplorer.AiSearch;

public static class AiModelCatalog
{
    private static readonly string[] ClaudeEfforts = ["default", "low", "medium", "high"];
    private static readonly string[] CodexEfforts = ["default", "low", "medium", "high", "xhigh", "max"];

    public static IReadOnlyList<AiModelDescriptor> RequestedModels { get; } = Array.AsReadOnly(new[]
    {
        Requested(AiProviderKind.ClaudeCli, "claude-opus-5-5", "Claude Opus 5.5", ClaudeEfforts),
        Requested(AiProviderKind.ClaudeCli, "claude-sonnet-5-5", "Claude Sonnet 5.5", ClaudeEfforts),
        Requested(AiProviderKind.ClaudeCli, "claude-haiku-5-5", "Claude Haiku 5.5", ClaudeEfforts),
        Requested(AiProviderKind.CodexCli, "gpt-6.1-sol", "GPT-6.1 Sol", CodexEfforts),
        Requested(AiProviderKind.CodexCli, "gpt-6-luna", "GPT-6 Luna", CodexEfforts),
        Requested(AiProviderKind.CodexCli, "gpt-6-astra", "GPT-6 Astra", CodexEfforts)
    });

    public static IReadOnlyList<AiModelDescriptor> GetModels(AiProviderKind provider) =>
        RequestedModels.Where(model => model.Provider == provider).ToArray();

    public static string DisplayName(AiProviderKind provider, string id) =>
        RequestedModels.FirstOrDefault(model => model.Provider == provider && model.Id == id)?.DisplayName ?? id;

    public static IReadOnlyList<string> GetReasoningEfforts(AiProviderKind provider, string id)
    {
        if (!AiSearchSettingsStore.SupportsReasoning(provider, id))
            return ["default"];
        return provider == AiProviderKind.CodexCli ? CodexEfforts : ClaudeEfforts;
    }

    internal static IReadOnlyList<AiModelDescriptor> Merge(AiProviderKind provider,
        IEnumerable<AiModelDescriptor> reported, string? unavailableReason = null)
    {
        var actual = reported.DistinctBy(model => model.Id, StringComparer.Ordinal).Take(100).ToArray();
        var result = new List<AiModelDescriptor>();
        foreach (var preferred in GetModels(provider))
            result.Add(actual.FirstOrDefault(model => model.Id == preferred.Id) ?? preferred with
            {
                UnavailableReason = unavailableReason ?? "Not reported by this account's model catalog."
            });
        result.AddRange(actual.Where(model => !result.Any(item => item.Id == model.Id)));
        return result;
    }

    private static AiModelDescriptor Requested(AiProviderKind provider, string id, string name, string[] efforts) =>
        new(provider, id, name, Array.AsReadOnly(efforts), false, "Refresh the provider catalog to check availability.");
}
