using System.Diagnostics;
using System.Text.Json;

namespace QuadExplorer.AiSearch;

public enum ProviderAuthentication { Unknown, Authenticated, Unauthenticated }

/// <summary>Read-only status checks and explicitly requested, provider-owned interactive login.</summary>
public static class ProviderAccess
{
    public static ProviderAuthentication ClassifyCodex(ProviderProbe result)
    {
        if (result.Ok) return ProviderAuthentication.Authenticated;
        return result.Detail is "Sign into ChatGPT in Codex first. No API key is required."
            or "A ChatGPT subscription login is required; API-key credentials are not used."
            or "ChatGPT login expired. Refresh your login in Codex and try again."
            ? ProviderAuthentication.Unauthenticated : ProviderAuthentication.Unknown;
    }

    public static ProviderAuthentication ParseClaudeStatus(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("loggedIn", out var loggedIn)
                ? loggedIn.ValueKind switch
                {
                    JsonValueKind.True => ProviderAuthentication.Authenticated,
                    JsonValueKind.False => ProviderAuthentication.Unauthenticated,
                    _ => ProviderAuthentication.Unknown
                } : ProviderAuthentication.Unknown;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return ProviderAuthentication.Unknown; }
    }

    public static async Task<ProviderAuthentication> GetStatusAsync(AiSearchSettings settings, ProviderProbe probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (settings.Provider == AiProviderKind.LocalEndpoint) return ProviderAuthentication.Unknown;
        if (settings.Provider == AiProviderKind.CodexCli) return ClassifyCodex(probe);
        if (probe.Ok) return ProviderAuthentication.Authenticated;
        try
        {
            var start = new ProcessStartInfo(CliExecutableLocator.Resolve(settings))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            start.ArgumentList.Add("auth");
            start.ArgumentList.Add("status");
            start.ArgumentList.Add("--json");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Claude CLI.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
                var errors = process.StandardError.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                var json = await output.ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                return json.Length <= 65536 ? ParseClaudeStatus(json) : ProviderAuthentication.Unknown;
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                token.ThrowIfCancellationRequested();
                return ProviderAuthentication.Unknown;
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return ProviderAuthentication.Unknown; }
    }

    public static ProcessStartInfo CreateLoginStartInfo(AiProviderKind provider, string executable)
    {
        if (provider is not (AiProviderKind.ClaudeCli or AiProviderKind.CodexCli))
            throw new ArgumentException("Local servers do not require login.", nameof(provider));
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("The CLI path must be absolute.", nameof(executable));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = false
        };
        if (provider == AiProviderKind.ClaudeCli) start.ArgumentList.Add("auth");
        start.ArgumentList.Add("login");
        return start;
    }

    public static string ResolveLoginExecutable(AiSearchSettings settings) => CliExecutableLocator.Resolve(settings);

    public static async Task LoginAsync(AiSearchSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var executable = ResolveLoginExecutable(settings);
        using var process = Process.Start(CreateLoginStartInfo(settings.Provider, executable))
            ?? throw new InvalidOperationException("Could not start the provider login.");
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"Login exited with code {process.ExitCode}; try again in the provider CLI.");
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }
}
