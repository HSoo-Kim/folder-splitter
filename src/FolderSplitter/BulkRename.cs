using System.Text.RegularExpressions;

namespace QuadExplorer;

// New names for several items: find/replace on the name (the extension is kept), then a pattern where
// {name} is the result and {n} a running number. Problems are reported per item instead of guessed around.
internal static class BulkRename
{
    internal sealed record Plan(string Path, string OldName, string NewName, string? Problem);

    internal static IReadOnlyList<Plan> Build(IReadOnlyList<string> paths, string find, string replace, bool regex, bool matchCase,
        string pattern, int start, int digits, Func<string, bool>? exists = null)
    {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        Regex? expression = null;
        string? patternError = null;
        if (find.Length > 0 && regex)
        {
            try { expression = new Regex(find, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
            catch (ArgumentException error) { patternError = error.Message; }
        }
        var plans = new List<Plan>();
        var invalid = Path.GetInvalidFileNameChars();
        int number = start;
        foreach (string path in paths)
        {
            string trimmed = path.TrimEnd('\\');
            string oldName = Path.GetFileName(trimmed);
            bool folder = Directory.Exists(trimmed);
            string extension = folder ? "" : Path.GetExtension(oldName);
            string stem = oldName[..^extension.Length];
            string replaced = find.Length == 0 || patternError is not null ? stem
                : expression is not null ? expression.Replace(stem, replace)
                : stem.Replace(find, replace, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            string formatted = (string.IsNullOrEmpty(pattern) ? "{name}" : pattern)
                .Replace("{name}", replaced).Replace("{n}", number.ToString().PadLeft(Math.Clamp(digits, 1, 9), '0'));
            number++;
            string newName = (formatted + extension).Trim();
            string? problem = patternError is not null ? "pattern"
                : newName.Length == 0 || newName.Trim('.').Length == 0 ? "empty"
                : newName.IndexOfAny(invalid) >= 0 ? "invalid" : null;
            plans.Add(new Plan(trimmed, oldName, newName, problem));
        }
        // Two items may not end with one name, and a new name may not take an existing item's name.
        var taken = plans.Where(plan => plan.Problem is null)
            .GroupBy(plan => Path.Combine(Path.GetDirectoryName(plan.Path)!, plan.NewName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(plans.Select(plan => plan.Path), StringComparer.OrdinalIgnoreCase);
        return plans.Select(plan =>
        {
            if (plan.Problem is not null || plan.NewName == plan.OldName) return plan;
            string target = Path.Combine(Path.GetDirectoryName(plan.Path)!, plan.NewName);
            if (taken[target] > 1) return plan with { Problem = "duplicate" };
            bool sameItem = string.Equals(target, plan.Path, StringComparison.OrdinalIgnoreCase);
            if (!sameItem && (current.Contains(target) || exists(target))) return plan with { Problem = "exists" };
            return plan;
        }).ToList();
    }
}
