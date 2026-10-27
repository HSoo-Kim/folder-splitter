using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static Task ProviderCatalog()
    {
        Check(AiModelCatalog.RequestedModels.Count == 6);
        Check(AiModelCatalog.RequestedModels.All(model => !model.Available && model.UnavailableReason is not null));
        Check(AiModelCatalog.GetModels(AiProviderKind.ClaudeCli).Select(model => model.Id)
            .SequenceEqual(["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"]));
        Check(AiModelCatalog.GetModels(AiProviderKind.CodexCli).Select(model => model.Id)
            .SequenceEqual(["gpt-6.1-sol", "gpt-6-luna", "gpt-6-astra"]));
        Check(AiModelCatalog.GetModels(AiProviderKind.LocalEndpoint).Count == 0);
        Check(AiModelCatalog.DisplayName(AiProviderKind.ClaudeCli, "claude-opus-5-5") == "Claude Opus 5.5");
        Check(AiModelCatalog.DisplayName(AiProviderKind.CodexCli, "claude-opus-5-5") == "claude-opus-5-5");
        Check(AiModelCatalog.GetReasoningEfforts(AiProviderKind.LocalEndpoint, "custom").SequenceEqual(["default"]));
        foreach (var effort in new[] { "low", "medium", "high", "xhigh", "max" })
            Check(AiSearchSettingsStore.Normalize(new AiSearchSettings
            {
                Provider = AiProviderKind.CodexCli, Model = "gpt-6-astra", ReasoningEffort = effort
            }).ReasoningEffort == effort);
        Check(!AiSearchSettingsStore.SupportsReasoning(AiProviderKind.CodexCli, "gpt-6-made-up"));
        return Task.CompletedTask;
    }


    private static async Task ProviderCredentials()
    {
        var previousCodex = Environment.GetEnvironmentVariable("CODEX_HOME");
        var previousClaude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var previousKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var home = Folder("provider-credentials");
        Environment.SetEnvironmentVariable("CODEX_HOME", home);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", home);
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "must-not-be-inherited");
        try
        {
            var settings = new AiSearchSettings { Provider = AiProviderKind.CodexCli, CodexCliPath = Environment.ProcessPath };
            Check(!(await AiSearchSession.ListModelsAsync(settings, default)).Ok);
            File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fixture\"}");
            Check(!(await AiSearchSession.ListModelsAsync(settings, default)).Ok);
            var claude = new CliTransport(new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli, ClaudeCliPath = Environment.ProcessPath,
                CliPath = Path.Combine(home, "missing.exe"), Model = "fixture"
            });
            Check((await claude.CompleteAsync("SYSTEM_SENTINEL", "USER_SENTINEL", default)).Contains("\"ok\":true"));
            var path = Path.Combine(home, "settings.json");
            AiSearchSettingsStore.Save(path, new AiSearchSettings
            {
                Provider = AiProviderKind.CodexCli, ClaudeCliPath = @"C:\Claude\claude.exe",
                CodexCliPath = @"C:\Codex\codex.exe", CliPath = @"C:\legacy.exe"
            });
            var restored = AiSearchSettingsStore.Load(path);
            Check(restored.ClaudeCliPath == @"C:\Claude\claude.exe" && restored.CodexCliPath == @"C:\Codex\codex.exe"
                && restored.CliPath == @"C:\legacy.exe");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodex);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousClaude);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousKey);
        }
    }

    private static async Task<int> CodexCatalogChild(string[] args)
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME")!;
        if (args.Contains("--print") || args.Any(arg => arg.Contains("must-not-be-submitted", StringComparison.Ordinal))
            || Environment.GetEnvironmentVariable("OPENAI_API_KEY") is not null
            || !args.Contains("model_provider=\"openai\"") || !args.Contains("mcp_servers={}")
            || Directory.EnumerateFileSystemEntries(".").Any()
            || Directory.GetFiles(home).Select(Path.GetFileName).Single() != "auth.json"
            || await Console.In.ReadToEndAsync() != "")
            return 21;
        using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(home, "auth.json")));
        var mode = auth.RootElement.GetProperty("fixture").GetString()!;
        if (mode.StartsWith("codex-catalog-", StringComparison.Ordinal))
        {
            await using var pipe = new NamedPipeClientStream(".", mode, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(15000);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(Environment.CurrentDirectory);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await reader.ReadLineAsync();
            return 0;
        }
        if (mode == "error") { Console.Error.WriteLine("fixture stderr failure"); return 7; }
        if (mode == "malformed") { Console.WriteLine("{broken"); return 0; }
        if (mode == "missing") { Console.WriteLine("{}"); return 0; }
        if (mode == "oversize") { Console.WriteLine(new string('x', 4 * 1024 * 1024 + 4096)); return 0; }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            models = new[]
            {
                new { slug = "gpt-6-astra", display_name = "Provider spelling", visibility = "list",
                    supported_reasoning_levels = new[] { "low", "medium", "high", "xhigh", "max", "ultra" }.Select(effort => new { effort }) },
                new { slug = "installed-extra", display_name = "Installed extra", visibility = "list",
                    supported_reasoning_levels = Array.Empty<string>().Select(effort => new { effort }) },
                new { slug = "hidden-model", display_name = "Hidden", visibility = "hide",
                    supported_reasoning_levels = Array.Empty<string>().Select(effort => new { effort }) },
                new { slug = "bad\nid", display_name = "Invalid", visibility = "list",
                    supported_reasoning_levels = Array.Empty<string>().Select(effort => new { effort }) }
            },
            metadata = new string('x', 70000)
        }));
        return 0;
    }

    private static async Task<int> SubscriptionSmoke()
    {
        var failures = 0;
        foreach (var provider in new[] { AiProviderKind.ClaudeCli, AiProviderKind.CodexCli })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var catalog = await AiSearchSession.ListModelsAsync(new AiSearchSettings { Provider = provider }, deadline.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { provider = provider.ToString(), catalog.Ok,
                models = catalog.Models, elapsedMs = (long)catalog.Elapsed.TotalMilliseconds, catalog.Detail }));
            foreach (var model in AiModelCatalog.GetModels(provider))
            {
                var available = catalog.ModelDescriptors.FirstOrDefault(item => item.Id == model.Id);
                if (available?.Available != true)
                {
                    failures++;
                    Console.WriteLine(JsonSerializer.Serialize(new { model = model.Id, status = "unavailable",
                        reason = available?.UnavailableReason ?? catalog.Detail }));
                    continue;
                }
                var clock = Stopwatch.StartNew();
                try
                {
                    var transport = new CliTransport(new AiSearchSettings { Provider = provider, Model = model.Id });
                    var response = await transport.CompleteAsync(
                        "Return only the JSON object {\"ok\":true}. Do not use tools.", "Synthetic connection test.", deadline.Token);
                    using var result = JsonDocument.Parse(response);
                    if (!result.RootElement.GetProperty("ok").GetBoolean())
                        throw new InvalidDataException("Synthetic response did not contain ok=true.");
                    if (transport.LastContextUsage is not { InputTokens: >= 0, ModelId: { Length: > 0 } } usage)
                        throw new InvalidDataException("Provider did not report measured input tokens.");
                    Console.WriteLine(JsonSerializer.Serialize(new { model = model.Id, status = "passed",
                        inputTokens = usage.InputTokens, contextWindow = usage.ContextWindow ?? available.ContextWindow,
                        elapsedMs = clock.ElapsedMilliseconds }));
                }
                catch (Exception error)
                {
                    failures++;
                    Console.WriteLine(JsonSerializer.Serialize(new { model = model.Id, status = "failed",
                        elapsedMs = clock.ElapsedMilliseconds, error = error.Message }));
                }
            }
        }
        return failures == 0 ? 0 : 1;
    }
}
