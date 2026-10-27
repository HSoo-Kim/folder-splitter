using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace QuadExplorer.AiSearch;

public sealed class CliTransport : IModelTransport
{
    private readonly AiSearchSettings _settings;
    private readonly ContextUsageTracker _usage = new();
    public string Label => _settings.Provider == AiProviderKind.ClaudeCli ? "Claude CLI" : "ChatGPT subscription";
    public AiContextUsage? LastContextUsage => _usage.Value;

    public CliTransport(AiSearchSettings settings)
    {
        _settings = AiSearchSettingsStore.Normalize(settings);
        if (_settings.Provider == AiProviderKind.LocalEndpoint)
            throw new ArgumentException("Select a CLI provider explicitly.", nameof(settings));
    }

    public Task<string> CompleteAsync(string system, string conversation, CancellationToken token) =>
        CompleteResponseAsync(system, conversation, null, token);

    public Task<string> CompleteJsonAsync(string system, string conversation, string schema, CancellationToken token) =>
        CompleteResponseAsync(system, conversation, schema, token);

    private async Task<string> CompleteResponseAsync(string system, string conversation, string? schema, CancellationToken token)
    {
        var generation = _usage.Begin();
        token.ThrowIfCancellationRequested();
        if (_settings.Provider == AiProviderKind.CodexCli)
        {
            using var transport = new CodexSubscriptionTransport(_settings);
            var text = schema is null
                ? await transport.CompleteAsync(system, conversation, token).ConfigureAwait(false)
                : await transport.CompleteJsonAsync(system, conversation, schema, token).ConfigureAwait(false);
            _usage.Complete(generation, transport.LastContextUsage, token);
            return text;
        }
        var response = await ExecuteAsync(system + "\n\n" + conversation, false, token).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(response);
            var result = document.RootElement;
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("result", out var text)
                || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString())
                || result.TryGetProperty("is_error", out var error) && error.ValueKind != JsonValueKind.False)
                throw new InvalidDataException("Claude CLI returned an invalid result.");
            _usage.Complete(generation,
                ContextUsageReader.Read(result, AiProviderKind.ClaudeCli, _settings.Model), token);
            return text.GetString()!;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Claude CLI returned malformed result data.", error);
        }
    }

    private async Task<string> ExecuteAsync(string input, bool modelsOnly, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var executable = CliExecutableLocator.Resolve(_settings);
        var root = Directory.CreateTempSubdirectory("quadexplorer-ai-").FullName;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        using var process = new Process();
        try
        {
            var working = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            var config = Directory.CreateDirectory(Path.Combine(root, "config")).FullName;
            // Retain only the installed CLI's authentication, not its plugins, MCP configuration,
            // instructions or history. This temporary copy is never sent to the model.
            var installedConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            var credential = Path.Combine(installedConfig, ".credentials.json");
            if (File.Exists(credential))
                File.Copy(credential, Path.Combine(config, ".credentials.json"));
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = working,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[]
            {
                "--input-format", modelsOnly ? "stream-json" : "text",
                "--output-format", modelsOnly ? "stream-json" : "json",
                "--tools=", "--strict-mcp-config",
                "--safe-mode", "--restricted", "--disable-slash-commands", "--no-chrome",
                "--no-session-persistence", "--system-prompt-snapshot", "off",
                "--permission-mode", "dontAsk", "--permission-prompts", "none",
                "--setting-sources=", "--settings", "{\"disableAllHooks\":true}"
            })
                start.ArgumentList.Add(argument);
            if (!modelsOnly)
            {
                start.ArgumentList.Add("--print");
                start.ArgumentList.Add("--mcp-config");
                start.ArgumentList.Add("{\"mcpServers\":{}}");
                start.ArgumentList.Add("--system-prompt");
                start.ArgumentList.Add("Follow the supplied application instructions and return only the requested JSON. Never use tools.");
            }
            if (_settings.Model.Length > 0)
            {
                start.ArgumentList.Add("--model");
                start.ArgumentList.Add(_settings.Model);
            }
            if (modelsOnly)
                start.ArgumentList.Add("--verbose");
            else if (_settings.ReasoningEffort != "default")
            {
                start.ArgumentList.Add("--effort");
                start.ArgumentList.Add(_settings.ReasoningEffort);
            }
            start.Environment["CLAUDE_CONFIG_DIR"] = config;
            start.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
            start.Environment["DISABLE_TELEMETRY"] = "1";
            start.Environment["DISABLE_ERROR_REPORTING"] = "1";
            if (modelsOnly)
                start.Environment["CLAUDE_CODE_ENTRYPOINT"] = "sdk-ts";
            start.Environment.Remove("ANTHROPIC_LOG");
            start.Environment.Remove("DEBUG");
            // "default" means the model's own effort, not an inherited shell override.
            start.Environment.Remove("CLAUDE_CODE_EFFORT_LEVEL");
            foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL",
                "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY" })
                start.Environment.Remove(name);
            start.Environment["TMP"] = root;
            start.Environment["TEMP"] = root;
            start.Environment["TMPDIR"] = root;
            process.StartInfo = start;
            process.Start();
            using var cancellation = deadline.Token.Register(() => Kill(process));
            var stdout = modelsOnly ? ReadCatalogResponseAsync(process.StandardOutput, deadline.Token)
                : ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            try
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token)
                    .ConfigureAwait(false);
                if (modelsOnly)
                {
                    await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
                    await stdout.ConfigureAwait(false);
                    // No user message is ever sent. End the metadata-only session immediately.
                    Kill(process);
                }
                else
                    process.StandardInput.Close();
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr).ConfigureAwait(false);
            }
            catch
            {
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (!modelsOnly && process.ExitCode != 0)
            {
                var detail = stderr.Result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                    ?? "Check CLI login and selected model.";
                detail = new string(detail.Where(value => !char.IsControl(value)).Take(600).ToArray());
                throw new InvalidOperationException($"Claude CLI exited with code {process.ExitCode}. {detail}");
            }
            if (string.IsNullOrWhiteSpace(stdout.Result))
                throw new InvalidDataException("Claude CLI returned no model response.");
            return stdout.Result;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Claude CLI did not respond within 75 seconds.", error);
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException("Claude CLI could not be started. Check its executable path.", error);
        }
        finally
        {
            Kill(process);
            Directory.Delete(root, true);
        }
    }

    public async Task<ProviderProbe> ProbeAsync(CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            await CompleteAsync("Return only the JSON object {\"ok\":true}. Do not use tools.",
                "Connection test.", token).ConfigureAwait(false);
            return new ProviderProbe(true, Label + " responded.", [], clock.Elapsed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
            or NotSupportedException or TimeoutException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        {
            return new ProviderProbe(false, error.Message, [], clock.Elapsed);
        }
    }

    public async Task<ProviderProbe> ListModelsAsync(CancellationToken token)
    {
        if (_settings.Provider == AiProviderKind.CodexCli)
            return await CodexModelCatalog.ListAsync(_settings, token).ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        try
        {
            // Agent SDK supportedModels() reads initializationResult().models. Send only
            // that control request, never a prompt or slash command that could run inference.
            var input = JsonSerializer.Serialize(new
            {
                type = "control_request", request_id = "quadexplorer-models",
                request = new { subtype = "initialize", systemPrompt = new[] { "Metadata lookup only." } }
            }) + "\n";
            var response = await ExecuteAsync(input, true, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            var envelope = document.RootElement.GetProperty("response");
            if (envelope.GetProperty("subtype").GetString() != "success"
                || !envelope.TryGetProperty("response", out var payload)
                || !payload.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                throw new NotSupportedException("Claude CLI did not provide a supported model catalog. Use a documented alias or enter a model ID manually.");
            var ids = models.EnumerateArray().Take(100).Select(model =>
            {
                if (model.TryGetProperty("resolvedModel", out var resolved)
                    && resolved.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(resolved.GetString()))
                    return resolved.GetString();
                return model.GetProperty("value").GetString();
            }).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 200 && !id.Any(char.IsControl))
                .Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
            return new ProviderProbe(true, "Claude CLI reported its available model catalog (metadata only).", ids, clock.Elapsed)
            {
                ModelDescriptors = AiModelCatalog.Merge(_settings.Provider, ids.Select(id =>
                    new AiModelDescriptor(_settings.Provider, id, AiModelCatalog.DisplayName(_settings.Provider, id),
                        AiModelCatalog.GetReasoningEfforts(_settings.Provider, id), true)))
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
            or NotSupportedException or TimeoutException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            return new ProviderProbe(false, error.Message, [], clock.Elapsed)
            {
                ModelDescriptors = AiModelCatalog.Merge(_settings.Provider, [], error.Message)
            };
        }
    }

    private static async Task<string> ReadCatalogResponseAsync(StreamReader reader, CancellationToken token)
    {
        var line = new StringBuilder();
        // Stop at the frame's newline even while the CLI keeps its stream open.
        var buffer = new char[1];
        var total = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > 65536)
                throw new InvalidDataException("Claude CLI model catalog exceeds the response size limit.");
            foreach (var value in buffer.AsSpan(0, count))
            {
                if (value != '\n')
                {
                    line.Append(value);
                    continue;
                }
                var text = line.ToString();
                line.Clear();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "control_response"
                    && document.RootElement.TryGetProperty("response", out var response)
                    && response.TryGetProperty("request_id", out var id) && id.GetString() == "quadexplorer-models")
                    return text;
            }
        }
        throw new NotSupportedException("Claude CLI model discovery is unavailable. Update Claude Code, use a documented alias, or enter a model ID manually.");
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > 65536)
                throw new InvalidDataException("CLI output exceeds the response size limit.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
