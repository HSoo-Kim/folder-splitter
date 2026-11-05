using QuadExplorer;
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
                    "summon restores the last-active browser without hiding a visible peer");
                MultiWindowActivated(third);
                third.Hide();
                second.Hide();
                context.ToggleFromHotkey();
                Check(third.Visible && !second.Visible && ReferenceEquals(context.LastActiveWindow, third),
                    "the global hotkey summons only the last-active browser");
                second.Show();

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
        foreach (string mode in new[] { "saved", "hidden", "minimized" })
            RunMultiWindowPresentation(fixture, folders[0], mode);
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
            StartHidden = mode == "hidden", StartMinimized = mode is "hidden" or "minimized"
        };
        RunMultiWindowContext(context, async () =>
        {
            foreach (var window in context.Windows) await MultiWindowReady(window);
            Check(ReferenceEquals(context.LastActiveWindow, context.Windows[0]),
                $"{mode} initial showing preserves the saved last-active browser even when it is unobtrusive");
            Check(mode switch
            {
                "hidden" => context.Windows.All(window => !window.Visible),
                "minimized" => context.Windows.All(window => window.Visible && window.WindowState == FormWindowState.Minimized),
                _ => context.Windows[0].Visible && context.Windows[0].WindowState == FormWindowState.Minimized
                    && !context.Windows[1].Visible
            }, $"{mode} presentation applies independently to every restored browser");
            Check(context.Windows.All(window => window.IsUnobtrusive),
                $"{mode} restored browsers are all unobtrusive for memory restart");
            Check(context.SaveSession() && SessionStore.Load(session).Windows.All(window => window.ShowSidebar)
                && SessionStore.Load(session).Windows[0].Maximized,
                $"{mode} aggregate capture retains hidden sidebar preferences and minimized maximized restore state");
            var peer = context.CreateWindowFromActive(context.Windows[0]);
            await MultiWindowReady(peer);
            Check(peer.Visible && peer.WindowState == FormWindowState.Normal && !peer.IsUnobtrusive,
                $"{mode} startup presentation does not hide or minimize a newly requested peer");
        });
    }

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
