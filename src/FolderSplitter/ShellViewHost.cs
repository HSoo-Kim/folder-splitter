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
    private object? selectionSource;
    private readonly Action selectionChanged;
    internal event EventHandler? SelectionChanged;
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
        selectionChanged = () => SelectionChanged?.Invoke(this, EventArgs.Empty);
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
        visual.SetColor(2, ColorTranslator.ToWin32(Theme.ListBack));
        visual.SetColor(3, ColorTranslator.ToWin32(Theme.SortColumn));
        visual.SetColor(4, ColorTranslator.ToWin32(Theme.Muted));
        visual.SetColor(5, ColorTranslator.ToWin32(Theme.ListBack));
        ApplyListFont();
    }

    // The folder view does not implement IVisualProperties.SetFont, so the font goes to its list window,
    // which keeps it through refreshes and theme changes. Each folder visited gets a new list, so
    // navigation applies it again.
    internal void ApplyListFont()
    {
        if (Theme.ListFont is null && !customFont) return;
        nint list = 0;
        if (IsHandleCreated)
            EnumChildWindows(Handle, (window, _) =>
            {
                var name = new System.Text.StringBuilder(32);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "SysListView32") return true;
                list = window;
                return false;
            }, 0);
        if (list == 0) return;
        var (face, points) = Theme.ListFont ?? (SystemFonts.IconTitleFont!.Name, SystemFonts.IconTitleFont.SizeInPoints);
        nint font = ListFonts.Get(face, -(int)Math.Round(points * DeviceDpi / 72d));
        SendMessage(list, 0x0030, font, 1); // WM_SETFONT, redraw
        SendMessage(SendMessage(list, 0x101F, 0, 0), 0x0030, font, 1); // the column header (LVM_GETHEADER)
        customFont = Theme.ListFont is not null;
    }

    private bool customFont;

    // Fonts are shared by every list and kept for the life of the process; a new one is made only when a
    // different font or size is chosen.
    private static class ListFonts
    {
        private static readonly Dictionary<(string, int), nint> Cache = [];

        public static nint Get(string face, int height)
        {
            if (Cache.TryGetValue((face, height), out nint font)) return font;
            var logFont = new ListLogFont { Height = height, Weight = 400, FontCharSet = 1, Quality = 5, FaceName = face };
            font = CreateFontIndirect(ref logFont);
            if (font != 0) Cache[(face, height)] = font;
            return font;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ListLogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, FontCharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }

    private delegate bool ListWindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, ListWindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int length);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectW", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontIndirect(ref ListLogFont logFont);

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
        if (selectionSource is not null)
        {
            ComEventsHelper.Remove(selectionSource, FolderViewEvents, 200, selectionChanged);
            Marshal.ReleaseComObject(selectionSource);
            selectionSource = null;
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
        ClearFilterForNavigation();
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
        Guid events = new("00020400-0000-0000-C000-000000000046");
        if (view.GetItemObject(0, ref events, out nint source) >= 0)
        {
            try { selectionSource = Marshal.GetObjectForIUnknown(source); }
            finally { Marshal.Release(source); }
            ComEventsHelper.Combine(selectionSource, FolderViewEvents, 200, selectionChanged); // DISPID_SELECTIONCHANGED
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
        recentLocations.RemoveAll(path => string.Equals(path, LocationPath, StringComparison.OrdinalIgnoreCase));
        recentLocations.Insert(0, LocationPath);
        if (recentLocations.Count > 16) recentLocations.RemoveAt(16);
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

    // The first selected file-system path and how many items are selected. Virtual items such as
    // This PC have no file path, so they count toward the selection but yield no path.
    private readonly List<string> recentLocations = [];

    // Locations this tab has shown, newest first, for the back/forward buttons' recent list.
    internal IReadOnlyList<string> RecentLocations => recentLocations;

    internal (string? Path, int Count) SelectionInfo()
    {
        var (paths, count) = SelectedPaths(1);
        return (paths.Count > 0 ? paths[0] : null, count);
    }

    // File-system paths of the selection (up to limit) and the total number of selected items.
    // Virtual items such as This PC have no path, so they count toward the selection only.
    internal (IReadOnlyList<string> Paths, int Count) SelectedPaths(int limit = int.MaxValue)
    {
        if (view is not IFolderView folderView || folderView.ItemCount(1, out int count) < 0 || count == 0) // SVGIO_SELECTION
            return ([], 0);
        Guid dataId = new("0000010e-0000-0000-C000-000000000046");
        if (view.GetItemObject(1, ref dataId, out nint pointer) < 0) return ([], count);
        var data = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT, // CF_HDROP
            lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
        };
        try
        {
            data.GetData(ref format, out var medium);
            try
            {
                uint total = ShellNative.DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
                var paths = new List<string>((int)Math.Min(total, (uint)Math.Min(limit, 4096)));
                for (uint i = 0; i < total && paths.Count < limit; i++)
                {
                    uint length = ShellNative.DragQueryFile(medium.unionmember, i, null, 0);
                    if (length == 0) continue;
                    var buffer = new System.Text.StringBuilder((int)length + 1);
                    ShellNative.DragQueryFile(medium.unionmember, i, buffer, length + 1);
                    paths.Add(buffer.ToString());
                }
                return (paths, count);
            }
            finally { ShellNative.ReleaseStgMedium(ref medium); }
        }
        catch (COMException) { return ([], count); }
        finally { Marshal.ReleaseComObject(data); }
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

    // Commands that File Explorer's command bar runs on the selection or the folder background.
    internal int SelectionCount => view is IFolderView folderView && folderView.ItemCount(1, out int count) >= 0 ? count : 0;

    internal bool InvokeSelectionVerb(string verb) =>
        view is IFolderView2 folderView && SelectionCount > 0 && folderView.InvokeVerbOnSelection(verb) >= 0;

    internal bool StartRename() => view is IFolderView2 folderView && SelectionCount > 0 && folderView.DoRename() >= 0;

    // "NewFolder" and "paste" are canonical verbs of the folder background menu.
    internal bool InvokeBackgroundVerb(string verb)
    {
        if (view is null) return false;
        Guid menuId = typeof(IContextMenu).GUID;
        if (view.GetItemObject(0, ref menuId, out nint pointer) < 0) return false; // SVGIO_BACKGROUND
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        nint popup = CreatePopupMenu(), verbA = Marshal.StringToHGlobalAnsi(verb), verbW = Marshal.StringToHGlobalUni(verb);
        nint info = Marshal.AllocHGlobal(Marshal.SizeOf<InvokeCommandInfo>());
        try
        {
            if (menu.QueryContextMenu(popup, 0, 1, 0x7FFF, 0) < 0) return false; // CMF_NORMAL
            Marshal.StructureToPtr(new InvokeCommandInfo
            {
                Size = Marshal.SizeOf<InvokeCommandInfo>(), Mask = 0x4000, Window = Handle, // CMIC_MASK_UNICODE
                Verb = verbA, VerbW = verbW, Show = 1
            }, info, false);
            return menu.InvokeCommand(info) >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(verbA);
            Marshal.FreeHGlobal(verbW);
            DestroyMenu(popup);
            Marshal.ReleaseComObject(menu);
        }
    }

    // Folder view modes with File Explorer's icon sizes (FVM_ICON 1, SMALLICON 2, LIST 3, DETAILS 4, TILE 6, CONTENT 8).
    internal void SetViewModeAndSize(uint mode, int size)
    {
        if (view is IFolderView2 folderView) folderView.SetViewModeAndIconSize(mode, size);
    }

    internal (uint Mode, int Size)? ViewModeAndSize =>
        view is IFolderView2 folderView && folderView.GetViewModeAndIconSize(out uint mode, out int size) >= 0 ? (mode, size) : null;

    internal void SortBy(PropertyKey key, bool descending)
    {
        if (view is IFolderView2 folderView)
            folderView.SetSortColumns([new SortColumn { Property = key, Direction = descending ? -1 : 1 }], 1);
    }

    internal SortColumn? FirstSortColumn
    {
        get
        {
            if (view is not IFolderView2 folderView || folderView.GetSortColumnCount(out int count) < 0 || count < 1) return null;
            var columns = new SortColumn[count];
            return folderView.GetSortColumns(columns, count) >= 0 ? columns[0] : null;
        }
    }

    // Selects exactly these items of the current folder and scrolls to the first one.
    internal int SelectPaths(IEnumerable<string> paths)
    {
        if (view is null) return 0;
        view.SelectItem(0, 0x4); // SVSI_DESELECTOTHERS
        int selected = 0;
        foreach (string path in paths)
        {
            if (!string.Equals(Path.GetDirectoryName(path), LocationPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (ShellNative.SHParseDisplayName(path, 0, out nint item, 0, out _) < 0) continue;
            try
            {
                if (view.SelectItem(ShellNative.ILFindLastID(item), selected == 0 ? 0x19u : 0x1u) >= 0) selected++; // SELECT | ENSUREVISIBLE | FOCUSED
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        return selected;
    }

    // Quick filter: only names containing every typed fragment are listed. Cleared when the tab navigates.
    internal string FilterText { get; private set; } = "";
    internal event EventHandler? FilterChanged;

    internal void SetFilter(string text)
    {
        text = text.Trim();
        if (text == FilterText || browser is not IFolderFilterSite site) return;
        FilterText = text;
        site.SetFilter(text.Length == 0 ? null : new NameFilter(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        view?.Refresh();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearFilterForNavigation()
    {
        if (FilterText.Length == 0 || browser is not IFolderFilterSite site) return;
        FilterText = "";
        site.SetFilter(null);
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

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
