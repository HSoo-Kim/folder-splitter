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
    private readonly HashSet<string> _transient = new(StringComparer.OrdinalIgnoreCase);
    private readonly IndexScope _scope;
    private readonly System.Text.StringBuilder _packed = new();

    internal IndexBuilder(string[] roots, string storePath, IndexScope? scope = null, FileIndex? snapshot = null)
    {
        _roots = roots;
        _storePath = storePath;
        _scope = (scope ?? IndexScope.ForSystem(roots)).Without(roots);
        if (snapshot is not null)
            foreach (var directory in snapshot.Directories)
                _directories[directory.Path] = directory;
    }

    internal FileIndex Build(CancellationToken cancellationToken)
    {
        var previous = _directories.Values.ToArray();
        _directories.Clear();
        _denied.Clear();
        _transient.Clear();
        foreach (var root in _roots)
            ReadTree(root, cancellationToken);
        RestoreTransient(previous);
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
                // A batch can hold a renamed folder under its old spelling too; read it as it is on disk.
                var current = OnDiskName(path);
                var previous = _directories.Values.Where(directory => IsWithin(directory.Path, current)).ToArray();
                RemoveTree(current);
                ReadTree(current, cancellationToken);
                RestoreTransient(previous);
                refreshedTrees.Add(current);
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
            else if (!_denied.Contains(parent))
                RemoveTree(parent);
        }
        return Snapshot();
    }

    internal bool IsExcluded(string path) =>
        _scope.IsInsideSkipped(path)
        || path.Equals(_storePath, StringComparison.OrdinalIgnoreCase)
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

    private static string OnDiskName(string path)
    {
        var parent = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (parent is null || name.Length == 0)
            return path;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent, name))
                return Path.Combine(parent, Path.GetFileName(entry));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return path;
    }

    private bool IsTraversableDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            _denied.Remove(path);
            _transient.Remove(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;
        }
        catch (FileNotFoundException) { _denied.Remove(path); _transient.Remove(path); return false; }
        catch (DirectoryNotFoundException) { _denied.Remove(path); _transient.Remove(path); return false; }
        catch (IOException) { _denied.Add(path); _transient.Add(path); return false; }
        catch (UnauthorizedAccessException) { RemoveTree(path); _denied.Add(path); return false; }
    }

    private void RemoveTree(string path)
    {
        foreach (var key in _directories.Keys.Where(key => IsWithin(key, path)).ToArray())
            _directories.Remove(key);
        _denied.RemoveWhere(key => IsWithin(key, path));
        _transient.RemoveWhere(key => IsWithin(key, path));
    }

    private void RestoreTransient(IEnumerable<IndexDirectory> previous)
    {
        if (_transient.Count == 0)
            return;
        foreach (var directory in previous)
        {
            for (string? path = directory.Path; path is not null && IsInScope(path); path = Path.GetDirectoryName(path))
            {
                if (_transient.Contains(path))
                {
                    _directories[directory.Path] = directory;
                    break;
                }
            }
        }
    }

    private void ReadTree(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_scope.SkipsContentsOf(directory) && !_roots.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                _directories.Remove(directory);
                continue;
            }
            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.Directory) == 0)
                    continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (_roots.Contains(directory, StringComparer.OrdinalIgnoreCase))
                        _directories[directory] = new IndexDirectory(directory, "", 0);
                    continue;
                }
                foreach (var child in ReadDirectory(directory, cancellationToken, hasIndexedDescendants: false))
                    pending.Push(child);
            }
            catch (UnauthorizedAccessException)
            {
                // ReadTree starts with an empty subtree; there is nothing to remove here.
                _denied.Add(directory);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException)
            {
                _denied.Add(directory);
                _transient.Add(directory);
            }
        }
    }

    private List<string> ReadDirectory(string path, CancellationToken cancellationToken, bool hasIndexedDescendants = true)
    {
        _packed.Clear();
        var count = 0;
        var children = new List<string>();
        _denied.Remove(path);
        _transient.Remove(path);
        try
        {
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsExcluded(info.FullName))
                    continue;
                var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                IndexDirectory.Append(_packed, info.Name, isDirectory);
                count++;
                if (isDirectory && ((info.Attributes & FileAttributes.ReparsePoint) == 0 || info.LinkTarget is null)
                    && !_scope.SkipsContentsOf(info.FullName))
                    children.Add(info.FullName);
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (hasIndexedDescendants) RemoveTree(path);
            _denied.Add(path);
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            if (hasIndexedDescendants) RemoveTree(path);
            return [];
        }
        catch (IOException)
        {
            _denied.Add(path);
            _transient.Add(path);
            return [];
        }
        _directories[path] = new IndexDirectory(path, _packed.ToString(), count);
        return children;
    }

    private FileIndex Snapshot() => new(_roots.Where(root => _directories.ContainsKey(root) || _denied.Contains(root)).ToArray(),
        _directories.Values.ToArray(), _denied.Order(StringComparer.OrdinalIgnoreCase).ToArray(), DateTime.UtcNow);
}
