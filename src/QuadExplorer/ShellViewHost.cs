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
    private string requestedLocation;

    public string LocationPath { get; private set; }
    public string DisplayName { get; private set; }
    public bool HasNavigated { get; private set; }
    public event EventHandler? Navigated;
    public event EventHandler<string>? NavigationError;

    public ShellViewHost(string location)
    {
        LocationPath = requestedLocation = location;
        DisplayName = TitleFor(location);
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
            Navigate(requestedLocation);
        }
        catch (COMException ex) { ReportError(ex.Message); }
    }

    public void Navigate(string location)
    {
        location = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
        if (location.Length == 0) return;
        if (location == "~") location = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        requestedLocation = location;
        if (browser is null) { LocationPath = location; DisplayName = TitleFor(location); return; }
        nint pidl = 0;
        try
        {
            Marshal.ThrowExceptionForHR(ShellNative.SHParseDisplayName(location, 0, out pidl, 0x20000000, out uint attributes));
            if ((attributes & 0x20000000) == 0) throw new IOException("Enter a folder path, not a file.");
            browser.BrowseToIDList(pidl, 0);
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException)
        {
            ReportError($"Could not open:\n{location}\n\n{ex.Message}");
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
        if (view is null) return;
        Marshal.ReleaseComObject(view);
        view = null;
    }

    private void ReportError(string message)
    {
        error.Text = message + "\n\nUse the address bar to choose another folder, or retry with Go.";
        error.Visible = true;
        error.BringToFront();
        NavigationError?.Invoke(this, message.Replace("\n", " "));
    }

    public int OnNavigationPending(nint pidlFolder)
    {
        HasNavigated = false;
        error.Visible = false;
        return 0;
    }

    public int OnViewCreated(IShellView shellView)
    {
        ReleaseView();
        view = shellView;
        return 0;
    }

    public int OnNavigationComplete(nint pidlFolder)
    {
        LocationPath = ShellNative.Name(pidlFolder, 0x80028000) ?? requestedLocation; // Desktop absolute parsing.
        DisplayName = ShellNative.Name(pidlFolder, 0) ?? TitleFor(LocationPath);
        requestedLocation = LocationPath;
        HasNavigated = true;
        error.Visible = false;
        Navigated?.Invoke(this, EventArgs.Empty);
        return 0;
    }

    public int OnNavigationFailed(nint pidlFolder)
    {
        ReportError($"Folder unavailable: {requestedLocation}");
        return 0;
    }

    internal static string TitleFor(string path)
    {
        if (path == ShellNative.ThisPc) return "This PC";
        if (path == ShellNative.RecycleBin) return "Recycle Bin";
        string trimmed = path.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
