using System.Diagnostics;

namespace QuadExplorer.Search;

public sealed record IndexStats(int Entries, int DeniedDirectories, DateTime BuiltUtc,
    bool SnapshotLoaded, int Overflows);

public sealed class IndependentSearchBackend : IPathSearchBackend
{
    private readonly object _gate = new();
    private readonly string[] _roots;
    private readonly string _storePath;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _initial = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<Task> _queries = [];
    private FileIndex _index = FileIndex.Empty;
    private Task? _worker;
    private CancellationTokenSource? _buildCancellation;
    private IndexWatcher? _watcher;
    private volatile bool _ready;
    private volatile bool _disposed;
    private volatile bool _snapshotLoaded;
    private int _overflows;
    private string? _lastError;

    public IndependentSearchBackend(IEnumerable<string> roots, string storePath)
    {
        var normalized = roots.Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(root => root.Length).ToArray();
        _roots = normalized.Where((root, index) =>
            !normalized.Take(index).Any(parent => IndexBuilder.IsWithin(root, parent))).ToArray();
        _storePath = Path.GetFullPath(storePath);
    }

    public static string DefaultStorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderSplitter", "Search", "paths.v1.index");

    public bool IsAvailable => !_disposed && _roots.Length > 0;
    public bool IsReady => !_disposed && _ready;
    public string? LastError => Volatile.Read(ref _lastError);
    public IReadOnlyList<string> DeniedPaths => Array.AsReadOnly(Volatile.Read(ref _index).DeniedPaths);
    public IndexStats Stats
    {
        get
        {
            var index = Volatile.Read(ref _index);
            return new IndexStats(index.Count, index.DeniedPaths.Length, index.BuiltUtc,
                _snapshotLoaded, Volatile.Read(ref _overflows));
        }
    }

    public event EventHandler? IndexChanged;
    public event EventHandler? RescanCompleted;

    public Task BuildOrLoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is null)
            {
                _buildCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
                _worker = Task.Run(() => RunAsync(_buildCancellation.Token));
            }
            return _initial.Task;
        }
    }

    public Task<IReadOnlyList<SearchHit>> QueryAsync(string text, IReadOnlyList<string> priorityRoots,
        int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var query = SearchQuery.Parse(text);
            var index = Volatile.Read(ref _index);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var task = Task.Run(() =>
            {
                using (linked)
                    return index.Search(query, priorityRoots, limit, true, linked.Token);
            });
            _queries.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_gate)
                    _queries.Remove(completed);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    public IReadOnlyList<SearchHit> QueryAll(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Volatile.Read(ref _index).Search(query, [], int.MaxValue, false, cancellationToken);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var builder = new IndexBuilder(_roots, _storePath);
            var watcher = new IndexWatcher(_roots, builder.IsExcluded);
            _watcher = watcher;
            var saved = IndexStore.Load(_storePath, _roots, token);
            if (saved is not null)
            {
                _snapshotLoaded = true;
                Volatile.Write(ref _index, saved);
                _ready = true;
                _initial.TrySetResult();
                // Deliver cached readiness before starting the expensive reconciliation.
                // This callback is not part of the worker that Dispose joins.
                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    Task startup;
                    lock (_gate)
                        startup = _worker!;
                    await startup.ConfigureAwait(false);
                    if (_disposed)
                        return;
                    try
                    {
                        IndexChanged?.Invoke(this, EventArgs.Empty);
                    }
                    finally
                    {
                        lock (_gate)
                            if (!_disposed)
                                _worker = Task.Run(() => ReconcileAsync(builder, watcher, token));
                    }
                });
                return;
            }
            await ReconcileAsync(builder, watcher, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _initial.TrySetCanceled(token);
            _watcher?.Dispose();
            _watcher = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Volatile.Write(ref _lastError, error.Message);
            _initial.TrySetException(error);
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private async Task ReconcileAsync(IndexBuilder builder, IndexWatcher watcher, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var index = builder.Build(token);
            var pending = watcher.Drain(out var invalidations);
            if (invalidations > 0)
            {
                Interlocked.Add(ref _overflows, invalidations);
                watcher.Restart();
                index = builder.Build(token);
            }
            else if (pending.Length > 0)
            {
                index = builder.Reconcile(pending, token);
            }
            IndexStore.Save(_storePath, _roots, index, token);
            _ready = true;
            Publish(index);
            Notify(RescanCompleted);
            _initial.TrySetResult();
            var lastSaved = Stopwatch.GetTimestamp();
            while (await watcher.WaitAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                pending = watcher.Drain(out invalidations);
                if (invalidations > 0)
                {
                    Interlocked.Add(ref _overflows, invalidations);
                    watcher.Restart();
                    index = builder.Build(token);
                }
                else
                {
                    index = builder.Reconcile(pending, token);
                }
                Publish(index);
                if (invalidations > 0)
                    Notify(RescanCompleted);
                // Startup always reconciles the snapshot. Bound write amplification on busy volumes.
                if (Stopwatch.GetElapsedTime(lastSaved) >= TimeSpan.FromSeconds(30))
                {
                    IndexStore.Save(_storePath, _roots, index, token);
                    lastSaved = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _initial.TrySetCanceled(token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Volatile.Write(ref _lastError, error.Message);
            _initial.TrySetException(error);
        }
        finally
        {
            watcher.Dispose();
            _watcher = null;
            _ready = false;
            Notify(IndexChanged);
        }
    }

    private void Publish(FileIndex index)
    {
        Volatile.Write(ref _index, index);
        Notify(IndexChanged);
    }

    private void Notify(EventHandler? handler)
    {
        if (handler is null || _disposed)
            return;
        // Subscriber callbacks never run on the worker being joined by Dispose.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (!_disposed)
                handler(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        Task? worker;
        Task[] queries;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();
            worker = _worker;
            queries = _queries.ToArray();
        }
        worker?.GetAwaiter().GetResult();
        try
        {
            Task.WhenAll(queries).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        _watcher?.Dispose();
        _watcher = null;
        _buildCancellation?.Dispose();
        _lifetime.Dispose();
    }
}
