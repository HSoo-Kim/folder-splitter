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
    private static readonly Guid FolderViewEvents = new("62112AA2-EBE4-11CF-A5FB-0020AFE7292D");

    public string LocationPath { get; private set; }
    public string DisplayName { get; private set; }
    public bool HasNavigated { get; private set; }
    internal string? ErrorStatus => error.Visible && errorMessage is not null ? errorMessage().Replace("\n", " ") : null;
    public event EventHandler? Navigated;
    public event EventHandler<string>? NavigationError;

    public ShellViewHost(string location, UiText? text = null)
    {
        enumerationDone = OnEnumerationDone;
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

    public void RefreshFolder() => view?.Refresh();

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

    private void ReleaseView()
    {
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
        navigationReady = false;
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
        return 0;
    }

    public int OnNavigationComplete(nint pidlFolder)
    {
        LocationPath = ShellNative.Name(pidlFolder, 0x80028000) ?? requestedLocation; // Desktop absolute parsing.
        DisplayName = ShellNative.Name(pidlFolder, 0) ?? TitleFor(LocationPath, text);
        requestedLocation = LocationPath;
        navigationReady = true;
        error.Visible = false;
        if (pendingSelection is not null && !enumerationReady) return 0;
        CompleteNavigation();
        return 0;
    }

    private void OnEnumerationDone()
    {
        enumerationReady = true;
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
        pendingSelection = null;
        ReportError(() => text.T("Folder unavailable: {0}", requestedLocation));
        return 0;
    }

    internal static string TitleFor(string path, UiText? text = null)
    {
        if (path == ShellNative.ThisPc) return text?.T("This PC") ?? "This PC";
        if (path == ShellNative.RecycleBin) return text?.T("Recycle Bin") ?? "Recycle Bin";
        string trimmed = path.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
