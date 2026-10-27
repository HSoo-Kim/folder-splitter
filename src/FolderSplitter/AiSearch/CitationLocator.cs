using System.Text;

namespace QuadExplorer.AiSearch;

/// <summary>
/// Maps a model-supplied quotation back to the exact passages of the evidence it was copied from. Only formatting
/// differs between the two: whitespace and line breaks (including words split by extraction), table and list markup,
/// bullets, typographic quotes and dashes, letter case, full-width forms, decomposed Hangul and passages left out
/// with an ellipsis. The letters and digits themselves, and separators inside numbers (1,200,000 / 37.5 / 10-30),
/// must match, so a changed word or number never locates. The result always consists of verbatim, contiguous
/// substrings of the evidence.
/// </summary>
internal static class CitationLocator
{
    public const int MaxQuote = 600;
    public const int MaxSpan = 1200;
    public const int MaxSegments = 3;
    private const int MinSkeleton = 4;

    /// <returns>Verbatim evidence passages for the quotation, or null when it cannot be located.</returns>
    public static string[]? Locate(string evidence, string quote)
    {
        quote = quote.Trim();
        if (quote.Length is 0 or > MaxQuote) return null;
        if (evidence.Contains(quote, StringComparison.Ordinal)) return [quote];
        var segments = quote.Split(["...", "\u2026"], StringSplitOptions.None);
        return Match(evidence, segments, skeleton: false) ?? Match(evidence, segments, skeleton: true);
    }

    private static string[]? Match(string evidence, string[] rawSegments, bool skeleton)
    {
        var segments = rawSegments.Select(segment => Normalize(segment, skeleton).Text.Trim())
            .Where(segment => segment.Length > 0).ToArray();
        // A passage must be substantial enough to identify where it comes from; the skeleton ignores punctuation
        // and spacing, so it needs more letters and digits than the spacing-only comparison.
        int minimum = skeleton ? MinSkeleton : 3;
        if (segments.Length is 0 or > MaxSegments
            || (segments.Length > 1 || skeleton) && segments.Any(segment => segment.Count(char.IsLetterOrDigit) < minimum))
            return null;
        var (text, starts, ends) = Normalize(evidence, skeleton);
        var spans = new string[segments.Length];
        int from = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            int at = text.IndexOf(segments[i], from, StringComparison.Ordinal);
            if (at < 0) return null;
            spans[i] = evidence[starts[at]..ends[at + segments[i].Length - 1]];
            if (spans[i].Length > MaxSpan) return null;
            from = at + segments[i].Length;
        }
        return spans;
    }

    private static (string Text, int[] Starts, int[] Ends) Normalize(string value, bool skeleton)
    {
        var text = new StringBuilder(value.Length);
        var starts = new List<int>(value.Length);
        var ends = new List<int>(value.Length);
        void Add(char character, int start, int end)
        {
            text.Append(character);
            starts.Add(start);
            ends.Add(end);
        }
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c is '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF') continue;
            if (char.IsWhiteSpace(c))
            {
                if (skeleton) continue;
                if (text.Length > 0 && text[^1] == ' ') ends[^1] = i + 1;
                else Add(' ', i, i + 1);
                continue;
            }
            // Conjoining Hangul jamo (decomposed text from some documents) compose into the syllable a model writes.
            if (c is >= '\u1100' and <= '\u1112' && i + 1 < value.Length && value[i + 1] is >= '\u1161' and <= '\u1175')
            {
                int syllable = 0xAC00 + ((c - 0x1100) * 21 + value[i + 1] - 0x1161) * 28;
                int consumed = 2;
                if (i + 2 < value.Length && value[i + 2] is >= '\u11A8' and <= '\u11C2')
                {
                    syllable += value[i + 2] - 0x11A7;
                    consumed = 3;
                }
                Add((char)syllable, i, i + consumed);
                i += consumed - 1;
                continue;
            }
            char mapped = c switch
            {
                '\u2018' or '\u2019' or '\u201A' or '\u201B' or '\u2032' or '\u00B4' or '`' => '\'',
                '\u201C' or '\u201D' or '\u201E' or '\u201F' or '\u2033' or '\u00AB' or '\u00BB' => '"',
                >= '\u2010' and <= '\u2015' or '\u2212' or '\uFE58' or '\uFE63' => '-',
                >= '\uFF01' and <= '\uFF5E' => (char)(c - 0xFEE0),
                _ => c
            };
            // The Hangul middle dot (ㆍ) is classed as a letter but is used as a separator, like the punctuation dot.
            if (skeleton && mapped is '\u318D' or '\u119E') continue;
            if (skeleton && !char.IsLetterOrDigit(mapped))
            {
                // Only the separators inside a number or date carry meaning (1,200,000, 37.5, 10-30, 12:30).
                bool insideNumber = mapped is '.' or ',' or '-' or '/' or ':'
                    && i > 0 && i + 1 < value.Length && char.IsDigit(value[i - 1]) && char.IsDigit(value[i + 1]);
                if (!insideNumber) continue;
            }
            Add(char.ToLowerInvariant(mapped), i, i + 1);
        }
        return (text.ToString(), starts.ToArray(), ends.ToArray());
    }
}
