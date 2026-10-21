namespace QuadExplorer.Search;

internal sealed class IndexBuilder
{
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    private readonly string[] _roots;
    private readonly string _storePath;
    private readonly Dictionary<string, IndexDirectory> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);

    internal IndexBuilder(string[] roots, string storePath)
    {
        _roots = roots;
        _storePath = storePath;
    }

    internal FileIndex Build(CancellationToken cancellationToken)
    {
        _directories.Clear();
        _denied.Clear();
        foreach (var root in _roots)
            ReadTree(root, cancellationToken);
        return Snapshot();
    }

    internal FileIndex Reconcile(IEnumerable<string> changedPaths, CancellationToken cancellationToken)
    {
        var paths = changedPaths.Where(path => !IsExcluded(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Length).ToArray();
        var refreshedTrees = new List<string>();
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsInScope(path) || HasReparseAncestor(path)
                || refreshedTrees.Any(tree => IsWithin(path, tree)))
                continue;
            if (_directories.ContainsKey(path) || IsTraversableDirectory(path))
            {
                RemoveTree(path);
                ReadTree(path, cancellationToken);
                refreshedTrees.Add(path);
            }
            var parent = Path.GetDirectoryName(path);
            if (parent is not null && IsInScope(parent))
                parents.Add(parent);
        }
        foreach (var parent in parents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTraversableDirectory(parent))
                ReadDirectory(parent, cancellationToken);
            else
                RemoveTree(parent);
        }
        return Snapshot();
    }

    internal bool IsExcluded(string path) =>
        path.Equals(_storePath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(_storePath + ".", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    private bool IsInScope(string path) => _roots.Any(root => IsWithin(path, root));

    private bool HasReparseAncestor(string path)
    {
        for (var parent = Path.GetDirectoryName(path); parent is not null && IsInScope(parent);
            parent = Path.GetDirectoryName(parent))
        {
            try
            {
                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
        }
        return false;
    }

    internal static bool IsWithin(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
        && (root.EndsWith('\\') || path.Length > root.Length && path[root.Length] == '\\');

    private static bool IsTraversableDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void RemoveTree(string path)
    {
        foreach (var key in _directories.Keys.Where(key => IsWithin(key, path)).ToArray())
            _directories.Remove(key);
        _denied.RemoveWhere(key => IsWithin(key, path));
    }

    private void ReadTree(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.Directory) == 0)
                    continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (_roots.Contains(directory, StringComparer.OrdinalIgnoreCase))
                        _directories[directory] = new IndexDirectory(directory, []);
                    continue;
                }
                foreach (var child in ReadDirectory(directory, cancellationToken))
                    pending.Push(child);
            }
            catch (UnauthorizedAccessException)
            {
                _denied.Add(directory);
                _directories[directory] = new IndexDirectory(directory, []);
            }
            catch (IOException)
            {
                // A directory removed during the walk is reconciled by its queued watcher event.
            }
        }
    }

    private List<string> ReadDirectory(string path, CancellationToken cancellationToken)
    {
        var entries = new List<IndexEntry>();
        var children = new List<string>();
        _denied.Remove(path);
        try
        {
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsExcluded(info.FullName))
                    continue;
                var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                entries.Add(new IndexEntry(info.Name, isDirectory));
                if (isDirectory && ((info.Attributes & FileAttributes.ReparsePoint) == 0 || info.LinkTarget is null))
                    children.Add(info.FullName);
            }
        }
        catch (UnauthorizedAccessException)
        {
            _denied.Add(path);
        }
        catch (DirectoryNotFoundException)
        {
            _directories.Remove(path);
            return [];
        }
        catch (IOException)
        {
            _denied.Add(path);
        }
        _directories[path] = new IndexDirectory(path, entries.ToArray());
        return children;
    }

    private FileIndex Snapshot() => new(_roots.Where(_directories.ContainsKey).ToArray(),
        _directories.Values.ToArray(), _denied.Order(StringComparer.OrdinalIgnoreCase).ToArray(), DateTime.UtcNow);
}
