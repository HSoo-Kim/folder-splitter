using System.Threading.Channels;

namespace QuadExplorer.Search;

internal sealed class IndexWatcher : IDisposable
{
    private const int Capacity = 65536;
    private readonly string[] _roots;
    private readonly Func<string, bool> _excluded;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Channel<string> _changes = Channel.CreateBounded<string>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private int _invalidations;

    internal IndexWatcher(string[] roots, Func<string, bool> excluded)
    {
        _roots = roots;
        _excluded = excluded;
        try
        {
            Restart();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Restart()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        foreach (var root in _roots)
        {
            FileSystemWatcher? watcher = null;
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    continue;
                watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 65536
                };
                watcher.Created += OnPath;
                watcher.Deleted += OnPath;
                watcher.Renamed += OnRename;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (UnauthorizedAccessException) { watcher?.Dispose(); }
            catch (DirectoryNotFoundException) { watcher?.Dispose(); }
            catch
            {
                watcher?.Dispose();
                throw;
            }
        }
    }

    internal ValueTask<bool> WaitAsync(CancellationToken cancellationToken) =>
        _changes.Reader.WaitToReadAsync(cancellationToken);

    internal string[] Drain(out int invalidations)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Capacity && _changes.Reader.TryRead(out var path); i++)
            if (path.Length != 0)
                paths.Add(path);
        invalidations = Interlocked.Exchange(ref _invalidations, 0);
        return paths.ToArray();
    }

    private void OnPath(object sender, FileSystemEventArgs args) => Enqueue(args.FullPath);

    private void OnRename(object sender, RenamedEventArgs args)
    {
        Enqueue(args.FullPath);
        Enqueue(args.OldFullPath);
    }

    private void Enqueue(string path)
    {
        if (_excluded(path))
            return;
        if (!_changes.Writer.TryWrite(path))
        {
            Interlocked.Increment(ref _invalidations);
            _changes.Writer.TryWrite(string.Empty);
        }
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        Interlocked.Increment(ref _invalidations);
        _changes.Writer.TryWrite(string.Empty);
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        _changes.Writer.TryComplete();
    }
}
