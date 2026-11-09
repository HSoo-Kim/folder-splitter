using System.Text.Json;

namespace QuadExplorer;

internal static class UpdateSummary
{
    internal const int MaxCommentLength = 4096;
    internal const int MaxBulletLength = 120;
    private const string Marker = "<!-- foldersplitter-update-summary";

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        int start = body.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0 || body.IndexOf(Marker, start + Marker.Length, StringComparison.Ordinal) >= 0) return null;
        int jsonStart = start + Marker.Length;
        if (jsonStart < body.Length && body[jsonStart] == '\r') jsonStart++;
        if (jsonStart >= body.Length || body[jsonStart++] != '\n') return null;
        int searchLength = Math.Min(body.Length - jsonStart, MaxCommentLength - (jsonStart - start));
        int end = body.IndexOf("-->", jsonStart, searchLength, StringComparison.Ordinal);
        if (end < 0 || end == jsonStart || body[end - 1] != '\n' || end + 3 - start > MaxCommentLength) return null;

        try
        {
            using var json = JsonDocument.Parse(body.Substring(jsonStart, end - jsonStart));
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            var summaries = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var locale in json.RootElement.EnumerateObject())
            {
                if (locale.Name is not ("ko" or "en" or "zh-CN" or "ja")
                    || locale.Value.ValueKind != JsonValueKind.Array || summaries.ContainsKey(locale.Name)
                    || locale.Value.GetArrayLength() > 3) return null;
                var bullets = new List<string>();
                foreach (var item in locale.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) return null;
                    string? text = item.GetString();
                    if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBulletLength
                        || text.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029')) return null;
                    bullets.Add(text.Trim());
                }
                summaries.Add(locale.Name, bullets);
            }
            return summaries.Count == 0 ? null : summaries;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> ForLanguage(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? summaries, string language) =>
        language is "ko" or "en" or "zh-CN" or "ja"
        && summaries is not null && summaries.TryGetValue(language, out var bullets)
            ? bullets : Array.Empty<string>();
}
