using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace QuadExplorer.Search;

/// <summary>Benchmark-only read-only Everything IPC client. Does not launch or configure Everything.</summary>
public sealed class EverythingIpcBackend : IPathSearchBackend
{
    // Source: official Everything-SDK.zip, ipc/everything_ipc.h (downloaded 2026-10-21).
    // QUERY2: seven packed DWORDs. LIST2: five DWORDs, then ITEM2 pairs of DWORDs.
    // FULL_PATH_AND_NAME data: DWORD character count, UTF-16 text, UTF-16 terminator.
    private const uint WmCopyData = 0x004a, WmUser = 0x0400, Wake = 0x8001, Stop = 0x8002;
    private const uint Query2W = 18, MatchPath = 4, FullPathAndName = 4, SortNameAscending = 1;
    private const uint IsDbLoaded = 401, PageSize = 8192, TimeoutMilliseconds = 15000;
    private readonly string _serverClass;
    private readonly string _searchScope;
    private readonly Func<SearchHit, bool>? _includeHit;
    private readonly string _clientClass = "QuadExplorer.Everything." + Guid.NewGuid().ToString("N");
    private readonly ConcurrentQueue<Request> _queue = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowProc _windowProc;
    private nint _window;
    private Request? _active;
    private uint _generation;
    private volatile bool _disposed;
    private int _available, _ready;
    private bool _wasReady;
    private bool _stateInitialized;

    public EverythingIpcBackend(string? instanceName = null, IReadOnlyList<string>? searchRoots = null,
        Func<SearchHit, bool>? includeHit = null)
    {
        _includeHit = includeHit;
        _searchScope = includeHit is null && searchRoots is { Count: > 0 }
            ? "<" + string.Join("|", searchRoots.Select(root => "regex:\"^" +
                Regex.Escape(root.Replace('/', '\\').TrimEnd('\\')) + @"(\\|$)" + "\"")) + "> "
            : "";
        // Official ES-1.1.0.38.src.zip, src/es.c: _es_get_window_classname.
        _serverClass = "EVERYTHING_TASKBAR_NOTIFICATION" +
            (string.IsNullOrEmpty(instanceName) ? "" : "_(" + instanceName + ")");
        _windowProc = WindowProcedure;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Everything IPC" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Task.GetAwaiter().GetResult();
    }

    // Address-bar keystrokes read these properties. Never perform IPC/service calls here.
    public bool IsAvailable => !_disposed && Volatile.Read(ref _available) != 0;
    public bool IsReady => !_disposed && Volatile.Read(ref _ready) != 0;

    // Everything 1.4 has no result-change subscription. Notify when a query observes readiness change.
    public event EventHandler? IndexChanged;

    public async Task<IReadOnlyList<SearchHit>> QueryAsync(string text, IReadOnlyList<string> priorityRoots,
        int limit, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (limit <= 0 || string.IsNullOrWhiteSpace(text)) return Array.Empty<SearchHit>();
        var query = SearchQuery.Parse(text);
        var expression = _searchScope + SearchExpression(query);
        var candidates = new Dictionary<string, SearchHit>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in priorityRoots.Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = root.Replace('/', '\\').TrimEnd('\\');
            if (Directory.Exists(root))
                Add([new SearchHit(Path.TrimEndingDirectorySeparator(root.Replace('/', '\\')), true)]);
            var scope = "path:\"" + (path + "\\").Replace("\"", "\"\"") + "\"";
            Add(await QueryAllAsync(scope + " " + expression, cancellationToken).ConfigureAwait(false));
            if (candidates.Count >= limit)
                return PathRanker.Rank(query, candidates.Values, priorityRoots, limit);
        }
        Add(await QueryAllAsync(expression, cancellationToken).ConfigureAwait(false));
        // Rank complete scopes before truncation, never an arbitrary global prefix.
        cancellationToken.ThrowIfCancellationRequested();
        return PathRanker.Rank(query, candidates.Values, priorityRoots, limit);

        void Add(IReadOnlyList<SearchHit> hits)
        {
            foreach (var hit in hits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (query.IsAnchor(hit.FullPath) && (_includeHit is null || _includeHit(hit)))
                    candidates.TryAdd(hit.FullPath, hit);
            }
        }
    }

    /// <summary>Returns every result of a native Everything search expression, without max-results truncation.</summary>
    public async Task<IReadOnlyList<SearchHit>> QueryAllAsync(string search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var hits = new List<SearchHit>();
        uint offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await Enqueue(search, offset, cancellationToken).ConfigureAwait(false);
            hits.AddRange(page.Hits);
            offset = checked(offset + (uint)page.Hits.Count);
            if (offset >= page.Total) return hits;
            if (page.Hits.Count == 0)
                throw new IOException("Everything returned an incomplete result page.");
        }
    }

    public static string LiteralSearch(string text) => "regex:" +
        "\"" + Regex.Escape(text.Replace('/', '\\')).Replace("\"", "\"\"") + "\"";

    public static string SearchExpression(SearchQuery query) => query.Terms.Length == 0
        ? "regex:\"(?!)\""
        : string.Join(" ", query.Terms.Select(term =>
            (term.Contains('\\') ? "path:" : "nopath:") +
            (term.Contains('*') || term.Contains('?') ? "regex:\"" + Regex.Escape(term).Replace("\"", "\"\"")
                : "\"" + term.Replace("\"", "\"\"")) + "\""));

    private void ObserveReady(bool ready)
    {
        Volatile.Write(ref _ready, ready ? 1 : 0);
        if (_wasReady == ready) return;
        _wasReady = ready;
        IndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<Page> Enqueue(string search, uint offset, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var request = new Request(search, offset, cancellationToken);
            request.Registration = cancellationToken.Register(() =>
            {
                request.Completion.TrySetCanceled(cancellationToken);
                PostMessageW(_window, Wake, 0, 0);
            });
            _queue.Enqueue(request);
            PostMessageW(_window, Wake, 0, 0);
            return request.Completion.Task;
        }
    }

    private void MessageLoop()
    {
        var module = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = module,
            Procedure = Marshal.GetFunctionPointerForDelegate(_windowProc), ClassName = _clientClass
        };
        try
        {
            if (RegisterClassExW(ref windowClass) == 0) throw new Win32Exception();
            _window = CreateWindowExW(0, _clientClass, "", 0, 0, 0, 0, 0, new nint(-3), 0, module, 0);
            if (_window == 0) throw new Win32Exception();
            _started.SetResult();
            PostMessageW(_window, Wake, 0, 0);
            int result;
            while ((result = GetMessageW(out var message, 0, 0, 0)) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
            if (result < 0) throw new Win32Exception();
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            Finish(null, exception);
        }
        finally
        {
            while (_queue.TryDequeue(out var pending))
            {
                pending.Registration.Dispose();
                pending.Completion.TrySetException(new ObjectDisposedException(nameof(EverythingIpcBackend)));
            }
            if (_window != 0) DestroyWindow(_window);
            _window = 0;
            UnregisterClassW(_clientClass, module);
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == Stop)
            {
                Finish(null, new ObjectDisposedException(nameof(EverythingIpcBackend)));
                PostQuitMessage(0);
                return 0;
            }
            if (message == Wake)
            {
                if (_active?.CancellationToken.IsCancellationRequested == true)
                    Finish(null, new OperationCanceledException(_active.CancellationToken));
                StartNext();
                return 0;
            }
            if (message == 0x0113 && wParam == _generation && _active != null)
            {
                Finish(null, new TimeoutException("Everything IPC result reply timed out."));
                StartNext();
                return 0;
            }
            if (message == WmCopyData && _active != null)
            {
                var copy = Marshal.PtrToStructure<CopyData>(lParam);
                if (copy.Tag != _generation) return 0; // A cancelled request may reply late.
                if (copy.Length < 20) throw new IOException("Invalid Everything LIST2 header.");
                var bytes = new byte[copy.Length];
                Marshal.Copy(copy.Data, bytes, 0, bytes.Length); // Sender owns memory until return.
                Finish(ParsePage(bytes), null);
                PostMessageW(window, Wake, 0, 0);
                return 1;
            }
        }
        catch (Exception exception)
        {
            Finish(null, exception);
            PostMessageW(window, Wake, 0, 0);
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void StartNext()
    {
        if (_active == null && _queue.IsEmpty && !_stateInitialized)
        {
            _stateInitialized = true;
            RefreshState();
        }
        while (_active == null && _queue.TryDequeue(out var request))
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Registration.Dispose();
                request.Completion.TrySetCanceled(request.CancellationToken);
                continue;
            }
            _active = request;
            try
            {
                var server = RefreshState();
                if (server == 0)
                {
                    Finish(new Page(0, Array.Empty<SearchHit>()), null);
                    continue;
                }
                _generation = _generation == uint.MaxValue ? 1 : _generation + 1;
                var text = Encoding.Unicode.GetBytes(request.Search + "\0");
                var bytes = new byte[28 + text.Length];
                uint[] header = [unchecked((uint)_window.ToInt64()), _generation, MatchPath,
                    request.Offset, PageSize, FullPathAndName, SortNameAscending];
                for (var i = 0; i < header.Length; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), header[i]);
                text.CopyTo(bytes, 28);
                var native = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, native, bytes.Length);
                    var copy = new CopyData { Tag = Query2W, Length = bytes.Length, Data = native };
                    if (SetTimer(_window, _generation, TimeoutMilliseconds, 0) == 0) throw new Win32Exception();
                    if (SendCopyDataTimeoutW(server, WmCopyData, (nuint)_window, ref copy, 2,
                        TimeoutMilliseconds, out var accepted) == 0)
                        throw new TimeoutException("Everything IPC query dispatch failed.", new Win32Exception());
                    if (accepted == 0) throw new IOException("Everything rejected QUERY2W.");
                }
                finally { Marshal.FreeHGlobal(native); }
            }
            catch (Exception exception) { Finish(null, exception); }
        }
    }

    private nint RefreshState()
    {
        var server = FindWindowW(_serverClass, null);
        var available = server != 0;
        Volatile.Write(ref _available, available ? 1 : 0);
        if (!available)
        {
            ObserveReady(false);
            return 0;
        }
        if (SendMessageTimeoutW(server, WmUser, IsDbLoaded, 0, 2, TimeoutMilliseconds, out var loaded) == 0)
        {
            ObserveReady(false);
            throw new TimeoutException("Everything IPC database-state request failed.", new Win32Exception());
        }
        ObserveReady(loaded != 0);
        return loaded == 0 ? 0 : server;
    }

    private void Finish(Page? page, Exception? exception)
    {
        var request = _active;
        if (request == null) return;
        _active = null;
        KillTimer(_window, _generation);
        request.Registration.Dispose();
        if (request.CancellationToken.IsCancellationRequested)
            request.Completion.TrySetCanceled(request.CancellationToken);
        else if (exception != null) request.Completion.TrySetException(exception);
        else request.Completion.TrySetResult(page!);
    }

    private static Page ParsePage(byte[] bytes)
    {
        uint Read(int offset)
        {
            if (offset < 0 || offset > bytes.Length - 4) throw new IOException("Invalid Everything LIST2 offset.");
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        }
        var total = Read(0);
        var count = checked((int)Read(4));
        if (Read(12) != FullPathAndName || count > (bytes.Length - 20) / 8)
            throw new IOException("Invalid Everything LIST2 item table or request flags.");
        var hits = new SearchHit[count];
        for (var i = 0; i < count; i++)
        {
            var flags = Read(20 + i * 8);
            var offset = checked((int)Read(24 + i * 8));
            var length = checked((int)Read(offset) * 2);
            if (offset < 20 + count * 8 || length > bytes.Length - offset - 6 ||
                bytes[offset + 4 + length] != 0 || bytes[offset + 5 + length] != 0)
                throw new IOException("Invalid Everything full-path string.");
            hits[i] = new SearchHit(Encoding.Unicode.GetString(bytes, offset + 4, length), (flags & 1) != 0);
        }
        return new Page(total, hits);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            PostMessageW(_window, Stop, 0, 0);
        }
        if (Thread.CurrentThread != _thread) _thread.Join();
    }

    private sealed record Page(uint Total, IReadOnlyList<SearchHit> Hits);
    private sealed record Request(string Search, uint Offset, CancellationToken CancellationToken)
    {
        public TaskCompletionSource<Page> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData { public nuint Tag; public int Length; public nint Data; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeoutW(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern nint SendCopyDataTimeoutW(nint window, uint message, nuint wParam, ref CopyData copy, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string className, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
