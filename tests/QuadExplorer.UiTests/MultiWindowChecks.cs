using QuadExplorer;
using QuadExplorer.Search;
using System.Reflection;
using System.Text.Json;

internal static partial class Program
{
    private static void RunMultiWindowScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "MultiWindow")).FullName;
        string[] folders = Enumerable.Range(1, 5)
            .Select(i => Directory.CreateDirectory(Path.Combine(fixture, $"Folder-{i}")).FullName).ToArray();
        string session = Path.Combine(fixture, "session.json");
        // Serialize directly: SessionStore.Save upgrades the schema before a context ever sees it.
        File.WriteAllText(session, JsonSerializer.Serialize(new
        {
            Language = "en", ColorTheme = "light", Layout = 2,
            Panes = new[] { new PaneState { Tabs = [folders[0], folders[1]], SelectedTab = 1 },
                new PaneState { Tabs = [folders[2]] } },
            WindowWidth = 1100, WindowHeight = 720, ShowFolderTree = false,
            ExplorerFavoritesImported = true, GlobalHotkey = false, StartWithWindows = false,
            CheckUpdates = false, AutoUpdate = false, AutoRestartMemory = 0, KeepInTray = true
        }));

        SessionState? saved = null;
        using (var context = MultiWindowContext(session))
        {
            var first = context.Windows.Single();
            Check(ReferenceEquals(first.ApplicationContext, context)
                && first.CurrentLayout.GetPanes().Length == 2
                && first.GetPane(0).CaptureState().Tabs.SequenceEqual([folders[0], folders[1]])
                && first.GetPane(0).SelectedIndex == 1,
                "multi-window context migrates a legacy session into one browser with its layout and tabs");
            int exited = 0;
            context.ThreadExit += (_, _) => exited++;
            RunMultiWindowContext(context, async () =>
            {
                await MultiWindowReady(first);
                context.CreateWindowFromActive(first);
                var second = context.Windows.Single(window => !ReferenceEquals(window, first));
                await MultiWindowReady(second);
                Check(context.Windows.Count == 2 && first.Visible && second.Visible && second.Owner is null
                    && ReferenceEquals(second.ApplicationContext, context),
                    "opening a second browser creates a shown peer in the same application context");

                first.Close();
                Check(first.IsDisposed && context.Windows.Count == 1 && ReferenceEquals(context.Windows[0], second)
                    && !second.IsDisposed && exited == 0,
                    "closing the original browser disposes only it and keeps the shared message loop alive");
                await Navigate(second.GetPane(0).ActiveView!, () => second.GetPane(0).Navigate(folders[3]));
                Check(second.GetPane(0).CurrentPath == folders[3],
                    "the surviving browser still navigates after the original browser closes");

                var third = context.CreateWindow(new BrowserWindowState
                {
                    Layout = 3, LayoutTree = PaneLayout.Preset(3, splitFirst: false, vertical: false),
                    Panes = [new PaneState { Tabs = [folders[4], folders[0]], SelectedTab = 1 },
                        new PaneState { Tabs = [folders[1]] }, new PaneState { Tabs = [folders[2]] }],
                    ShowFolderTree = false, ShowSidebar = false
                });
                await MultiWindowReady(third);
                MultiWindowActivated(second);
                Check(ReferenceEquals(context.LastActiveWindow, second),
                    "browser activation records the last-active surviving window");
                second.Hide();
                context.Summon();
                Check(second.Visible && third.Visible && ReferenceEquals(context.LastActiveWindow, second),
                    "summon restores all browsers and retains the captured last-active target");
                MultiWindowActivated(third);
                third.Hide();
                second.Hide();
                context.ToggleFromHotkey();
                Check(third.Visible && second.Visible && ReferenceEquals(context.LastActiveWindow, third),
                    "the global hotkey summons every hidden browser and retains the last-active target");

                second.SetColorTheme("dark");
                Check(context.Windows.All(window => window.ColorTheme == "dark" && window.BackColor == Theme.Canvas
                    && window.FolderTree.Tree.BackColor == Theme.Sidebar),
                    "a theme change repaints both browser windows in one shared palette");
                third.SetColorTheme("light");
                Check(context.Windows.All(window => window.ColorTheme == "light" && window.BackColor == Theme.Canvas),
                    "a theme change from the secondary browser also updates its peer");
                second.SetLanguage("ko");
                Check(context.Windows.All(window => window.Language == "ko"
                    && Descendants(window).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                        .Any(item => item.AccessibleName == "1 pane layout" && item.Text == new UiText("ko").T("1 pane"))),
                    "language changes update both browser state and translated toolbar controls");
                third.SetLanguage("en");
                third.SetUiFont(null, "large");
                third.SetActivePaneColor("blue");
                third.SetAutoRestartMemory(300);
                Check(context.Windows.All(window => window.Language == "en" && window.TextSize == "large"
                    && window.ActivePaneColor == "blue" && window.AutoRestartMemory == 300),
                    "font, active-pane outline and restart settings propagate from either browser");
                third.SetUiFont(null, "normal");
                third.SetAutoRestartMemory(0);
                MultiWindowToggleCommandBar(second.GetPane(0));
                Check(context.Windows.All(window => window.CurrentLayout.GetPanes().All(id =>
                    !Descendants(window.GetPane(id)).OfType<ToolStrip>()
                        .Single(strip => strip.AccessibleName == $"Pane {id + 1} commands").Visible)),
                    "the pane View command-bar preference updates every browser's visible panes");
                MultiWindowToggleCommandBar(third.GetPane(0));
                Check(context.Windows.All(window => window.CurrentLayout.GetPanes().All(id =>
                    Descendants(window.GetPane(id)).OfType<ToolStrip>()
                        .Single(strip => strip.AccessibleName == $"Pane {id + 1} commands").Visible)),
                    "the shared command-bar preference can be restored from another browser");
                second.RestoreFavorite(folders[4]);
                Check(context.Windows.All(window => window.FavoritePaths.Contains(folders[4], StringComparer.OrdinalIgnoreCase)
                    && Descendants(window).OfType<ListBox>().Single(list => list.AccessibleName == "Favorites").Items.Count > 0),
                    "adding a favorite refreshes the shared collection and every favorites sidebar");
                third.ClearFavorites();
                Check(context.Windows.All(window => window.FavoritePaths.Count == 0
                    && window.DeletedFavoritePaths.Contains(folders[4], StringComparer.OrdinalIgnoreCase)),
                    "removing a favorite in a secondary browser updates every browser");
                second.RestoreFavorite(folders[4]);

                second.ApplyLayout(2, vertical: true);
                third.ApplyLayout(3, splitFirst: false, vertical: false);
                third.SaveWorkspace("Peer layout");
                Check(second.Workspaces.Any(workspace => workspace.Name == "Peer layout"),
                    "saving a workspace in one browser updates the shared catalog");
                var untouchedLayout = second.CurrentLayout;
                third.ApplyLayout(1);
                third.LoadWorkspace("Peer layout");
                Check(third.CurrentLayout.GetPanes().Length == 3 && second.CurrentLayout == untouchedLayout,
                    "loading a shared workspace changes only the invoking browser");
                second.DeleteWorkspace("Peer layout");
                Check(third.Workspaces.All(workspace => workspace.Name != "Peer layout"),
                    "deleting a workspace updates the shared catalog without closing browser panes");
                second.SetFolderTreeVisible(false);
                third.SetFolderTreeVisible(true);
                second.SetSidebarWidth(190);
                third.SetSidebarWidth(260);
                third.SetSidebarVisible(false);
                second.GetPane(0).AddTab(folders[4]);
                await MultiWindowReady(second);
                third.GetPane(0).SelectTab(0);
                await MultiWindowReady(third);
                MultiWindowPlace(second, 16, 16, 1100, 720);
                MultiWindowPlace(third, 52, 44, 1020, 680);
                MultiWindowActivated(third);
                Check(context.SaveSession(), "one aggregate save succeeds for all live browsers");
                saved = SessionStore.Load(session);
                Check(saved.Windows.Count == 2 && saved.LastActiveWindow == 1
                    && saved.Windows[0].LayoutTree == second.CurrentLayout
                    && saved.Windows[1].LayoutTree == third.CurrentLayout
                    && saved.Windows[0].Panes[0].Tabs.SequenceEqual(second.GetPane(0).CaptureState().Tabs)
                    && saved.Windows[1].Panes[0].Tabs.SequenceEqual(third.GetPane(0).CaptureState().Tabs),
                    "aggregate persistence captures distinct layouts, tabs and the last-active browser");
                Check(saved.Windows[0].WindowX == second.Left && saved.Windows[0].WindowY == second.Top
                    && saved.Windows[0].WindowWidth == second.Width && saved.Windows[0].WindowHeight == second.Height
                    && saved.Windows[1].WindowX == third.Left && saved.Windows[1].WindowY == third.Top
                    && saved.Windows[1].WindowWidth == third.Width && saved.Windows[1].WindowHeight == third.Height,
                    "aggregate persistence captures independent normal bounds including window positions");
                MultiWindowLegacyProjection(session, saved.Windows[0]);

                int launches = 0;
                second.RestartLauncher = arguments =>
                {
                    launches++;
                    return false;
                };
                second.RestartApplication();
                Check(launches == 1 && context.Windows.Count == 2 && context.Windows.All(window => !window.IsDisposed)
                    && SessionStore.Load(session).Windows.Count == 2,
                    "restart launches once after saving every browser and preserves them when launch fails");
                int secondClosed = 0, thirdClosed = 0;
                second.FormClosed += (_, _) => secondClosed++;
                third.FormClosed += (_, _) => thirdClosed++;
                context.ExitApplication();
                context.ExitApplication();
                Check(second.IsDisposed && third.IsDisposed && secondClosed == 1 && thirdClosed == 1 && exited == 1,
                    "explicit Exit closes every browser and exits the context exactly once even with tray enabled");
            });
        }

        if (saved is not null)
        {
            using var restored = MultiWindowContext(session);
            Check(restored.Windows.Count == 2 && ReferenceEquals(restored.LastActiveWindow, restored.Windows[1]),
                "a recreated context restores both browser registrations and its last-active target");
            RunMultiWindowContext(restored, async () =>
            {
                for (int index = 0; index < restored.Windows.Count; index++)
                {
                    var window = restored.Windows[index];
                    var expected = saved.Windows[index];
                    await MultiWindowReady(window);
                    Check(window.CurrentLayout == expected.LayoutTree
                        && Enumerable.Range(0, 4).All(pane => MultiWindowPaneEqual(window.GetPane(pane).CaptureState(), expected.Panes[pane])),
                        $"round-trip browser {index + 1} restores its own layout, tabs and selected tabs");
                    Check(window.Bounds == new Rectangle(expected.WindowX!.Value, expected.WindowY!.Value,
                        expected.WindowWidth, expected.WindowHeight),
                        $"round-trip browser {index + 1} restores its independent normal bounds");
                    Check(window.FolderTreeVisible == expected.ShowFolderTree && window.SidebarWidth == expected.SidebarWidth,
                        $"round-trip browser {index + 1} restores window-local sidebar and tree settings");
                }
                Check(restored.Windows.All(window => window.Language == "en" && window.ColorTheme == "light"
                    && window.ActivePaneColor == "blue"
                    && window.FavoritePaths.Contains(folders[4], StringComparer.OrdinalIgnoreCase)),
                    "a recreated context restores shared settings and favorites to every browser");
            });
        }
        RunMultiWindowLastClose(fixture, folders[0], keepInTray: false);
        RunMultiWindowLastClose(fixture, folders[0], keepInTray: true);
        foreach (string mode in new[] { "saved", "hidden", "minimized", "wait-pid" })
            RunMultiWindowPresentation(fixture, folders[0], mode);
        RunMultiWindowTrayRelaunch(fixture, folders[0]);
        RunMultiWindowCancelledShutdown(fixture, folders[0]);
        RunMultiWindowRestartSuccess(fixture, folders[0]);
        RunMultiWindowReachability(fixture, folders[0]);
        RunMultiWindowSharedSearch(fixture, folders[0]);
        RunMultiWindowDivider(fixture, folders[0]);
        RunMultiWindowDpiScenario(fixture);
        RunGroupWindowScenario(fixture);
    }

    private static FolderSplitterApplicationContext MultiWindowContext(string session) => new(session, [],
        latestRelease: _ => Task.FromResult<ReleaseInfo?>(null))
    {
        StartHidden = false, StartMinimized = false, RegisterStartupOnLaunch = false
    };

    private static void RunMultiWindowContext(FolderSplitterApplicationContext context, Func<Task> scenario)
    {
        EventHandler? start = null;
        start = async (_, _) =>
        {
            Application.Idle -= start;
            try { await scenario(); }
            catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL multi-window " + exception); }
            finally { context.ExitApplication(); }
        };
        Application.Idle += start;
        try
        {
            context.ShowWindows();
            Application.Run(context);
        }
        finally { Application.Idle -= start; }
    }

    private static async Task MultiWindowReady(MainForm window)
    {
        // Show posts Shown to the UI queue; finish that initialization before inspecting native panes.
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.BeginInvoke(() => shown.TrySetResult());
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(20));
        foreach (int id in window.CurrentLayout.GetPanes())
            if (window.GetPane(id).ActiveView is { HasNavigated: false } view)
                await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static void MultiWindowActivated(MainForm window)
    {
        window.Activate();
        // The QA desktop is deliberately not the input desktop; exercise the activation callback
        // directly as the existing update-notice scenario does, without relying on foreground policy.
        typeof(Form).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);
    }

    private static void MultiWindowPlace(MainForm window, int x, int y, int width, int height)
    {
        var work = Screen.FromControl(window).WorkingArea;
        window.WindowState = FormWindowState.Normal;
        window.StartPosition = FormStartPosition.Manual;
        window.Bounds = new Rectangle(work.Left + x, work.Top + y,
            Math.Max(window.MinimumSize.Width, Math.Min(width, work.Width - x)),
            Math.Max(window.MinimumSize.Height, Math.Min(height, work.Height - y)));
    }

    private static void MultiWindowToggleCommandBar(ExplorerPane pane)
    {
        Descendants(pane).OfType<Button>()
            .Single(button => button.AccessibleName == $"Pane {pane.PaneNumber} Pane options").PerformClick();
        var options = (ContextMenuStrip)typeof(ExplorerPane)
            .GetField("tabMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!;
        var view = options.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "View");
        view.DropDownItems.OfType<ToolStripMenuItem>()
            .Single(item => item.AccessibleName == "Show command bar").PerformClick();
        options.Close();
    }

    private static bool MultiWindowPaneEqual(PaneState actual, PaneState expected) =>
        (actual.Tabs.Count == 0 || actual.SelectedTab == expected.SelectedTab)
        && actual.Tabs.SequenceEqual(expected.Tabs, StringComparer.OrdinalIgnoreCase);

    private static void MultiWindowLegacyProjection(string path, BrowserWindowState first)
    {
        // This DTO intentionally has only the old reader's fields; extra Windows data is ignored.
        var legacy = JsonSerializer.Deserialize<MultiWindowLegacyReader>(File.ReadAllText(path))!;
        Check(legacy.Layout == first.Layout && legacy.LayoutTree == first.LayoutTree
            && legacy.Panes.Count == first.Panes.Count
            && legacy.Panes.Zip(first.Panes).All(pair => MultiWindowPaneEqual(pair.First, pair.Second))
            && legacy.WindowWidth == first.WindowWidth && legacy.WindowHeight == first.WindowHeight
            && legacy.Maximized == first.Maximized && legacy.ActivePane == first.ActivePane
            && legacy.ShowFolderTree == first.ShowFolderTree && legacy.FolderTreeHeight == first.FolderTreeHeight
            && legacy.ShowSidebar == first.ShowSidebar && legacy.SidebarWidth == first.SidebarWidth
            && legacy.FavoritesExpanded == first.FavoritesExpanded
            && legacy.ShowPreview == first.ShowPreview && legacy.ShowDetails == first.ShowDetails
            && legacy.PreviewWidth == first.PreviewWidth,
            "an old-schema reader ignores Windows and sees the first browser's complete legacy projection");
    }

    private sealed class MultiWindowLegacyReader
    {
        public int Layout { get; set; }
        public PaneLayout? LayoutTree { get; set; }
        public List<PaneState> Panes { get; set; } = [];
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool Maximized { get; set; }
        public int ActivePane { get; set; }
        public bool ShowFolderTree { get; set; }
        public int FolderTreeHeight { get; set; }
        public bool ShowSidebar { get; set; }
        public int SidebarWidth { get; set; }
        public bool FavoritesExpanded { get; set; }
        public bool ShowPreview { get; set; }
        public bool ShowDetails { get; set; }
        public int PreviewWidth { get; set; }
    }

    private static void RunMultiWindowPresentation(string fixture, string folder, string mode)
    {
        string session = Path.Combine(fixture, "presentation-" + mode + ".json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ExplorerFavoritesImported = true, KeepInTray = true, GlobalHotkey = false,
            StartWithWindows = false, CheckUpdates = false, AutoUpdate = false, AutoRestartMemory = 0,
            Windows =
            [
                new BrowserWindowState { Maximized = true, Minimized = true, ShowSidebar = true,
                    Panes = [new PaneState { Tabs = [folder] }] },
                new BrowserWindowState { Hidden = true, ShowSidebar = true,
                    Panes = [new PaneState { Tabs = [folder] }] }
            ]
        });
        using var context = new FolderSplitterApplicationContext(session, [], treeRoots: [folder])
        {
            StartHidden = mode == "hidden", StartMinimized = mode is "hidden" or "minimized",
            RestorePresentation = mode == "wait-pid"
        };
        var visibility = context.Windows.Select(window => new MultiWindowVisibility(window)).ToArray();
        RunMultiWindowContext(context, async () =>
        {
            foreach (var window in context.Windows) await MultiWindowReady(window);
            Check(ReferenceEquals(context.LastActiveWindow, context.Windows[0]),
                $"{mode} initial showing preserves the saved last-active browser even when it is unobtrusive");
            Check(mode switch
            {
                "hidden" => context.Windows.All(window => !window.Visible),
                "minimized" => context.Windows.All(window => window.Visible && window.WindowState == FormWindowState.Minimized),
                "wait-pid" => context.Windows[0].Visible && context.Windows[0].WindowState == FormWindowState.Minimized
                    && !context.Windows[1].Visible,
                _ => context.Windows.All(window => window.Visible)
                    && context.Windows[0].WindowState == FormWindowState.Maximized
                    && context.Windows[1].WindowState == FormWindowState.Normal
            }, $"{mode} presentation applies independently to every restored browser");
            Check(mode == "saved" ? context.Windows.All(window => !window.IsUnobtrusive)
                : context.Windows.All(window => window.IsUnobtrusive),
                $"{mode} ordinary launch is visible while explicit and restart presentations remain unobtrusive");
            Check(visibility.Select((record, index) => record.First is { } first
                && (mode == "saved"
                    ? first.State == (index == 0 ? FormWindowState.Maximized : FormWindowState.Normal)
                    : first.State == FormWindowState.Minimized)
                && (mode == "saved" || record.VisibleStates.All(state => state == FormWindowState.Minimized))).All(value => value),
                $"{mode} FIRST VisibleChanged already has the required state with no visible normal-size flash");
            Check(context.SaveSession() && SessionStore.Load(session).Windows.All(window => window.ShowSidebar)
                && SessionStore.Load(session).Windows[0].Maximized,
                $"{mode} aggregate capture retains hidden sidebar preferences and minimized maximized restore state");
            var peer = context.CreateWindowFromActive(context.Windows[0]);
            await MultiWindowReady(peer);
            Check(peer.Visible && peer.WindowState == FormWindowState.Normal && !peer.IsUnobtrusive,
                $"{mode} startup presentation does not hide or minimize a newly requested peer");
        });
    }

    private sealed class MultiWindowVisibility
    {
        internal (FormWindowState State, Rectangle Bounds)? First { get; private set; }
        internal List<FormWindowState> VisibleStates { get; } = [];

        internal MultiWindowVisibility(MainForm window)
        {
            window.VisibleChanged += (_, _) =>
            {
                if (!window.Visible) return;
                First ??= (window.WindowState, window.Bounds);
                VisibleStates.Add(window.WindowState);
            };
        }
    }

    private static void MultiWindowSeed(string session, string folder, int count = 2) =>
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ExplorerFavoritesImported = true, KeepInTray = true, GlobalHotkey = false,
            StartWithWindows = false, CheckUpdates = false, AutoUpdate = false, AutoRestartMemory = 0,
            Windows = Enumerable.Range(0, count).Select(_ => new BrowserWindowState
            {
                Layout = 1, ShowFolderTree = false, Panes = [new PaneState { Tabs = [folder] }],
                WindowWidth = 1100, WindowHeight = 720
            }).ToList()
        });

    private static void MultiWindowTrayCommand(FolderSplitterApplicationContext context, string command)
    {
        var tray = (NotifyIcon)typeof(FolderSplitterApplicationContext)
            .GetField("trayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        tray.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(item => item.Text == command).PerformClick();
    }

    private static async Task MultiWindowIdle()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Idle(object? sender, EventArgs args)
        {
            Application.Idle -= Idle;
            drained.TrySetResult();
        }
        Application.Idle += Idle;
        try { await drained.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { Application.Idle -= Idle; }
    }

    private static void RunMultiWindowCancelledShutdown(string fixture, string folder)
    {
        string session = Path.Combine(fixture, "cancelled-shutdown.json");
        MultiWindowSeed(session, folder);
        using var context = MultiWindowContext(session);
        var windows = context.Windows.ToArray();
        int exits = 0, queries = 0, closes = 0;
        context.ThreadExit += (_, _) => exits++;
        foreach (var window in windows)
        {
            window.FormClosing += (_, args) =>
            {
                if (args.CloseReason == CloseReason.WindowsShutDown) queries++;
            };
            window.FormClosed += (_, _) => closes++;
        }
        RunMultiWindowContext(context, async () =>
        {
            foreach (var window in windows)
            {
                await MultiWindowReady(window);
                window.GetPane(0).AddTab(folder);
            }
            foreach (var window in windows)
                Check(SendMessage(window.Handle, 0x0011, 0, 0) != 0,
                    "a real WM_QUERYENDSESSION allows Windows to continue its shutdown query");
            foreach (var window in windows) SendMessage(window.Handle, 0x0016, 0, 0);
            Check(queries == windows.Length && closes == 0 && exits == 0
                && windows.All(window => !window.IsDisposed) && context.Windows.Count == windows.Length,
                "WM_ENDSESSION(FALSE) leaves every queried browser and the application message loop alive");
            Check(SessionStore.Load(session).Windows.Count == windows.Length
                && SessionStore.Load(session).Windows.All(window => window.Panes[0].Tabs.Count == 2),
                "the shutdown query saves the complete live browser registry before Windows can end the session");
            if (exits != 0) return;
            await MultiWindowIdle();
            foreach (var window in windows) window.Hide();
            MultiWindowTrayCommand(context, "Open Folder Splitter");
            Check(context.TrayIconVisible && windows.All(window => window.Visible),
                "tray Open remains usable after Windows cancels shutdown");
            // The private desktop cannot supply foreground ownership, so dismiss through the browser command first.
            foreach (var window in windows) window.DismissFromHotkey();
            Check(windows.All(window => !window.Visible),
                "the hotkey dismissal command still hides every browser after a cancelled shutdown");
            SendMessage(context.ActivationHandle, 0x0312, 0x4653, 0);
            Check(windows.All(window => window.Visible),
                "the real hotkey receiver still restores every browser after a cancelled shutdown");
            MultiWindowTrayCommand(context, "New window");
            var created = context.Windows.Single(window => !windows.Contains(window));
            await MultiWindowReady(created);
            Check(created.Visible && !created.IsDisposed && context.Windows.Count == windows.Length + 1,
                "tray New window remains usable after Windows cancels shutdown");
            created.Close();
            // Windows queries every browser before confirming shutdown, preserving the complete final snapshot.
            foreach (var window in windows) SendMessage(window.Handle, 0x0011, 0, 0);
            foreach (var window in windows) SendMessage(window.Handle, 0x0016, 1, 0);
            Check(windows.All(window => window.IsDisposed) && context.Windows.Count == 0
                && closes == windows.Length && exits == 1,
                "WM_ENDSESSION(TRUE) closes every browser and exits the application normally");
        });
        var saved = SessionStore.Load(session);
        Check(saved.Windows.Count == windows.Length && saved.Windows.All(window => window.Panes[0].Tabs.Count == 2),
            "confirmed end-session retains the complete snapshot saved during its shutdown query");
    }

    private static void RunMultiWindowUpdateNotice(string fixture, string folder)
    {
        string session = Path.Combine(fixture, "update-owner-close.json");
        MultiWindowSeed(session, folder);
        var latest = new ReleaseInfo(new Version(99, 0, 0), "v99.0.0",
            "https://github.com/HSoo-Kim/folder-splitter/releases/tag/v99.0.0");
        using var context = new FolderSplitterApplicationContext(session, [],
            latestRelease: _ => Task.FromResult<ReleaseInfo?>(latest))
        {
            StartHidden = false, StartMinimized = false, RegisterStartupOnLaunch = false
        };
        RunMultiWindowContext(context, async () =>
        {
            var owner = context.Windows[0];
            var peer = context.Windows[1];
            await MultiWindowReady(owner);
            await MultiWindowReady(peer);
            peer.Hide();
            MultiWindowActivated(owner);
            owner.SetCheckUpdates(true);
            await owner.CheckForUpdateAsync();
            var interrupted = owner.OwnedForms.OfType<UpdateNotice>().SingleOrDefault();
            Check(interrupted is { Visible: true } && context.NotifiedUpdateVersion == latest.Version,
                "the first browser owns the shared release notification before it closes");
            if (interrupted is null) throw new InvalidOperationException("Owner notification was not created");
            owner.Close();
            Check(owner.IsDisposed && interrupted.IsDisposed && context.Windows.Count == 1
                && context.NotifiedUpdateVersion is null,
                "closing the owning browser without dismissal releases the shared update notification marker");
            peer.Show();
            MultiWindowActivated(peer);
            var resumed = peer.OwnedForms.OfType<UpdateNotice>().SingleOrDefault();
            Check(resumed is { Visible: true } && peer.UpdateLinkShown,
                "the same available release is notified in a surviving browser after its owner closes");
            if (resumed is null) throw new InvalidOperationException("Surviving browser notification was not created");
            var replacement = context.CreateWindowFromActive(peer);
            await MultiWindowReady(replacement);
            var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            resumed.FormClosed += (_, _) => dismissed.TrySetResult();
            Descendants(resumed).OfType<Button>().Single(button => button.Text == "Close").PerformClick();
            peer.Close();
            await dismissed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            MultiWindowActivated(replacement);
            await replacement.CheckForUpdateAsync();
            Check(context.NotifiedUpdateVersion == latest.Version && replacement.UpdateLinkShown
                && !replacement.OwnedForms.OfType<UpdateNotice>().Any(),
                "explicit user dismissal remains session-wide even if its browser closes during the dismissal animation");
        });
    }

    private static void RunMultiWindowTrayRelaunch(string fixture, string folder)
    {
        string session = Path.Combine(fixture, "ordinary-after-tray-exit.json");
        MultiWindowSeed(session, folder);
        using (var context = MultiWindowContext(session))
        {
            RunMultiWindowContext(context, async () =>
            {
                foreach (var window in context.Windows) await MultiWindowReady(window);
                // Dismiss each peer through the same consumer command as the global shortcut.
                foreach (var window in context.Windows) window.DismissFromHotkey();
                Check(context.Windows.All(window => !window.Visible),
                    "tray Exit fixture has every saved browser hidden before choosing Exit");
                MultiWindowTrayCommand(context, "Exit");
            });
        }
        Check(SessionStore.Load(session).Windows.All(window => window.Hidden),
            "tray menu Exit persists every hidden browser before disposing the application");
        using var relaunched = MultiWindowContext(session);
        RunMultiWindowContext(relaunched, async () =>
        {
            foreach (var window in relaunched.Windows) await MultiWindowReady(window);
            Check(relaunched.Windows.Count == 2 && relaunched.Windows.All(window => window.Visible
                && window.WindowState == FormWindowState.Normal),
                "ordinary launch after hidden tray Exit reveals ALL saved browser windows");
        });
    }

    private static Action<string>? multiWindowSessionWritten;
    private static void MultiWindowObserveSave(string path) => multiWindowSessionWritten?.Invoke(path);

    private static void RunMultiWindowRestartSuccess(string fixture, string folder)
    {
        string session = Path.Combine(fixture, "restart-success.json");
        MultiWindowSeed(session, folder, count: 3);
        using (var context = MultiWindowContext(session))
        {
            var windows = context.Windows.ToArray();
            int launches = 0, writes = 0, exits = 0;
            int[] closed = new int[windows.Length];
            IReadOnlyList<string>? actualArguments = null;
            context.ThreadExit += (_, _) => exits++;
            for (int index = 0; index < windows.Length; index++)
            {
                int captured = index;
                windows[index].FormClosed += (_, _) => closed[captured]++;
            }
            RunMultiWindowContext(context, async () =>
            {
                foreach (var window in windows) await MultiWindowReady(window);
                windows[0].Hide();
                windows[1].WindowState = FormWindowState.Minimized;
                windows[2].WindowState = FormWindowState.Maximized;
                MultiWindowActivated(windows[2]);
                // Observe the actual store call, not a timer or filesystem-event approximation.
                var harmony = new HarmonyLib.Harmony("FolderSplitter.UiTests.MultiWindowRestartSave");
                var save = HarmonyLib.AccessTools.Method(typeof(SessionStore), nameof(SessionStore.Save));
                var prefix = HarmonyLib.AccessTools.Method(typeof(Program), nameof(MultiWindowObserveSave));
                multiWindowSessionWritten = path => { if (path == session) writes++; };
                harmony.Patch(save, prefix: new HarmonyLib.HarmonyMethod(prefix));
                try
                {
                    windows[2].RestartLauncher = arguments =>
                    {
                        launches++;
                        actualArguments = arguments.ToArray();
                        return true;
                    };
                    windows[2].RestartApplication();
                    windows[2].RestartApplication();
                }
                finally
                {
                    harmony.Unpatch(save, prefix);
                    multiWindowSessionWritten = null;
                }
            });
            Check(launches == 1 && actualArguments is not null && actualArguments.SequenceEqual(
                ["--wait-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--session", session]),
                "successful restart launches exactly once with only --wait-pid pid and the custom --session");
            Check(writes == 1 && windows.All(window => window.IsDisposed)
                && closed.All(count => count == 1) && exits == 1,
                "successful restart writes the aggregate session once, disposes every form and raises ThreadExit once");
        }
        using var restarted = new FolderSplitterApplicationContext(session, [], treeRoots: [folder])
        {
            RestorePresentation = true
        };
        var records = restarted.Windows.Select(window => new MultiWindowVisibility(window)).ToArray();
        RunMultiWindowContext(restarted, async () =>
        {
            foreach (var window in restarted.Windows) await MultiWindowReady(window);
            Check(restarted.Windows.Count == 3 && !restarted.Windows[0].Visible
                && restarted.Windows[1].Visible && restarted.Windows[1].WindowState == FormWindowState.Minimized
                && restarted.Windows[2].Visible && restarted.Windows[2].WindowState == FormWindowState.Maximized
                && ReferenceEquals(restarted.LastActiveWindow, restarted.Windows[2]),
                "--wait-pid replacement context restores hidden, minimized and maximized peers and last-active browser");
            Check(records.Select((record, index) => record.First?.State == (index == 2
                    ? FormWindowState.Maximized : FormWindowState.Minimized)
                && record.VisibleStates.All(state => state != FormWindowState.Normal)).All(value => value),
                "memory-restart replacement FIRST VisibleChanged never presents any peer at normal size");
        });
    }

    private static void RunMultiWindowReachability(string fixture, string folder)
    {
        string session = Path.Combine(fixture, "hidden-peer-reachability.json");
        MultiWindowSeed(session, folder);
        using var context = MultiWindowContext(session);
        var first = context.Windows[0];
        var second = context.Windows[1];
        int exits = 0;
        context.ThreadExit += (_, _) => exits++;
        RunMultiWindowContext(context, async () =>
        {
            await MultiWindowReady(first);
            await MultiWindowReady(second);
            first.Hide();
            MultiWindowActivated(second);
            MultiWindowTrayCommand(context, "Open Folder Splitter");
            Check(first.Visible && second.Visible && ReferenceEquals(context.LastActiveWindow, second),
                "tray menu Open reaches a hidden non-last-active browser and retains the captured focus target");
            first.Hide();
            second.WindowState = FormWindowState.Minimized;
            PostMessage(context.ActivationHandle, 0x0312, 0x4653, 0);
            await MultiWindowIdle();
            Check(first.Visible && second.Visible && context.Windows.All(window => window.WindowState != FormWindowState.Minimized)
                && ReferenceEquals(context.LastActiveWindow, second),
                "real hotkey receiver reveals a hidden non-last-active peer and restores its minimized active peer");
            first.Hide();
            second.SetCloseToTray(false);
            Check(first.Visible && second.Visible && !context.TrayIconVisible,
                "turning tray off reveals a hidden peer even while another browser is already visible");
            first.Hide();
            second.Close();
            Check(first.IsDisposed && second.IsDisposed && context.Windows.Count == 0 && exits == 1,
                "tray-off last-visible close exits the whole context instead of stranding a hidden peer");
        });
    }

    private static void RunMultiWindowSharedSearch(string fixture, string folder)
    {
        string match = Directory.CreateDirectory(Path.Combine(folder, "multiwindow-search-survivor")).FullName;
        string session = Path.Combine(fixture, "shared-search.json");
        MultiWindowSeed(session, folder);
        using var backend = new IndependentSearchBackend([folder], Path.Combine(fixture, "shared-search.fsix"));
        using var context = new FolderSplitterApplicationContext(session, [], backend, [folder]);
        RunMultiWindowContext(context, async () =>
        {
            var original = context.Windows[0];
            var survivor = context.Windows[1];
            await MultiWindowReady(original);
            await MultiWindowReady(survivor);
            await backend.BuildOrLoadAsync(CancellationToken.None);
            original.Close();
            var pane = survivor.GetPane(0);
            var address = Descendants(pane).OfType<TextBox>()
                .Single(box => box.AccessibleName?.EndsWith(" address") == true);
            await Search(pane, address, "multiwindow-search-survivor");
            Check(original.IsDisposed && pane.CandidatesVisible && pane.SearchCandidates.Any(hit => hit.FullPath == match),
                "surviving browser queries the real shared search backend after the original browser closes");
            await Navigate(pane.ActiveView!, () => PostMessage(address.Handle, 0x100, (nint)Keys.Enter, 0));
            Check(pane.CurrentPath == match,
                "surviving browser opens a real shared-search candidate after the original browser closes");
        });
    }

    private static void RunMultiWindowDivider(string fixture, string folder)
    {
        string session = Path.Combine(fixture, "divider-peer.json");
        MultiWindowSeed(session, folder);
        using var context = MultiWindowContext(session);
        RunMultiWindowContext(context, async () =>
        {
            var first = context.Windows[0];
            var second = context.Windows[1];
            await MultiWindowReady(first);
            await MultiWindowReady(second);
            (nint Header, nint List) Handles(MainForm window)
            {
                nint header = 0, list = 0;
                EnumChildWindows(window.GetPane(0).ActiveView!.Handle, (child, _) =>
                {
                    var name = new System.Text.StringBuilder(32);
                    GetClassName(child, name, name.Capacity);
                    if (name.ToString() == "SysHeader32") header = child;
                    else if (name.ToString() == "SysListView32") list = child;
                    return true;
                }, 0);
                return (header, list);
            }
            var a = Handles(first);
            var b = Handles(second);
            var column = new HeaderRect();
            HeaderItemRect(b.Header, 0x1207, 0, ref column);
            nint At(int x) => (nint)(((column.Top + column.Bottom) / 2 << 16) | (x & 0xFFFF));
            int aBefore = (int)SendMessage(a.List, 0x101D, 0, 0);
            int bBefore = (int)SendMessage(b.List, 0x101D, 0, 0);
            PostMessage(b.Header, 0x201, 1, At(column.Right - 1));
            PostMessage(b.Header, 0x200, 1, At(column.Right + 90));
            await MultiWindowIdle();
            int during = (int)SendMessage(b.List, 0x101D, 0, 0);
            Check(second.ColumnDividerDragging && !first.ColumnDividerDragging
                && during - bBefore is >= 90 and <= 92 && (int)SendMessage(a.List, 0x101D, 0, 0) == aBefore,
                "native divider drag in browser B is tracked only by B while browser A remains open");
            PostMessage(b.Header, 0x200, 1, At(column.Right + 40));
            PostMessage(b.Header, 0x202, 0, At(column.Right + 40));
            await MultiWindowIdle();
            Check(!first.ColumnDividerDragging && !second.ColumnDividerDragging
                && (int)SendMessage(b.List, 0x101D, 0, 0) == during - 50
                && (int)SendMessage(a.List, 0x101D, 0, 0) == aBefore,
                "releasing B's native divider commits only B's width and leaves A's columns unchanged");
        });
    }

    private static MainForm MultiWindowCtrlN(FolderSplitterApplicationContext context, MainForm source)
    {
        source.Activate();
        source.GetPane(0).FocusFiles();
        var before = context.Windows.ToArray();
        byte[] saved = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("GetKeyboardState failed");
        byte[] keyboard = new byte[256];
        keyboard[0x11] = keyboard[0xA2] = 0x80;
        try
        {
            if (!PnSetKeyboardState(keyboard)) throw new InvalidOperationException("SetKeyboardState failed");
            var message = Message.Create(source.GetPane(0).ActiveView!.Handle, 0x100, (nint)Keys.N, 1);
            if (!source.PreFilterMessage(ref message)) throw new InvalidOperationException("Ctrl+N was not consumed");
        }
        finally
        {
            if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore keyboard state");
        }
        return context.Windows.Single(window => !before.Contains(window));
    }

    private static void RunMultiWindowDpiScenario(string fixture)
    {
        string folder = Directory.CreateDirectory(Path.Combine(fixture, "NativeDpi")).FullName;
        string session = Path.Combine(fixture, "dpi-physical-roundtrip.json");
        // A real legacy payload, not SessionStore.Save (which would pre-normalize Windows).
        File.WriteAllText(session, JsonSerializer.Serialize(new
        {
            Language = "en", ExplorerFavoritesImported = true, KeepInTray = true, GlobalHotkey = false,
            StartWithWindows = false, CheckUpdates = false, AutoUpdate = false, AutoRestartMemory = 0,
            Layout = 1, ShowFolderTree = false, Panes = new[] { new PaneState { Tabs = [folder] } },
            WindowWidth = 1943, WindowHeight = 1129
        }));
        Size? firstRound = null;
        for (int round = 1; round <= 2; round++)
        {
            using var context = MultiWindowContext(session);
            var window = context.Windows.Single();
            var visibility = new MultiWindowVisibility(window);
            RunMultiWindowContext(context, async () =>
            {
                await MultiWindowReady(window);
                uint nativeDpi = MultiWindowNativeDpi(window.Handle);
                Rectangle work = Screen.FromControl(window).WorkingArea;
                Size expected = new(Math.Min(1943, work.Width), Math.Min(1129, work.Height));
                Console.WriteLine($"METRIC multi_window_dpi round={round} native={nativeDpi} managed={window.DeviceDpi}"
                    + $" requested=1943x1129 work={work.Width}x{work.Height} actual={window.Width}x{window.Height}");
                Check(nativeDpi == window.DeviceDpi && window.Size == expected
                    && visibility.First?.Bounds.Size == expected
                    && (firstRound is null || window.Size == firstRound),
                    $"native DPI round {round} restores legacy physical bounds before FIRST visibility without scale growth");
                firstRound ??= window.Size;
                Check(context.SaveSession() && SessionStore.Load(session).Windows[0].WindowWidth == expected.Width
                    && SessionStore.Load(session).Windows[0].WindowHeight == expected.Height,
                    $"native DPI round {round} saves the same physical width and height without DPI conversion");
            });
        }

        string fresh = Path.Combine(fixture, "dpi-fresh-install.json");
        using var freshContext = new FolderSplitterApplicationContext(fresh, [folder], treeRoots: [folder],
            latestRelease: _ => Task.FromResult<ReleaseInfo?>(null));
        var source = freshContext.Windows.Single();
        var firstVisibility = new MultiWindowVisibility(source);
        RunMultiWindowContext(freshContext, async () =>
        {
            await MultiWindowReady(source);
            uint dpi = MultiWindowNativeDpi(source.Handle);
            var work = Screen.FromControl(source).WorkingArea;
            var defaultSize = new Size(Math.Min((int)Math.Round(1400 * dpi / 96d), work.Width),
                Math.Min((int)Math.Round(900 * dpi / 96d), work.Height));
            Check(source.Size == defaultSize && firstVisibility.First?.Bounds.Size == defaultSize,
                "fresh-install browser scales the 1400x900 default to native DPI before FIRST visibility");
            Check(source.MinimumSize.Width >= (int)Math.Ceiling(900 * dpi / 96d)
                && source.MinimumSize.Height >= (int)Math.Ceiling(600 * dpi / 96d),
                "fresh-install browser minimum width and height scale to native DPI");
            MultiWindowPlace(source, 12, 12, Math.Min(1600, work.Width - 80), Math.Min(1000, work.Height - 80));
            Rectangle normal = source.Bounds;
            for (int maximized = 0; maximized <= 1; maximized++)
            {
                if (maximized == 1) source.WindowState = FormWindowState.Maximized;
                var peer = MultiWindowCtrlN(freshContext, source);
                await MultiWindowReady(peer);
                int offset = (int)Math.Round(32 * dpi / 96d);
                int x = normal.Right + offset > work.Right ? work.Left : normal.Left + offset;
                int y = normal.Bottom + offset > work.Bottom ? work.Top : normal.Top + offset;
                Check(peer.WindowState == FormWindowState.Normal && peer.Size == normal.Size
                    && peer.Location == new Point(x, y) && work.Contains(peer.Bounds)
                    && Screen.FromControl(peer).DeviceName == Screen.FromRectangle(normal).DeviceName,
                    $"Ctrl+N from {(maximized == 1 ? "maximized" : "normal")} source copies normal physical size and cascades within its monitor");
                peer.Close();
                source.WindowState = FormWindowState.Normal;
            }
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint MultiWindowNativeDpi(nint window);

    private static void RunMultiWindowLastClose(string fixture, string folder, bool keepInTray)
    {
        string session = Path.Combine(fixture, keepInTray ? "last-tray-on.json" : "last-tray-off.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ExplorerFavoritesImported = true, Panes = [new PaneState { Tabs = [folder] }],
            KeepInTray = keepInTray, GlobalHotkey = false, StartWithWindows = false,
            CheckUpdates = false, AutoUpdate = false, AutoRestartMemory = 0
        });
        using var context = MultiWindowContext(session);
        var window = context.Windows.Single();
        int exits = 0, closes = 0;
        context.ThreadExit += (_, _) => exits++;
        window.FormClosed += (_, _) => closes++;
        RunMultiWindowContext(context, async () =>
        {
            await MultiWindowReady(window);
            window.Close();
            if (keepInTray)
            {
                Check(!window.Visible && !window.IsDisposed && context.Windows.Count == 1
                    && window.TrayIconVisible && closes == 0 && exits == 0,
                    "closing the last browser with tray enabled retains a hidden browser and the application");
                context.Summon();
                Check(window.Visible && ReferenceEquals(context.LastActiveWindow, window),
                    "summon restores the retained last browser from the notification area");
                window.Close();
                window.SetCloseToTray(false);
                Check(window.Visible && !window.TrayIconVisible,
                    "turning tray mode off summons a hidden last browser instead of stranding the process");
                window.Close();
            }
            Check(window.IsDisposed && context.Windows.Count == 0 && closes == 1 && exits == 1,
                "closing the last browser with tray disabled closes it once and exits the application context");
        });
        Check(SessionStore.Load(session).Windows.Count == 1,
            "last-browser shutdown persists its restorable window before removing it from the live registry");
    }
}
