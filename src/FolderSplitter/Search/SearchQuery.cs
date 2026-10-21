using System.Text;

namespace QuadExplorer.Search;

public sealed class SearchQuery
{
    internal readonly string[] Terms;
    internal readonly string[] NameTerms;
    internal readonly string[] PathTerms;
    public string Normalized { get; }

    private SearchQuery(string normalized, string[] terms)
    {
        Normalized = normalized;
        Terms = terms;
        NameTerms = terms.Where(term => !term.Contains('\\')).ToArray();
        PathTerms = terms.Where(term => term.Contains('\\')).ToArray();
    }

    public static SearchQuery Parse(string text)
    {
        var terms = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        foreach (var character in text.Trim())
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0)
                {
                    terms.Add(token.ToString());
                    token.Clear();
                }
                continue;
            }
            var value = character == '/' ? '\\' : character;
            if (value != '\\' || token.Length == 0 || token[^1] != '\\')
                token.Append(value);
        }
        if (token.Length > 0)
            terms.Add(token.ToString());
        return new SearchQuery(string.Join(" ", terms.Select(term =>
            term.Any(char.IsWhiteSpace) ? $"\"{term}\"" : term)), terms.ToArray());
    }

    public static bool IsRootedInput(string text)
    {
        var value = text.Trim().Trim('"').AsSpan();
        return value.Length > 0 && (value[0] is '\\' or '/' or '%' or '~'
            || value.StartsWith("::", StringComparison.Ordinal)
            || value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':');
    }

    public bool Matches(string fullPath) => Matches(NormalizePath(fullPath).AsSpan());

    public bool IsAnchor(string fullPath) => IsAnchor(NormalizePath(fullPath).AsSpan());

    internal bool Matches(ReadOnlySpan<char> fullPath)
    {
        if (Terms.Length == 0)
            return false;
        var leaf = fullPath[(fullPath.LastIndexOf('\\') + 1)..];
        foreach (var term in NameTerms)
            if (!leaf.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        foreach (var term in PathTerms)
            if (!fullPath.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    internal bool MatchesName(ReadOnlySpan<char> name, bool anchorsOnly)
    {
        foreach (var term in NameTerms)
            if (!name.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        if (!anchorsOnly || NameTerms.Length != 0 || PathTerms.Length == 0)
            return true;
        foreach (var term in PathTerms)
        {
            var tail = term.AsSpan(term.LastIndexOf('\\') + 1);
            if (tail.Length > 0 && name.Contains(tail, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal bool IsAnchor(ReadOnlySpan<char> fullPath)
    {
        if (!Matches(fullPath))
            return false;
        if (NameTerms.Length > 0)
            return true;
        var leafStart = fullPath.LastIndexOf('\\') + 1;
        foreach (var term in PathTerms)
        {
            var offset = Math.Max(0, leafStart - term.Length + 1);
            if (fullPath[offset..].Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal int AncestorPenalty(ReadOnlySpan<char> fullPath)
    {
        var penalty = 0;
        foreach (var term in PathTerms)
        {
            var rest = fullPath;
            var consumed = 0;
            var exact = false;
            while (true)
            {
                var at = rest.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    break;
                var absolute = consumed + at;
                if (absolute == 0 || fullPath[absolute - 1] == '\\' || term[0] == '\\')
                {
                    exact = true;
                    break;
                }
                consumed = absolute + 1;
                rest = fullPath[consumed..];
            }
            if (!exact)
                penalty++;
        }
        return penalty;
    }

    internal static string NormalizePath(string path) => path.Replace('/', '\\').TrimEnd('\\');
}
