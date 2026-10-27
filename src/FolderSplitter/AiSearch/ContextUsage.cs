using System.Text.Json;

namespace QuadExplorer.AiSearch;

internal sealed class ContextUsageTracker
{
    private readonly object gate = new();
    private long generation;
    private AiContextUsage? value;

    public AiContextUsage? Value { get { lock (gate) return value; } }

    public long Begin()
    {
        lock (gate)
        {
            value = null;
            return ++generation;
        }
    }

    public void Complete(long request, AiContextUsage? usage, CancellationToken token)
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (generation == request)
                value = usage;
        }
    }
}

internal static class ContextUsageReader
{
    public static long? Count(JsonElement value, string name)
    {
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var count)
            && count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out var result) && result >= 0
            ? result : null;
    }

    public static long? Capacity(JsonElement value, string name) =>
        Count(value, name) is > 0 and var capacity ? capacity : null;

    public static AiContextUsage? Read(JsonElement value, AiProviderKind provider, string model)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("usage", out var usage))
            return null;
        var input = Count(usage, provider == AiProviderKind.LocalEndpoint ? "prompt_tokens" : "input_tokens");
        if (input is null)
            return null;
        if (value.TryGetProperty("model", out var reportedModel) && reportedModel.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(reportedModel.GetString()))
            model = reportedModel.GetString()!;
        long? capacity = Capacity(value, "context_window");
        if (provider == AiProviderKind.ClaudeCli)
        {
            var created = Count(usage, "cache_creation_input_tokens");
            var cached = Count(usage, "cache_read_input_tokens");
            if (created is null || cached is null || input > long.MaxValue - created
                || input + created > long.MaxValue - cached)
                return null;
            input += created + cached;
            // modelUsage supplies model identity and capacity, not another token total.
            if (value.TryGetProperty("modelUsage", out var models) && models.ValueKind == JsonValueKind.Object)
            {
                var entries = models.EnumerateObject().Take(2).ToArray();
                JsonElement metadata;
                if (models.TryGetProperty(model, out metadata))
                    capacity = Capacity(metadata, "contextWindow");
                else if (entries.Length == 1)
                {
                    model = entries[0].Name;
                    capacity = Capacity(entries[0].Value, "contextWindow");
                }
            }
        }
        return new AiContextUsage(input.Value, capacity, model) { Provider = provider };
    }
}
