using QuadExplorer;
using System.Reflection;

internal static partial class Program
{
    private static void GroupWindowMove(ExplorerPane pane, TabGroup? group, ExplorerPane.FolderTab? tab = null)
    {
        var menu = group is not null ? TgOpenGroupMenu(pane, group) : TgOpenTabMenu(pane, tab!);
        var item = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text ==
            (group is null ? "Move tab to new window" : "Move group to new window"));
        string session = ((MainForm)pane.FindForm()!).ApplicationContext.SessionPath;
        using var created = new ManualResetEventSlim();
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(session)!, $".{Path.GetFileName(session)}.*.tmp");
        int saves = 0;
        watcher.Created += (_, _) => { Interlocked.Increment(ref saves); created.Set(); };
        watcher.EnableRaisingEvents = true;
        Point point = new(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
        nint location = (nint)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(menu.Handle, 0x201, 1, location);
        SendMessage(menu.Handle, 0x202, 0, location);
        bool saved = created.Wait(TimeSpan.FromSeconds(5));
        // Drain file notifications without pumping the UI's later navigation/save timers.
        Thread.Sleep(100);
        watcher.EnableRaisingEvents = false;
        Check(saved && saves == 1, "group-window transfer performs exactly one aggregate session save");
    }

    private static void RunGroupWindowScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "GroupWindows")).FullName;
        string[] paths = Enumerable.Range(0, 16).Select(i =>
            Directory.CreateDirectory(Path.Combine(fixture, "Folder-" + i)).FullName).ToArray();
        string session = Path.Combine(fixture, "session.json");
        PaneState Pane(string name, string color, int start) => new()
        {
            Tabs = paths.Skip(start).Take(3).ToList(), SelectedTab = 1,
            Groups = [new() { Name = name, Color = color, Start = 0, Count = 2 }]
        };
        SessionStore.Save(session, new SessionState
        {
            Language = "en", GlobalHotkey = false, StartWithWindows = false, CheckUpdates = false,
            AutoRestartMemory = 0, ExplorerFavoritesImported = true, KeepInTray = true,
            Windows = [new() { Panes = [Pane("First", "Blue", 0)], ShowFolderTree = false },
                new() { Panes = [Pane("Second", "Purple", 3)], ShowFolderTree = false }]
        });
        List<BrowserWindowState>? expected = null;
        {
            using (var context = MultiWindowContext(session))
            {
                int exits = 0;
                context.ThreadExit += (_, _) => exits++;
                RunMultiWindowContext(context, async () =>
                {
                    var first = context.Windows[0];
                    var second = context.Windows[1];
                    await MultiWindowReady(first);
                    await MultiWindowReady(second);
                    var source = first.GetPane(0);
                    var peer = second.GetPane(0);
                    var group = source.Groups.Single();
                    Check(peer.Groups.Single().Name == "Second" && peer.GroupMembers(peer.Groups.Single()).Count == 2,
                        "group-window secondary browser initially restores only its own named group");
                    for (int i = 0; i < 2; i++)
                    {
                        var window = context.Windows[i];
                        var pane = window.GetPane(0);
                        pane.SelectTab(0, focus: false);
                        pane.FocusAddress();
                        TgKey(window, Keys.Control | Keys.Shift | Keys.F6);
                        Check(pane.TabStrip.ContainsFocus, $"group-window browser {i + 1} routes Ctrl+Shift+F6 to its own strip");
                        TgKey(pane.TabStrip, Keys.Left);
                        Check(TgChip(pane, pane.Groups.Single()).Selected,
                            $"group-window browser {i + 1} reaches its own chip with native arrows");
                        TgKey(pane.TabStrip, Keys.Enter);
                        Check(pane.Groups.Single().Collapsed && !context.Windows[1 - i].GetPane(0).Groups.Single().Collapsed,
                            $"group-window browser {i + 1} chip Enter does not collapse its peer");
                        TgKey(pane.TabStrip, Keys.Space);
                        TgKey(pane.TabStrip, Keys.Apps);
                        var keyboardMenu = TgVisibleMenu("Move group to new window");
                        Check(keyboardMenu.Visible, $"group-window browser {i + 1} opens the chip menu through its message filter");
                        keyboardMenu.Close();
                        pane.FocusAddress();
                        pane.SelectTab(0, focus: false);
                        TgKey(window, Keys.Control | Keys.Shift | Keys.PageDown);
                        Check(pane.SelectedIndex == 1, $"group-window browser {i + 1} routes Ctrl+Shift+PageDown locally");
                        TgKey(window, Keys.Control | Keys.Shift | Keys.PageUp);
                        Check(pane.SelectedIndex == 0, $"group-window browser {i + 1} routes Ctrl+Shift+PageUp locally");
                    }
                    foreach (string theme in new[] { "dark", "light" })
                    {
                        first.SetColorTheme(theme);
                        foreach (var window in context.Windows)
                        {
                            window.Refresh();
                            var pane = window.GetPane(0);
                            var chip = TgChip(pane, pane.Groups.Single());
                            using var bitmap = TgPrintStrip(pane.TabStrip);
                            Check(TgPixels(bitmap, chip.Bounds, Theme.GroupColor(chip.Group.Color)) > 10,
                                $"group-window {theme} repaints browser {(window == first ? 1 : 2)} chip in the current palette");
                        }
                    }
                    second.SetUiFont(null, "large");
                    foreach (var window in context.Windows)
                    {
                        var pane = window.GetPane(0);
                        var chip = TgChip(pane, pane.Groups.Single());
                        Check(window.TextSize == "large" && pane.TabStrip.DeviceDpi == window.DeviceDpi
                            && pane.TabStrip.ClientRectangle.Contains(chip.Bounds),
                            "group-window shared font refresh lays out chips within both native-DPI tab strips");
                    }
                    second.SetUiFont(null, "normal");
                    foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                    {
                        second.SetLanguage(language);
                        var text = new UiText(language);
                        foreach (var window in context.Windows)
                        {
                            var pane = window.GetPane(0);
                            TgRightClick(pane, TgChip(pane, pane.Groups.Single()));
                            var menu = TgVisibleMenu(text.T("Move group to new window"));
                            Check(menu.Visible && TgChip(pane, pane.Groups.Single()).AccessibleName!.Contains(pane.Groups.Single().Name),
                                $"group-window {language} refreshes the chip and translated move menu in both browsers");
                            menu.Close();
                            TgRightClick(pane, pane.Tabs[0].Button);
                            menu = TgVisibleMenu(text.T("Move tab to new window"));
                            Check(menu.Visible, $"group-window {language} translates the tab move menu in both browsers");
                            menu.Close();
                        }
                    }
                    first.SetLanguage("en");
                    second.SaveWorkspace("Second grouped workspace");
                    peer.Ungroup(peer.Groups.Single());
                    second.LoadWorkspace("Second grouped workspace");
                    Check(peer.Groups.Single().Name == "Second" && source.Groups.Single() == group,
                        "group-window workspace loading restores secondary groups without replacing the first browser's identity");
                    var moveMenu = TgOpenGroupMenu(peer, peer.Groups.Single());
                    var movePane = moveMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move group to pane");
                    moveMenu.Close();
                    movePane.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pane 2").PerformClick();
                    Check(second.GetPane(1).Groups.Single().Name == "Second" && source.Groups.Single() == group,
                        "group-window DockGroup moves only within its invoking secondary browser");
                    second.GetPane(1).CloseGroup(second.GetPane(1).Groups.Single());
                    second.GetPane(1).ReopenTab();
                    Check(second.GetPane(1).Groups.Single().Name == "Second" && !source.CanReopenTab,
                        "group-window secondary group close and one-step undo do not pollute first-window history");
                    second.LoadWorkspace("Second grouped workspace");
                    peer = second.GetPane(0);
                    first.SetSidebarVisible(false);
                    first.Bounds = Screen.FromControl(first).WorkingArea;

                    for (int i = 2; i < 12; i++)
                    {
                        source.AddTab(paths[i + 3], countVisit: false);
                        source.AddToGroup(source.ActiveTab!, group);
                    }
                    var outside = source.Tabs.Single(tab => tab.Group is null);
                    source.CloseTab(source.IndexOfTab(outside));
                    source.ReopenTab();
                    var reopenedOutside = source.ActiveTab!;
                    source.SelectTab(source.IndexOfTab(source.GroupMembers(group)[8]), focus: false);
                    string[] ordered = source.GroupMembers(group).Select(tab => tab.Host!.LocationPath).ToArray();
                    source.SetGroupCollapsed(group, true);
                    Check(group.Collapsed && source.ActiveTab == reopenedOutside,
                        "group-window collapsed transfer fixture retains its previously selected ninth member while selecting outside");
                    var visits = context.FolderVisits.Entries.Select(visit => (visit.Path, visit.Count)).ToArray();
                    bool destinationBeforeRemoval = false;
                    source.StateChanged += (_, _) =>
                    {
                        if (source.GroupMembers(group).Count < 12)
                            destinationBeforeRemoval |= context.Windows.Any(window => window != first && window != second
                                && window.GetPane(0).TabCount == 12 && window.GetPane(0).Groups.Single().Name == "First");
                    };
                    Rectangle cascade = BrowserWindowGeometry.Cascade(first.Bounds, Screen.FromControl(first).WorkingArea, first.DeviceDpi);
                    GroupWindowMove(source, group);
                    var moved = context.Windows.Single(window => window != first && window != second);
                    await MultiWindowReady(moved);
                    var movedPane = moved.GetPane(0);
                    Check(context.Windows.Count == 3 && moved.Owner is null && moved.Visible && moved.CurrentLayout.GetPanes().SequenceEqual([0]),
                        "group-window group menu creates one visible peer with exactly one pane");
                    Check(destinationBeforeRemoval && movedPane.CaptureState().Tabs.SequenceEqual(ordered) && movedPane.TabCount == 12,
                        "group-window all twelve ordered members exist in the destination before the source begins removal");
                    Check(movedPane.Groups.Single() is { Name: "First", Color: TabGroupColor.Blue, Collapsed: false }
                        && movedPane.SelectedIndex == 8 && moved.Bounds == cascade,
                        "group-window transfer preserves metadata and selected member, expands the group and cascades source bounds");
                    Check(source.TabCount == 1 && source.ActiveTab == reopenedOutside && !source.Groups.Any()
                        && context.FolderVisits.Entries.Select(visit => (visit.Path, visit.Count)).SequenceEqual(visits),
                        "group-window source cleanup removes no outside tab and adds no folder visits");
                    Descendants(first).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                        .Single(item => item.AccessibleName == "Redo close").PerformClick();
                    Check(source.TabCount == 0 && source.CanReopenTab,
                        "group-window source redo still closes the original reopened outside tab rather than a transferred member");
                    TgShortcut(first, Keys.Control | Keys.Shift | Keys.T);
                    Check(source.TabCount == 1 && source.ActiveTab?.Group is null && source.CurrentPath == paths[2],
                        "group-window source undo restores only its original outside tab, never the transferred group");

                    var peerGroup = peer.Groups.Single();
                    var single = peer.GroupMembers(peerGroup)[1];
                    string singlePath = single.Host!.LocationPath;
                    GroupWindowMove(peer, null, single);
                    var singleWindow = context.Windows.Single(window => window != first && window != second && window != moved);
                    await MultiWindowReady(singleWindow);
                    Check(context.Windows.Count == 4 && singleWindow.GetPane(0).TabCount == 1
                        && singleWindow.GetPane(0).CurrentPath == singlePath && !singleWindow.GetPane(0).Groups.Any()
                        && peer.GroupMembers(peerGroup).Count == 1,
                        "group-window tab menu transfers just the chosen member into one ungrouped tab without Home or siblings");
                    peer.CloseTab(peer.Tabs.ToList().FindIndex(tab => tab.Group is null), remember: false);
                    bool populatedAtClose = false;
                    second.FormClosed += (_, _) => populatedAtClose = context.Windows.Any(window => window != second
                        && window.GetPane(0).Groups.Any(candidate => candidate.Name == "Second"));
                    GroupWindowMove(peer, peerGroup);
                    Check(second.IsDisposed && context.Windows.Count == 4 && exits == 0 && populatedAtClose,
                        "group-window an emptied source browser closes only after its populated peer exists and never exits the context");
                    expected = context.Windows.Select(window => window.CaptureWindowState()).ToList();
                    Check(SessionStore.Load(session).Windows.Count == 4
                        && SessionStore.Load(session).Windows.SelectMany(window => window.Panes).Sum(pane => pane.Groups.Count) == 2,
                        "group-window aggregate transfer save persists all surviving browsers with their own groups");
                    foreach (var window in context.Windows) window.Hide();
                    typeof(FolderSplitterApplicationContext).GetField("startedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(context, DateTime.UtcNow - TimeSpan.FromHours(1));
                    typeof(FolderSplitterApplicationContext).GetField("memoryBaseline", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(context, -200L * 1024 * 1024);
                    first.SetAutoRestartMemory(100);
                    bool restart = false;
                    context.RestartLauncher = arguments =>
                    {
                        var saved = SessionStore.Load(session);
                        restart = arguments.Contains("--wait-pid") && arguments.Contains(session) && saved.Windows.Count == 4
                            && saved.Windows.All(window => window.Hidden)
                            && saved.Windows.SelectMany(window => window.Panes).Sum(pane => pane.Groups.Count) == 2;
                        return false;
                    };
                    typeof(FolderSplitterApplicationContext).GetMethod("CheckMemory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(context, null);
                    Check(restart && exits == 0, "group-window actual memory restart saves groups and hidden presentation in every window before launching");
                    context.ExitApplication();
                });
            }
            if (expected is not null)
            {
                using var restored = MultiWindowContext(session);
                RunMultiWindowContext(restored, async () =>
                {
                    Check(restored.Windows.Count == expected.Count, "group-window restart restores every surviving browser registration");
                    for (int i = 0; i < expected.Count; i++)
                    {
                        var window = restored.Windows[i];
                        await MultiWindowReady(window);
                        Check(Enumerable.Range(0, 4).All(id =>
                            window.GetPane(id).CaptureState().Tabs.SequenceEqual(expected[i].Panes[id].Tabs)
                            && window.GetPane(id).CaptureState().Groups.SequenceEqual(expected[i].Panes[id].Groups)),
                            $"group-window restart browser {i + 1} restores ordered tabs and metadata in the original window only");
                    }
                    Check(restored.Windows[1].Workspaces.Single(workspace => workspace.Name == "Second grouped workspace")
                        .Panes[0].Groups.Single().Name == "Second", "group-window restart retains the secondary browser's grouped workspace");
                });
            }
            foreach (bool tray in new[] { false, true })
                foreach (bool grouped in new[] { false, true })
                    RunGroupWindowOnlySource(fixture, paths, tray, grouped);
            RunGroupWindowEmptyPane(fixture, paths);
        }
    }

    private static void RunGroupWindowOnlySource(string root, string[] paths, bool tray, bool grouped)
    {
        string session = Path.Combine(root, $"only-{tray}-{grouped}.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", GlobalHotkey = false, StartWithWindows = false, CheckUpdates = false,
            AutoRestartMemory = 0, ExplorerFavoritesImported = true, KeepInTray = tray,
            Panes = [new() { Tabs = paths.Take(grouped ? 12 : 1).ToList(),
                Groups = grouped ? [new() { Name = "Only group", Color = "Red", Start = 0, Count = 12 }] : [] }]
        });
        using var context = MultiWindowContext(session);
        int exits = 0;
        context.ThreadExit += (_, _) => exits++;
        RunMultiWindowContext(context, async () =>
        {
            var source = context.Windows.Single();
            await MultiWindowReady(source);
            var pane = source.GetPane(0);
            GroupWindowMove(pane, grouped ? pane.Groups.Single() : null, grouped ? null : pane.ActiveTab);
            var destination = context.Windows.Single();
            await MultiWindowReady(destination);
            Check(source.IsDisposed && !destination.IsDisposed && destination.Visible && exits == 0,
                $"group-window only-source transfer grouped={grouped} tray={tray} closes the empty source without hiding or exiting its peer");
            Check(destination.GetPane(0).TabCount == (grouped ? 12 : 1)
                && destination.GetPane(0).Groups.Count == (grouped ? 1 : 0)
                && !destination.GetPane(0).CanReopenTab && SessionStore.Load(session).Windows.Count == 1,
                $"group-window only-source transfer grouped={grouped} tray={tray} saves no Home, empty source or close undo entry");
        });
    }

    private static void RunGroupWindowEmptyPane(string root, string[] paths)
    {
        string session = Path.Combine(root, "empty-split.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", GlobalHotkey = false, StartWithWindows = false, CheckUpdates = false,
            AutoRestartMemory = 0, ExplorerFavoritesImported = true, KeepInTray = false, Layout = 2,
            Panes = [new() { Tabs = paths.Take(2).ToList(),
                Groups = [new() { Name = "Split group", Color = "Cyan", Start = 0, Count = 2 }] },
                new() { Tabs = [paths[2]] }]
        });
        using var context = MultiWindowContext(session);
        RunMultiWindowContext(context, async () =>
        {
            var source = context.Windows.Single();
            await MultiWindowReady(source);
            var pane = source.GetPane(0);
            GroupWindowMove(pane, pane.Groups.Single());
            var moved = context.Windows.Single(window => window != source);
            await MultiWindowReady(moved);
            Check(!source.IsDisposed && source.CurrentLayout.GetPanes().SequenceEqual([1])
                && source.GetPane(1).CurrentPath == paths[2] && pane.TabCount == 0 && !pane.CanReopenTab,
                "group-window transferring every member collapses only its empty split and preserves the populated sibling");
            var destination = moved.GetPane(0);
            while (destination.TabCount > 0) destination.CloseTab(destination.TabCount - 1, remember: false);
            var blankGroup = destination.AddGroup([null, null], "Blank members", TabGroupColor.Orange, selectedMember: 1)!;
            GroupWindowMove(destination, blankGroup);
            var blankWindow = context.Windows.Single(window => window != source);
            await MultiWindowReady(blankWindow);
            Check(moved.IsDisposed && blankWindow.GetPane(0).TabCount == 2
                && blankWindow.GetPane(0).Tabs.All(tab => tab.Host is null)
                && blankWindow.GetPane(0).Groups.Single().Name == "Blank members"
                && blankWindow.GetPane(0).SelectedIndex == 1,
                "group-window blank grouped members transfer with their selected slot intact instead of turning into Home folders");
        });
    }
}
