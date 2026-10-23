namespace QuadExplorer.Search;

// Folders whose contents change constantly and are rarely looked up by name: Windows, ProgramData,
// each profile's AppData, the recycle bin and System Volume Information. The folder itself stays
// searchable; its contents are neither indexed nor watched. A tree that contains an index root
// (for example a test root under %TEMP%) never applies, so explicit roots are always indexed.
internal sealed class IndexScope
{
    internal static readonly IndexScope None = new([], null);
    private readonly string[] _skippedTrees;
    private readonly string? _profilesRoot;

    internal IndexScope(IEnumerable<string> skippedTrees, string? profilesRoot)
    {
        _skippedTrees = skippedTrees.Where(path => path.Length > 0)
            .Select(path => Path.TrimEndingDirectorySeparator(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _profilesRoot = string.IsNullOrEmpty(profilesRoot) ? null : Path.TrimEndingDirectorySeparator(profilesRoot);
    }

    internal static IndexScope ForSystem(IReadOnlyCollection<string> roots)
    {
        var trees = roots.SelectMany(root => new[]
            {
                Path.Combine(root, "$Recycle.Bin"), Path.Combine(root, "System Volume Information")
            })
            .Append(Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify))
            .Append(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        var scope = new IndexScope(trees, profile.Length > 0 ? Path.GetDirectoryName(profile) : null);
        return scope.Without(roots);
    }

    internal IndexScope Without(IReadOnlyCollection<string> roots)
    {
        var trees = _skippedTrees.Where(tree => !roots.Any(root => IndexBuilder.IsWithin(root, tree)));
        var profilesRoot = roots.Any(root => ProfileDepth(root) >= 2) ? null : _profilesRoot;
        return new IndexScope(trees, profilesRoot);
    }

    internal bool SkipsContentsOf(string directory)
    {
        var path = Path.TrimEndingDirectorySeparator(directory);
        foreach (var tree in _skippedTrees)
            if (path.Equals(tree, StringComparison.OrdinalIgnoreCase))
                return true;
        return ProfileDepth(path) == 2;
    }

    internal bool IsInsideSkipped(string path)
    {
        foreach (var tree in _skippedTrees)
            if (path.Length > tree.Length && IndexBuilder.IsWithin(path, tree))
                return true;
        return ProfileDepth(path) > 2;
    }

    // Segment count below the profiles folder when the path runs through <user>\AppData; 0 otherwise.
    private int ProfileDepth(string path)
    {
        if (_profilesRoot is null || path.Length <= _profilesRoot.Length + 1
            || !path.StartsWith(_profilesRoot, StringComparison.OrdinalIgnoreCase) || path[_profilesRoot.Length] != '\\')
            return 0;
        var segments = path[(_profilesRoot.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && segments[1].Equals("AppData", StringComparison.OrdinalIgnoreCase) ? segments.Length : 0;
    }
}
