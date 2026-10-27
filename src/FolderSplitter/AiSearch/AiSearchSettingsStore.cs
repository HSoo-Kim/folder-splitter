using System.Text.Json;

namespace QuadExplorer.AiSearch;

public static class AiSearchSettingsStore
{
    public static string PathFor(string sessionPath) => Path.ChangeExtension(sessionPath, ".ai-search.json");

    public static bool SupportsReasoning(AiProviderKind provider, string? model)
    {
        var id = (model ?? "").Trim().ToLowerInvariant();
        if (provider == AiProviderKind.LocalEndpoint)
        {
            // GPT-OSS defines all three compatible named levels. Other local models may
            // expose boolean thinking or different levels; do not promise this control for them.
            id = id[(id.LastIndexOf('/') + 1)..];
            return id is "gpt-oss" || id.StartsWith("gpt-oss:", StringComparison.Ordinal)
                || id.StartsWith("gpt-oss-", StringComparison.Ordinal);
        }
        if (provider == AiProviderKind.CodexCli)
            return id is "gpt-6.1-sol" or "gpt-6-luna" or "gpt-6-astra"
                or "gpt-5.6-sol" or "gpt-5.6-terra" or "gpt-5.6-luna";
        if (provider != AiProviderKind.ClaudeCli)
            return false;
        if (id.EndsWith("[1m]", StringComparison.Ordinal))
            id = id[..^4];
        // Explicit model IDs only: aliases can resolve to older models on third-party
        // providers. The model catalog resolves aliases before offering them in chat.
        return id is "claude-fable-5" or "claude-fable-5-1"
            or "claude-opus-4-6" or "claude-opus-4-7" or "claude-opus-4-8"
            or "claude-opus-5" or "claude-opus-5-5"
            or "claude-sonnet-4-6" or "claude-sonnet-5" or "claude-sonnet-5-5"
            or "claude-haiku-5-5";
    }

    public static AiSearchSettings Normalize(AiSearchSettings? settings)
    {
        settings ??= new AiSearchSettings();
        if (!Enum.IsDefined(settings.Provider))
            throw new InvalidDataException("Unknown AI provider. Select a supported provider explicitly.");
        if (!Enum.IsDefined(settings.Mode))
            throw new InvalidDataException("Unknown AI search mode.");
        var endpoint = (settings.Endpoint ?? "").Trim().TrimEnd('/');
        if (endpoint.Length == 0)
            endpoint = new AiSearchSettings().Endpoint;
        var filenamePrompt = settings.FilenamePrompt ?? "";
        if (filenamePrompt.Length > 2000
            || filenamePrompt.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException("Personal instructions must be at most 2000 characters and contain no control characters other than line breaks or tabs.");
        filenamePrompt = filenamePrompt.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (endpoint.Length > 2048 || (settings.Model?.Length ?? 0) > 200
            || settings.Model?.Any(char.IsControl) == true
            || (settings.CliPath?.Length ?? 0) > 32768
            || (settings.ClaudeCliPath?.Length ?? 0) > 32768
            || (settings.CodexCliPath?.Length ?? 0) > 32768)
            throw new InvalidDataException("AI provider settings exceed their limits.");
        var model = (settings.Model ?? "").Trim();
        var effort = (settings.ReasoningEffort ?? "").Trim().ToLowerInvariant();
        if (effort.Length == 0)
            effort = "default";
        if (effort is not ("default" or "low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidDataException("Reasoning effort must be default, low, medium, high, xhigh or max.");
        if (effort is "xhigh" or "max" && settings.Provider != AiProviderKind.CodexCli)
            throw new InvalidDataException("This reasoning effort is supported only by Codex models.");
        if (!SupportsReasoning(settings.Provider, model))
            effort = "default";
        return settings with
        {
            Endpoint = endpoint, Model = model, FilenamePrompt = filenamePrompt, ReasoningEffort = effort,
            CliPath = string.IsNullOrWhiteSpace(settings.CliPath) ? null : settings.CliPath.Trim(),
            ClaudeCliPath = string.IsNullOrWhiteSpace(settings.ClaudeCliPath) ? null : settings.ClaudeCliPath.Trim(),
            CodexCliPath = string.IsNullOrWhiteSpace(settings.CodexCliPath) ? null : settings.CodexCliPath.Trim(),
            Width = Math.Clamp(settings.Width, 320, 1200)
        };
    }

    public static AiSearchSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AiSearchSettings();
        if (new FileInfo(path).Length > 65536)
            throw new InvalidDataException("AI settings file is too large.");
        try
        {
            return Normalize(JsonSerializer.Deserialize<AiSearchSettings>(File.ReadAllText(path)));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("AI settings file contains invalid JSON.", error);
        }
    }

    public static void Save(string path, AiSearchSettings settings)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(Normalize(settings)));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
