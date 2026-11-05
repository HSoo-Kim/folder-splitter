using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using QuadExplorer.Search;

namespace QuadExplorer;

// No browser owns the message loop: closing the first window must not retire shared services.
public sealed class FolderSplitterApplicationContext : ApplicationContext
{
    private readonly List<MainForm> windows = [];
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<MainForm> registeredWindows;
    private readonly IReadOnlyList<string>? treeRoots;
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60 * 60 * 1000 };
    private readonly System.Windows.Forms.Timer memoryTimer = new() { Interval = 5 * 60 * 1000 };
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<CancellationToken, Task<ReleaseInfo?>>? latestRelease;
    private readonly NotifyIcon trayIcon = new() { Text = "Folder Splitter" };
    private readonly Icon icon;
    private readonly ActivationReceiver receiver;
    private readonly UiText text;
    private readonly DateTime startedAt = DateTime.UtcNow;
    private long memoryBaseline;
    private bool saving, refreshing, shuttingDown, disposed, started, restarting, checking;
    private SessionState publishedState;
    private bool launchPresentationApplied;
    private bool restoringWindows;
    private readonly HashSet<MainForm> pendingPresentation = [];
    private MainForm? restoredActiveWindow;
    private string? saveError;

    internal string SessionPath { get; }
    internal SessionState State { get; private set; }
    internal IPathSearchBackend? Search { get; }
    internal List<string> Favorites { get; }
    internal List<string> DeletedFavorites { get; }
    internal List<Workspace> Workspaces { get; }
    internal FolderVisits FolderVisits { get; }
    internal WinEShortcut WinE { get; } = WinEShortcut.ForCurrentUser();
    internal StartupRegistration Startup { get; set; }
    internal Func<IReadOnlyList<string>, bool>? RestartLauncher { get; set; }
    public IReadOnlyList<MainForm> Windows => registeredWindows;
    public MainForm? LastActiveWindow { get; private set; }
    public bool StartHidden { get; init; }
    public bool StartMinimized { get; init; }
    public bool RegisterStartupOnLaunch { get; init; }
    internal bool TrayIconVisible => trayIcon.Visible;
    internal nint ActivationHandle => receiver.Handle;
    internal ReleaseInfo? AvailableUpdate { get; private set; }
    internal Version? NotifiedUpdateVersion { get; set; }
    internal bool Updating { get; private set; }
    internal DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
    internal TimeSpan UpdateCheckInterval => TimeSpan.FromMilliseconds(updateTimer.Interval);

    public FolderSplitterApplicationContext(string sessionPath, IReadOnlyList<string> folders,
        IPathSearchBackend? search = null, IReadOnlyList<string>? treeRoots = null,
        Func<CancellationToken, Task<ReleaseInfo?>>? latestRelease = null)
        : this(sessionPath, folders, search, treeRoots, latestRelease, createWindows: true) { }

    internal FolderSplitterApplicationContext(string sessionPath, IReadOnlyList<string> folders,
        IPathSearchBackend? search, IReadOnlyList<string>? treeRoots,
        Func<CancellationToken, Task<ReleaseInfo?>>? latestRelease, bool createWindows)
    {
        registeredWindows = windows.AsReadOnly();
        SessionPath = sessionPath;
        Search = search;
        this.treeRoots = treeRoots;
        this.latestRelease = latestRelease;
        State = SessionStore.Load(sessionPath);
        publishedState = State;
        text = new UiText(State.Language);
        Favorites = [.. State.Favorites];
        DeletedFavorites = [.. State.DeletedFavorites];
        Workspaces = [.. State.Workspaces];
        FolderVisits = new FolderVisits(State.FrequentFolders);
        Startup = StartupRegistration.ForCurrentUser(sessionPath);
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("QuadExplorer.AppIcon")!)
        using (var source = new Icon(stream)) icon = (Icon)source.Clone();
        trayIcon.Icon = icon;
        var menu = new ContextMenuStrip();
        menu.Items.Add("", null, (_, _) => Summon());
        menu.Items.Add("", null, (_, _) => CreateWindowFromActive(LastActiveWindow!));
        menu.Items.Add("", null, (_, _) => ExitApplication());
        trayIcon.ContextMenuStrip = menu;
        trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Summon(); };
        receiver = new ActivationReceiver(this, Program.ActivationMessage(sessionPath));
        receiver.SetHotkey(State.GlobalHotkey);
        // A scratch/custom session must never upgrade the user's real Win+E registration at launch.
        if (sessionPath.Equals(SessionStore.DefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            try { WinE.UpgradeLegacy(); }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        RefreshTray();
        saveTimer.Tick += (_, _) => SaveSession();
        updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        memoryTimer.Tick += (_, _) => CheckMemory();
        ApplyPalette();
        Theme.EndTransition();
        if (createWindows)
        {
            foreach (var state in State.Windows)
                _ = new MainForm(this, state, windows.Count == 0 ? folders : [], treeRoots);
            LastActiveWindow = windows[Math.Clamp(State.LastActiveWindow, 0, windows.Count - 1)];
        }
    }

    internal void Register(MainForm window)
    {
        windows.Add(window);
        LastActiveWindow ??= window;
    }

    internal void Unregister(MainForm window)
    {
        if (!windows.Remove(window)) return;
        if (LastActiveWindow == window) LastActiveWindow = windows.LastOrDefault();
        if (disposed || shuttingDown) return;
        if (windows.Count == 0)
        {
            ExitThread();
            Dispose();
        }
        else ScheduleSave();
    }

    public void ShowWindows()
    {
        if (!launchPresentationApplied)
        {
            restoredActiveWindow = LastActiveWindow;
            pendingPresentation.UnionWith(windows);
            restoringWindows = pendingPresentation.Count > 0;
            foreach (var window in windows)
            {
                window.StartHidden = StartHidden;
                window.StartMinimized = StartMinimized || StartHidden;
            }
            launchPresentationApplied = true;
        }
        foreach (var window in windows.ToArray()) window.Show();
    }

    // A supplied snapshot is the seed, not a second session load. Group transfer can seed one pane later.
    public MainForm CreateWindow(BrowserWindowState? state = null)
    {
        if (disposed || shuttingDown) throw new ObjectDisposedException(nameof(FolderSplitterApplicationContext));
        var window = new MainForm(this, SessionStore.NormalizeBrowserWindow(state ?? new BrowserWindowState()), [], treeRoots);
        window.Show();
        window.Activate();
        ScheduleSave();
        return window;
    }

    public MainForm CreateWindowFromActive(MainForm source)
    {
        string path = source?.ActiveFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return CreateWindow(new BrowserWindowState { Layout = 1, LayoutTree = PaneLayout.Preset(1),
            Panes = [new PaneState { Tabs = string.IsNullOrEmpty(path) ? [] : [path] }] });
    }

    internal void Activated(MainForm window)
    {
        if (!shuttingDown && !restoringWindows && windows.Contains(window) && LastActiveWindow != window)
        {
            LastActiveWindow = window;
            ScheduleSave();
        }
    }

    public void Summon() => LastActiveWindow?.Summon();
    public void ToggleFromHotkey() => LastActiveWindow?.ToggleFromHotkey();

    internal void WindowShown(MainForm window, bool registerStartup)
    {
        if (!started)
        {
            started = true;
            if ((RegisterStartupOnLaunch || registerStartup) && State.StartWithWindows && Startup.IsSupported)
            {
                try { Startup.EnsureRegistered(); }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            }
            memoryTimer.Start();
            updateTimer.Enabled = State.CheckUpdates && latestRelease is not null;
            _ = CheckForStaleUpdateAsync();
        }
        window.RefreshSharedSettings();
        if (pendingPresentation.Contains(window))
        {
            // Each HWND drains its own Shown queue. Wait for every browser's queued hide before
            // restoring activation, rather than assuming callbacks on different HWNDs are ordered.
            window.BeginInvoke(() =>
            {
                if (!pendingPresentation.Remove(window) || pendingPresentation.Count != 0 || disposed) return;
                restoringWindows = false;
                if (restoredActiveWindow is not { } active || !windows.Contains(active)) return;
                LastActiveWindow = active;
                if (active.Visible && active.WindowState != FormWindowState.Minimized) active.Activate();
            });
        }
    }

    internal void ChangeState(Func<SessionState, SessionState> change) => State = change(State);

    internal void ChangeSettings(Func<SessionState, SessionState> change, bool theme = false)
    {
        if (refreshing) return;
        State = change(State);
        PublishSharedSettings(theme);
        ScheduleSave();
    }

    private void PublishSharedSettings(bool theme = false)
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            text.SetLanguage(State.Language);
            if (theme) ApplyPalette();
            foreach (var window in windows.ToArray()) window.RefreshSharedSettings(publishedState, theme);
            RefreshTray();
            publishedState = State;
        }
        finally
        {
            if (theme) Theme.EndTransition();
            refreshing = false;
        }
    }

    private void ApplyPalette()
    {
        if (State.ColorTheme == SessionStore.CustomTheme && State.CustomAccent is { } accent && State.CustomBackground is { } background)
            Theme.SetCustom(SessionStore.ParseColor(accent), SessionStore.ParseColor(background));
        else Theme.Set(State.ColorTheme);
        Theme.ListFont = State.FontFamily is null && State.TextSize == "normal" ? null
            : (State.FontFamily ?? SystemFonts.IconTitleFont!.Name,
                SystemFonts.IconTitleFont!.SizeInPoints + (State.TextSize switch { "small" => -.75f, "large" => 1.5f, _ => 0f }));
    }

    internal void RefreshFavorites()
    {
        foreach (var window in windows.ToArray()) window.RefreshFavorites();
        ScheduleSave();
    }

    internal void ClearVisits() { FolderVisits.Clear(); ScheduleSave(); }
    internal void RefreshShellViews() { foreach (var window in windows.ToArray()) window.RefreshShellViews(); }

    internal void ScheduleSave()
    {
        if (disposed || shuttingDown || saving || refreshing) return;
        if (!ReferenceEquals(publishedState, State)) PublishSharedSettings();
        saveTimer.Stop();
        saveTimer.Start();
    }

    public bool SaveSession()
    {
        saveTimer.Stop();
        if (saving || disposed || windows.Count == 0) return saveError is null;
        saving = true;
        try
        {
            var snapshots = windows.Select(window => window.CaptureWindowState()).ToList();
            State = State with { Windows = snapshots, LastActiveWindow = Math.Max(0, windows.IndexOf(LastActiveWindow!)),
                Favorites = [.. Favorites], DeletedFavorites = [.. DeletedFavorites], Workspaces = [.. Workspaces],
                FrequentFolders = [.. FolderVisits.Entries] };
            SessionStore.Save(SessionPath, State);
            publishedState = State;
            saveError = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            saveError = error.Message;
            LastActiveWindow?.SetApplicationStatus("Session could not be saved: {0}", error.Message);
            return false;
        }
        finally { saving = false; }
    }

    internal void Closing(MainForm window, FormClosingEventArgs e)
    {
        if (shuttingDown) return;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing or CloseReason.ApplicationExitCall)
        {
            // The OS may close browsers one at a time. Snapshot the complete registry before the first disappears.
            SaveSession();
            shuttingDown = true;
            ExitThread();
            return;
        }
        if (windows.Count == 1 && State.KeepInTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            window.Hide();
            if (!State.TrayNoticeShown)
            {
                State = State with { TrayNoticeShown = true };
                trayIcon.ShowBalloonTip(8000, "Folder Splitter", text.T("Folder Splitter keeps running in the notification area. Press Win+Alt+E or click its icon to reopen it; right-click the icon and choose Exit to quit. You can turn this off in Settings."), ToolTipIcon.Info);
            }
            SaveSession();
            return;
        }
        if (!CanClose(window)) { e.Cancel = true; return; }
        if (windows.Count == 1)
        {
            if (!SaveSession() && !ConfirmSaveFailure(window)) e.Cancel = true;
        }
        else
        {
            // Capture only surviving browsers; a closed peer must not return at the next launch.
            windows.Remove(window);
            if (LastActiveWindow == window) LastActiveWindow = windows.LastOrDefault();
            if (!SaveSession() && !ConfirmSaveFailure(window))
            {
                windows.Add(window);
                LastActiveWindow = window;
                e.Cancel = true;
            }
        }
    }

    private bool CanClose(MainForm owner)
    {
        if (!FileOperations.InProgress) return true;
        owner.Summon();
        MessageBox.Show(owner, text.T("A file operation is still in progress. Wait for it to finish or cancel it before exiting."),
            "Folder Splitter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool ConfirmSaveFailure(MainForm owner) => MessageBox.Show(owner,
        text.T("Your session could not be saved:\n\n{0}\n\nClose anyway?", saveError ?? ""), "Folder Splitter",
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    public void ExitApplication()
    {
        if (disposed || shuttingDown || LastActiveWindow is not { } owner || !CanClose(owner)) return;
        if (!SaveSession() && !ConfirmSaveFailure(owner)) return;
        CloseAll();
    }

    private void CloseAll()
    {
        shuttingDown = true;
        saveTimer.Stop();
        foreach (var window in windows.ToArray()) window.Close();
        ExitThread();
        Dispose();
    }

    internal void SetCloseToTray(bool enabled)
    {
        ChangeSettings(s => s with { KeepInTray = enabled });
        if (!enabled && windows.All(window => !window.Visible)) Summon();
    }

    internal void SetGlobalHotkey(bool enabled)
    {
        if (refreshing) return;
        receiver.SetHotkey(enabled);
        ChangeSettings(s => s with { GlobalHotkey = enabled });
    }

    internal void SetWinEShortcut(bool enabled, MainForm owner)
    {
        try { if (enabled) WinE.Enable(); else WinE.Disable(); }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { owner.SetApplicationStatus("Could not change the Win+E setting: {0}", error.Message); }
        PublishSharedSettings();
    }

    internal void SetStartWithWindows(bool enabled, MainForm owner)
    {
        try { if (enabled) Startup.Enable(); else Startup.Disable(); }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { owner.SetApplicationStatus("Could not change the startup setting: {0}", error.Message); }
        ChangeSettings(s => s with { StartWithWindows = enabled });
    }

    private void RefreshTray()
    {
        trayIcon.Visible = State.KeepInTray;
        var items = trayIcon.ContextMenuStrip!.Items;
        items[0].Text = text.T("Open Folder Splitter");
        items[1].Text = text.T("New window");
        items[2].Text = text.T("Exit");
        Theme.Apply(trayIcon.ContextMenuStrip);
    }

    internal void SetCheckUpdates(bool enabled)
    {
        if (refreshing) return;
        if (!enabled) AvailableUpdate = null;
        updateTimer.Enabled = enabled && latestRelease is not null;
        ChangeSettings(s => s with { CheckUpdates = enabled });
        if (enabled) _ = CheckForUpdateAsync();
    }

    internal Task CheckForStaleUpdateAsync() => DateTime.UtcNow - LastUpdateCheck >= TimeSpan.FromMinutes(15)
        ? CheckForUpdateAsync() : Task.CompletedTask;

    internal async Task CheckForUpdateAsync()
    {
        if (checking || !State.CheckUpdates || latestRelease is null || cancellation.IsCancellationRequested) return;
        checking = true;
        LastUpdateCheck = DateTime.UtcNow;
        try
        {
            ReleaseInfo? latest = await latestRelease(cancellation.Token);
            if (disposed || !State.CheckUpdates) return;
            AvailableUpdate = latest is not null && latest.Version > UpdateChecker.Current ? latest : null;
            foreach (var window in windows.ToArray()) window.RefreshUpdate();
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException
            or IOException or InvalidOperationException or KeyNotFoundException) { }
        finally { checking = false; }
    }

    internal async Task OpenAvailableUpdateAsync(MainForm owner)
    {
        if (AvailableUpdate is not { } update || Updating) return;
        if (!State.AutoUpdate || update.PackageUrl is null || update.PackageSha256 is null || UpdateInstaller.InstallablePath() is not { } exe)
        { OpenReleasePage(update, owner); return; }
        if (FileOperations.InProgress) { owner.SetApplicationStatus("Wait for the file operation to finish or cancel it before restarting or updating."); return; }
        Updating = true;
        foreach (var window in windows.ToArray()) window.RefreshUpdate();
        try { await UpdateInstaller.InstallAsync(update, exe, UpdateInstaller.DownloadAsync, cancellation.Token); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (disposed || LastActiveWindow is null) return;
            if (owner.IsDisposed) owner = LastActiveWindow;
            Updating = false;
            foreach (var window in windows.ToArray()) window.RefreshUpdate();
            owner.SetApplicationStatus("Automatic update failed: {0}", error.Message);
            OpenReleasePage(update, owner);
            return;
        }
        if (disposed || LastActiveWindow is null) return;
        if (owner.IsDisposed) owner = LastActiveWindow;
        if (FileOperations.InProgress)
        {
            Updating = false;
            AvailableUpdate = null;
            foreach (var window in windows.ToArray()) window.RefreshUpdate();
            owner.SetApplicationStatus("Updated. Wait for the file operation to finish, then restart Folder Splitter.");
            return;
        }
        if (!SaveSession())
        {
            Updating = false;
            foreach (var window in windows.ToArray()) window.RefreshUpdate();
            return;
        }
        if (Launch(exe, RestartArguments())) CloseAll();
        else
        {
            Updating = false;
            AvailableUpdate = null;
            foreach (var window in windows.ToArray()) window.RefreshUpdate();
            owner.SetApplicationStatus("Updated. Restart Folder Splitter to finish.");
        }
    }

    private static void OpenReleasePage(ReleaseInfo update, MainForm owner)
    {
        try { Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true }); }
        catch (Win32Exception error) { owner.SetApplicationStatus(error.Message); }
    }

    private List<string> RestartArguments()
    {
        var arguments = new List<string> { "--wait-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (!SessionPath.Equals(SessionStore.DefaultPath, StringComparison.OrdinalIgnoreCase)) arguments.AddRange(["--session", SessionPath]);
        return arguments;
    }

    internal void RestartApplication(MainForm owner)
    {
        if (restarting || Updating) return;
        if (FileOperations.InProgress) { owner.SetApplicationStatus("Wait for the file operation to finish or cancel it before restarting or updating."); return; }
        if (!SaveSession()) return;
        if (!Launch(Environment.ProcessPath!, RestartArguments())) { owner.SetApplicationStatus("Could not restart Folder Splitter."); return; }
        restarting = true;
        CloseAll();
    }

    private bool Launch(string exe, IReadOnlyList<string> arguments)
    {
        if (RestartLauncher is { } launch) return launch(arguments);
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            return Process.Start(info) is not null;
        }
        catch (Win32Exception) { return false; }
    }

    private void CheckMemory()
    {
        if (restarting || Updating || State.AutoRestartMemory == 0 || FileOperations.InProgress) return;
        using var process = Process.GetCurrentProcess();
        long current = process.PrivateMemorySize64;
        var uptime = DateTime.UtcNow - startedAt;
        if (memoryBaseline == 0)
        {
            if (uptime >= TimeSpan.FromMinutes(5) && Search?.IsReady != false) memoryBaseline = current;
            return;
        }
        if (QuadExplorer.MainForm.ShouldRestartForMemory(current - memoryBaseline, State.AutoRestartMemory, uptime,
            windows.All(window => window.IsUnobtrusive)) && LastActiveWindow is { } owner) RestartApplication(owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            shuttingDown = true;
            cancellation.Cancel();
            saveTimer.Dispose();
            updateTimer.Dispose();
            memoryTimer.Dispose();
            receiver.Dispose();
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();
            foreach (var window in windows.ToArray()) window.Dispose();
            windows.Clear();
            Search?.Dispose();
            cancellation.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }

    // HWND_BROADCAST does not reach message-only windows; this invisible top-level HWND outlives browsers.
    private sealed class ActivationReceiver : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x4653;
        private readonly FolderSplitterApplicationContext application;
        private readonly uint activationMessage;
        internal ActivationReceiver(FolderSplitterApplicationContext application, uint activationMessage)
        {
            this.application = application;
            this.activationMessage = activationMessage;
            CreateHandle(new CreateParams { Caption = "Folder Splitter activation", ExStyle = 0x80 });
        }
        internal void SetHotkey(bool enabled)
        {
            UnregisterHotKey(Handle, HotkeyId);
            if (enabled && !RegisterHotKey(Handle, HotkeyId, 0x4000 | 0x0008 | 0x0001, (uint)Keys.E))
                application.LastActiveWindow?.SetApplicationStatus("Win+Alt+E is already used by another program.");
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam == HotkeyId) { application.ToggleFromHotkey(); return; }
            if (activationMessage != 0 && m.Msg == (int)activationMessage)
            {
                if (m.WParam == 1) application.ToggleFromHotkey(); else application.Summon();
                return;
            }
            base.WndProc(ref m);
        }
        public void Dispose() { UnregisterHotKey(Handle, HotkeyId); DestroyHandle(); }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    }
}
