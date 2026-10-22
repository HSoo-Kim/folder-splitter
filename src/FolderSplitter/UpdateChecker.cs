using System.Net.Http;
using System.Text.Json;

namespace QuadExplorer;

public sealed record ReleaseInfo(Version Version, string Tag, string Url);

internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/HSoo-Kim/folder-splitter/releases/latest";

    public static Version Current => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    // Reads only the latest release's tag and page URL; nothing about the user or their files is sent.
    public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FolderSplitter/" + Current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        await using var stream = await http.GetStreamAsync(LatestReleaseApi, token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = json.RootElement.GetProperty("html_url").GetString() ?? "";
        return Version.TryParse(tag.TrimStart('v', 'V'), out var version) && url.StartsWith("https://github.com/", StringComparison.Ordinal)
            ? new ReleaseInfo(Normalize(version), tag, url) : null;
    }
}
