using System.Runtime.InteropServices;

namespace QuadExplorer;

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ShellViewHost : UserControl, IExplorerBrowserEvents
{
    private IExplorerBrowser? browser;
    private IShellView? view;
    private uint cookie;
    private bool initialized;
    private readonly Label error;
    private readonly UiText text;
    private Func<string>? errorMessage;
    private string requestedLocation;
    private string? pendingSelection;
    private object? enumerationSource;
    private readonly Action enumerationDone;
    private bool enumerationReady;
    private bool navigationReady;
    private readonly System.Windows.Forms.Timer itemCountTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer selectionTimer = new() { Interval = 60 };
    private string? settlingSelection;
    private int settleTicks;
    private int heldTicks;
    internal event EventHandler? SelectionSettled;
    private static readonly Guid FolderViewEvents = new("62112AA2-EBE4-11CF-A5FB-0020AFE7292D");

    public string LocationPath { get; private set; }
    public string DisplayName { get; private set; }
    public bool HasNavigated { get; private set; }
    public int? ItemCount { get; private set; }
    internal string? ErrorStatus => error.Visible && errorMessage is not null ? errorMessage().Replace("\n", " ") : null;
    public event EventHandler? Navigated;
    public event EventHandler<string>? NavigationError;
    public event EventHandler? ItemCountChanged;

    public ShellViewHost(string location, UiText? text = null)
    {
        enumerationDone = OnEnumerationDone;
        itemCountTimer.Tick += OnItemCountTick;
        selectionTimer.Tick += OnSelectionTick;
        this.text = text ?? new UiText();
        LocationPath = requestedLocation = location;
        DisplayName = TitleFor(location, this.text);
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        TabStop = true;
        error = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(150, 45, 35), BackColor = Color.White,
            Padding = new Padding(24), Visible = false
        };
        Controls.Add(error);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Shell views register their drag/drop targets during initialization.
        Application.OleRequired();
        try
        {
            var type = Type.GetTypeFromCLSID(ShellNative.BrowserClass, throwOnError: true)!;
            browser = (IExplorerBrowser)Activator.CreateInstance(type)!;
            browser.SetOptions(0x40); // EBO_NOBORDER; preserve native travel history.
            var rect = new NativeRect(ClientSize.Width, ClientSize.Height);
            var settings = new FolderSettings { ViewMode = 4, Flags = 0x02000000 }; // Details; full row select.
            browser.Initialize(Handle, ref rect, ref settings);
            // Like File Explorer, remember each folder's grouping, sort and view mode under this app's name.
            browser.SetPropertyBag("FolderSplitter");
            initialized = true;
            browser.Advise(this, out cookie);
            BrowseToFolder(requestedLocation);
        }
        catch (COMException ex) { ReportError(ex.Message); }
    }

    public void Navigate(string location)
    {
        pendingSelection = null;
        BrowseToFolder(location);
    }

    public void NavigateAndSelect(string filePath)
    {
        if (!File.Exists(filePath))
        {
            ReportError(() => text.T("Could not open:\n{0}\n\n{1}", filePath,
                new FileNotFoundException(null, filePath).Message));
            return;
        }
        pendingSelection = Path.GetFullPath(filePath);
        BrowseToFolder(Path.GetDirectoryName(pendingSelection)!);
    }

    private void BrowseToFolder(string location)
    {
        location = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
        if (location.Length == 0) return;
        if (location == "~") location = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        requestedLocation = location;
        if (browser is null) { LocationPath = location; DisplayName = TitleFor(location, text); return; }
        nint pidl = 0;
        try
        {
            Marshal.ThrowExceptionForHR(ShellNative.SHParseDisplayName(location, 0, out pidl, 0x20000000, out uint attributes));
            if ((attributes & 0x20000000) == 0)
            {
                ReportError(() => text.T("Could not open:\n{0}\n\n{1}", location, text.T("Enter a folder path, not a file.")));
                return;
            }
            browser.BrowseToIDList(pidl, 0);
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException)
        {
            ReportError(() => text.T("Could not open:\n{0}\n\n{1}", location, ex.Message));
        }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }

    public void Travel(uint direction)
    {
        if (browser is null) return;
        try { browser.BrowseToIDList(0, direction); }
        catch (COMException ex) { NavigationError?.Invoke(this, ex.Message); }
    }

    public void RefreshFolder()
    {
        view?.Refresh();
        RefreshItemCount();
    }

    public void RefreshItemCount()
    {
        if (IsDisposed || Disposing || !initialized || !navigationReady || !enumerationReady ||
            view is not IFolderView folderView)
        {
            SetItemCount(null);
            return;
        }

        int result = folderView.ItemCount(ShellNative.SvgioAllView, out int count);
        if (result == unchecked((int)0x8000000A) || result == unchecked((int)0x80070015))
        {
            // E_PENDING or HRESULT_FROM_WIN32(ERROR_NOT_READY): enumeration is not ready.
            SetItemCount(null);
            return;
        }
        Marshal.ThrowExceptionForHR(result);
        if (result != 0)
            throw new InvalidOperationException($"The native folder view returned unexpected item-count status 0x{result:X8}.");
        if (count < 0) throw new InvalidOperationException("The native folder view returned a negative item count.");
        SetItemCount(count);
    }

    // VPCF_TEXT, BACKGROUND, SORTCOLUMN, SUBTEXT, TEXTBACKGROUND: list colors the folder view keeps
    // through its own redraws, unlike colors sent directly to its list window.
    internal void ApplyThemeColors()
    {
        if (view is not IVisualProperties visual) return;
        visual.SetColor(1, ColorTranslator.ToWin32(Theme.Text));
        visual.SetColor(2, ColorTranslator.ToWin32(Theme.Surface));
        visual.SetColor(3, ColorTranslator.ToWin32(Theme.Canvas));
        visual.SetColor(4, ColorTranslator.ToWin32(Theme.Muted));
        visual.SetColor(5, ColorTranslator.ToWin32(Theme.Surface));
    }

    private void SetItemCount(int? count)
    {
        if (ItemCount == count) return;
        ItemCount = count;
        ItemCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemCountTick(object? sender, EventArgs e) => RefreshItemCount();

    private void UpdateItemCountMonitoring()
    {
        // Query the view's existing item list on its STA; no filesystem enumeration or worker thread.
        itemCountTimer.Enabled = Visible && IsHandleCreated && initialized && navigationReady &&
            view is not null && !IsDisposed && !Disposing;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateItemCountMonitoring();
        if (Visible) RefreshItemCount();
    }

    public void SetViewMode(uint mode)
    {
        var settings = new FolderSettings { ViewMode = mode, Flags = 0x02000000 };
        browser?.SetFolderSettings(ref settings);
    }

    public void FocusView()
    {
        if (view is not null)
        {
            view.UIActivate(1);
            if (view.GetWindow(out nint window) >= 0) ShellNative.SetFocus(window);
        }
        else Focus();
    }

    public bool OwnsWindow(nint window) => IsHandleCreated && (window == Handle || ShellNative.IsChild(Handle, window));

    public bool TranslateKey(ref Message message)
    {
        if (view is null || !OwnsWindow(message.HWnd)) return false;
        var native = new NativeMessage
        {
            Window = message.HWnd, Message = (uint)message.Msg,
            WParam = (nuint)message.WParam, LParam = message.LParam
        };
        return view.TranslateAccelerator(ref native) == 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (initialized) browser?.SetRect(0, new NativeRect(ClientSize.Width, ClientSize.Height));
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseView();
        if (browser is not null)
        {
            if (cookie != 0) browser.Unadvise(cookie);
            if (initialized) browser.Destroy();
            Marshal.ReleaseComObject(browser);
            browser = null;
            initialized = false;
            cookie = 0;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            itemCountTimer.Stop();
            itemCountTimer.Tick -= OnItemCountTick;
            itemCountTimer.Dispose();
            selectionTimer.Stop();
            selectionTimer.Tick -= OnSelectionTick;
            selectionTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ReleaseView()
    {
        itemCountTimer.Stop();
        SetItemCount(null);
        if (enumerationSource is not null)
        {
            ComEventsHelper.Remove(enumerationSource, FolderViewEvents, 201, enumerationDone);
            Marshal.ReleaseComObject(enumerationSource);
            enumerationSource = null;
        }
        if (view is null) return;
        Marshal.ReleaseComObject(view);
        view = null;
    }

    private void ReportError(string message)
    {
        ReportError(() => message);
    }

    private void ReportError(Func<string> message)
    {
        errorMessage = message;
        RefreshLanguage();
        error.Visible = true;
        error.BringToFront();
        NavigationError?.Invoke(this, message().Replace("\n", " "));
    }

    internal void RefreshLanguage()
    {
        if (!HasNavigated) DisplayName = TitleFor(LocationPath, text);
        if (errorMessage is not null)
            error.Text = errorMessage() + "\n\n" + text.T("Use the address bar to choose another folder, or retry with Go.");
    }

    public int OnNavigationPending(nint pidlFolder)
    {
        EndSettling();
        navigationReady = false;
        itemCountTimer.Stop();
        SetItemCount(null);
        HasNavigated = false;
        error.Visible = false;
        return 0;
    }

    public int OnViewCreated(IShellView shellView)
    {
        ReleaseView();
        view = shellView;
        enumerationReady = !Directory.Exists(requestedLocation);
        if (!enumerationReady)
        {
            Guid dispatch = new("00020400-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(view.GetItemObject(0, ref dispatch, out nint pointer));
            try { enumerationSource = Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
            ComEventsHelper.Combine(enumerationSource, FolderViewEvents, 201, enumerationDone);
        }
        ApplyThemeColors();
        UpdateItemCountMonitoring();
        RefreshItemCount();
        return 0;
    }

    public int OnNavigationComplete(nint pidlFolder)
    {
        LocationPath = ShellNative.Name(pidlFolder, 0x80028000) ?? requestedLocation; // Desktop absolute parsing.
        DisplayName = ShellNative.Name(pidlFolder, 0) ?? TitleFor(LocationPath, text);
        requestedLocation = LocationPath;
        navigationReady = true;
        if (IsRecent(LocationPath)) SortNewestFirst();
        UpdateItemCountMonitoring();
        RefreshItemCount();
        error.Visible = false;
        if (pendingSelection is not null && !enumerationReady) return 0;
        CompleteNavigation();
        return 0;
    }

    private void OnEnumerationDone()
    {
        enumerationReady = true;
        RefreshItemCount();
        if (pendingSelection is not null && navigationReady) CompleteNavigation();
    }

    private void CompleteNavigation()
    {
        HasNavigated = true;
        string? selected = pendingSelection;
        pendingSelection = null;
        if (selected is not null && string.Equals(Path.GetDirectoryName(selected), LocationPath,
                StringComparison.OrdinalIgnoreCase) && view is not null)
        {
            nint item = 0;
            try
            {
                Marshal.ThrowExceptionForHR(ShellNative.SHParseDisplayName(selected, 0, out item, 0, out _));
                Marshal.ThrowExceptionForHR(view.SelectItem(ShellNative.ILFindLastID(item), 0x1D));
                BeginSettling(selected);
            }
            catch (COMException ex)
            {
                ReportError(() => text.T("Could not open:\n{0}\n\n{1}", selected, ex.Message));
            }
            finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
        }
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public int OnNavigationFailed(nint pidlFolder)
    {
        navigationReady = false;
        itemCountTimer.Stop();
        SetItemCount(null);
        pendingSelection = null;
        ReportError(() => text.T("Folder unavailable: {0}", requestedLocation));
        return 0;
    }

    // Restoring a folder's saved grouping can rebuild the list after enumeration and drop the
    // selection, so a requested file is re-selected until it has stayed selected for a few ticks.
    private void BeginSettling(string path)
    {
        settlingSelection = path;
        settleTicks = 0;
        heldTicks = 0;
        selectionTimer.Start();
    }

    private void OnSelectionTick(object? sender, EventArgs e)
    {
        if (settlingSelection is null || view is not IFolderView folderView) { EndSettling(); return; }
        settleTicks++;
        if (folderView.ItemCount(1, out int selectedCount) >= 0 && selectedCount > 0) heldTicks++; // SVGIO_SELECTION
        else
        {
            heldTicks = 0;
            nint item = 0;
            try
            {
                if (ShellNative.SHParseDisplayName(settlingSelection, 0, out item, 0, out _) >= 0)
                    view.SelectItem(ShellNative.ILFindLastID(item), 0x1D);
            }
            finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
        }
        if (heldTicks >= 3 || settleTicks >= 40) EndSettling();
    }

    private void EndSettling()
    {
        selectionTimer.Stop();
        if (settlingSelection is null) return;
        settlingSelection = null;
        SelectionSettled?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsRecent(string path) => ShellNative.Recent.Length > 0
        && path.TrimEnd('\\', '/').Equals(ShellNative.Recent.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static readonly PropertyKey DateModified = new() { FormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = 14 };

    // Recent items are shortcuts whose modified time is when the item was last opened.
    private void SortNewestFirst()
    {
        if (view is IFolderView2 folderView)
            folderView.SetSortColumns([new SortColumn { Property = DateModified, Direction = -1 }], 1);
    }

    internal void GroupBy(PropertyKey key)
    {
        if (view is IFolderView2 folderView) folderView.SetGroupBy(ref key, true);
    }

    internal PropertyKey? GroupKey =>
        view is IFolderView2 folderView && folderView.GetGroupBy(out var key, out _) >= 0 ? key : null;

    internal bool SortedNewestFirst
    {
        get
        {
            if (view is not IFolderView2 folderView || folderView.GetSortColumnCount(out int count) < 0 || count < 1) return false;
            var columns = new SortColumn[count];
            return folderView.GetSortColumns(columns, count) >= 0 && columns[0].Property.FormatId == DateModified.FormatId
                && columns[0].Property.PropertyId == DateModified.PropertyId && columns[0].Direction == -1;
        }
    }

    internal static string TitleFor(string path, UiText? text = null)
    {
        if (path == ShellNative.ThisPc) return text?.T("This PC") ?? "This PC";
        if (path == ShellNative.RecycleBin) return text?.T("Recycle Bin") ?? "Recycle Bin";
        if (IsRecent(path)) return text?.T("Recent items") ?? "Recent items";
        string trimmed = path.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
