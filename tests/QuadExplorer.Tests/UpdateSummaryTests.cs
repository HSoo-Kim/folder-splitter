using System.Text.Json;
using QuadExplorer;

internal static class UpdateSummaryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("release body supplies authored summaries for each selected language", AuthoredSummaries),
        ("missing and malformed summary metadata never hides the release", InvalidSummaries),
        ("summary size and bullet count limits are enforced", Bounds),
        ("release parser retains package and digest with and without summaries", ReleaseCompatibility)
    ];

    private static string Comment(string json) => "<!-- foldersplitter-update-summary\n" + json + "\n-->";

    private static void AuthoredSummaries()
    {
        string body = "# Folder Splitter 0.8.23\n\nRelease notes in several languages.\n\n" + Comment("""
            {
              "ko": ["여러 창에서 탭 이동", "폴더 선택 개선"],
              "en": ["Move tabs between windows", "A & B: don't lose selections."],
              "zh-CN": ["跨窗口移动标签页"],
              "ja": ["ウィンドウ間でタブを移動", "絵文字 📁 を表示"]
            }
            """) + "\n\nMore release notes.";
        var release = ParseRelease(body);
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "ko"), "여러 창에서 탭 이동", "폴더 선택 개선");
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "en"), "Move tabs between windows", "A & B: don't lose selections.");
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "zh-CN"), "跨窗口移动标签页");
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "ja"), "ウィンドウ間でタブを移動", "絵文字 📁 を表示");
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "fr"));
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "en-US"));
        Sequence(UpdateSummary.ForLanguage(null, "ko"));

        var partial = UpdateSummary.Parse(Comment("{\"en\":[\"English only\"],\"ja\":[]}"));
        Sequence(UpdateSummary.ForLanguage(partial, "en"), "English only");
        Sequence(UpdateSummary.ForLanguage(partial, "ko"));
        Sequence(UpdateSummary.ForLanguage(partial, "ja"));
        var windowsLines = body.Replace("\n", "\r\n");
        Sequence(UpdateSummary.ForLanguage(UpdateSummary.Parse(windowsLines), "zh-CN"), "跨窗口移动标签页");
    }

    private static void InvalidSummaries()
    {
        string[] invalid =
        [
            "", "ordinary release notes", "<!-- foldersplitter-update-summary -->",
            "<!-- foldersplitter-update-summary\n{\"en\":[\"missing end\"]}",
            "<!-- foldersplitter-update-summary {\"en\":[\"not on a new line\"]}\n-->",
            "<!-- foldersplitter-update-summary\n{\"en\":[\"no closing newline\"]}-->",
            Comment("{broken json}"), Comment("null"), Comment("[]"), Comment("{}"),
            Comment("{\"en\":\"one\"}"), Comment("{\"en\":[1]}"),
            Comment("{\"en\":[null]}"), Comment("{\"en\":[{}]}"),
            Comment("{\"en\":[\"\"]}"), Comment("{\"en\":[\"  \"]}"),
            Comment("{\"en\":[\"line\\nbreak\"]}"), Comment("{\"en\":[\"tab\\there\"]}"),
            Comment("{\"en\":[\"bad\\u0000value\"]}"), Comment("{\"en\":[\"line\\u2028break\"]}"),
            Comment("{\"en\":[\"bad\\ud800\"]}"),
            Comment("{\"fr\":[\"unknown language\"]}"),
            Comment("{\"en\":[\"first\"],\"en\":[\"second\"]}"),
            Comment("{\"en\":[\"one\"]}") + Comment("{\"en\":[\"two\"]}")
        ];
        foreach (string body in invalid)
        {
            if (UpdateSummary.Parse(body) is not null) throw new InvalidOperationException("Accepted invalid summary: " + body);
            AssertRelease(ParseRelease(body));
            Sequence(UpdateSummary.ForLanguage(ParseRelease(body).Summaries, "en"));
        }
        if (UpdateSummary.Parse(null) is not null) throw new InvalidOperationException("Accepted null body");
        foreach (string value in new[] { "null", "42", "[]", "{}", "true", "\"\\ud800\"" })
        {
            using var json = JsonDocument.Parse("{\"tag_name\":\"v0.8.23\",\"html_url\":\"https://github.com/a/b/releases/tag/v0.8.23\",\"body\":" + value + "}");
            var release = UpdateChecker.Parse(json.RootElement) ?? throw new InvalidOperationException("Release lost for body " + value);
            Sequence(UpdateSummary.ForLanguage(release.Summaries, "en"));
        }
    }

    private static void Bounds()
    {
        string longest = new('x', UpdateSummary.MaxBulletLength);
        Sequence(UpdateSummary.ForLanguage(UpdateSummary.Parse(Comment(JsonSerializer.Serialize(new { en = new[] { longest, "second", "third" } }))), "en"),
            longest, "second", "third");
        if (UpdateSummary.Parse(Comment(JsonSerializer.Serialize(new { en = new[] { longest + "x" } }))) is not null)
            throw new InvalidOperationException("Accepted oversized bullet");
        if (UpdateSummary.Parse(Comment("{\"en\":[\"a\",\"b\",\"c\",\"d\"]}")) is not null)
            throw new InvalidOperationException("Accepted four bullets");
        string json = "{\"en\":[\"ok\"]}";
        int padding = UpdateSummary.MaxCommentLength - Comment(json).Length;
        string atLimit = Comment(json + new string(' ', padding));
        Sequence(UpdateSummary.ForLanguage(UpdateSummary.Parse(atLimit), "en"), "ok");
        if (UpdateSummary.Parse(Comment(json + new string(' ', padding + 1))) is not null)
            throw new InvalidOperationException("Accepted oversized comment");
    }

    private static void ReleaseCompatibility()
    {
        AssertRelease(ParseRelease(Comment("{\"en\":[\"One\"]}")));
        AssertRelease(ParseRelease(null));
        using var json = JsonDocument.Parse("{\"tag_name\":\"v0.8.23\",\"html_url\":\"https://github.com/a/b/releases/tag/v0.8.23\"}");
        var release = UpdateChecker.Parse(json.RootElement) ?? throw new InvalidOperationException("Release without body was lost");
        Sequence(UpdateSummary.ForLanguage(release.Summaries, "en"));
        var oldCaller = new ReleaseInfo(new Version(0, 8, 23), "v0.8.23", "https://github.com/a/b");
        Sequence(UpdateSummary.ForLanguage(oldCaller.Summaries, "en"));
    }

    private static ReleaseInfo ParseRelease(string? body)
    {
        string data = JsonSerializer.Serialize(new
        {
            tag_name = "v0.8.23",
            html_url = "https://github.com/a/b/releases/tag/v0.8.23",
            body,
            assets = new[] { new { name = UpdateChecker.PackageName, browser_download_url = "https://github.com/a/b/package.zip", digest = "sha256:" + new string('a', 64) } }
        });
        using var json = JsonDocument.Parse(data);
        return UpdateChecker.Parse(json.RootElement) ?? throw new InvalidOperationException("Release was lost");
    }

    private static void AssertRelease(ReleaseInfo release)
    {
        if (release.Version != new Version(0, 8, 23) || release.Tag != "v0.8.23"
            || release.Url != "https://github.com/a/b/releases/tag/v0.8.23"
            || release.PackageUrl?.ToString() != "https://github.com/a/b/package.zip"
            || release.PackageSha256 != new string('a', 64))
            throw new InvalidOperationException("Release fields changed");
    }

    private static void Sequence(IReadOnlyList<string> actual, params string[] expected)
    {
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }
}
