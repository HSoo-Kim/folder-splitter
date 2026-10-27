namespace QuadExplorer.AiSearch;

internal static class CliExecutableLocator
{
    public static string Resolve(AiSearchSettings settings)
    {
        var claude = settings.Provider == AiProviderKind.ClaudeCli;
        var selected = (claude ? settings.ClaudeCliPath : settings.CodexCliPath) ?? settings.CliPath;
        if (selected is not null)
        {
            if (Path.IsPathFullyQualified(selected) && File.Exists(selected))
                return selected;
            throw new FileNotFoundException($"{(claude ? "Claude" : "Codex")} CLI executable was not found. Set its full path in AI settings.");
        }
        var name = claude ? "claude.exe" : "codex.exe";
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(user, ".local", "bin", name),
            Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", name)
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries).Select(path => Path.Combine(path.Trim('"'), name)));
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"{(claude ? "Claude" : "Codex")} CLI executable was not found. Install and sign into the CLI, or set its full path in AI settings.");
    }
}
