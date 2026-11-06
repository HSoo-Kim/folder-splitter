using System.Reflection;
using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static void RunCrossWindowDragScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "CrossWindowDrag")).FullName;
        string[] paths = Enumerable.Range(0, 8).Select(i =>
            Directory.CreateDirectory(Path.Combine(fixture, "Folder-" + i)).FullName).ToArray();
        string session = Path.Combine(fixture, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ExplorerFavoritesImported = true, CheckUpdates = false,
            GlobalHotkey = false, StartWithWindows = false, AutoRestartMemory = 0,
            Windows = [new() { Panes = [new() { Tabs = [paths[0]] }], ShowFolderTree = false }]
        });
        using (var context = MultiWindowContext(session))
        RunMultiWindowContext(context, async () =>
        {
            var first = context.Windows.Single();
            await MultiWindowReady(first);
            var second = context.CreateWindowFromActive(first);
            await MultiWindowReady(second);
            first.SetSidebarVisible(false);
            second.SetSidebarVisible(false);
            var transport = new HarmonyLib.Harmony("FolderSplitter.CrossWindowDrag");
            var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
                [typeof(object), typeof(DragDropEffects)]);
            transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
            try
            {
                foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                {
                    var text = new UiText(language);
                    Check(!string.IsNullOrWhiteSpace(text.T("Move to window")) && !string.IsNullOrWhiteSpace(text.T("New window"))
                        && (language == "en" || (text.T("Move to window") != "Move to window" && text.T("New window") != "New window")),
                        $"cross-window drag action labels are localized in {language}");
                }
                foreach (string kind in new[] { "tab", "selection", "group" })
                foreach (string destination in new[] { "center", "edge", "strip" })
                {
                    CdReset(first, paths);
                    CdReset(second, [paths[6], paths[7]]);
                    second.BringToFront();
                    var source = first.GetPane(0);
                    var target = second.GetPane(0);
                    var (tab, group, members) = CdPayload(source, kind);
                    int selected = Array.IndexOf(members, source.ActiveTab);
                    var before = target.Tabs.ToArray();
                    string?[] movedPaths = members.Select(member => member.Host?.LocationPath).ToArray();
                    int closed = 0;
                    EventHandler onClosed = (_, _) => closed++;
                    source.TabClosed += onClosed;
                    Point point = destination == "strip" ? TgStripPoint(target, target.Tabs[0].Button, after: false)
                        : destination == "edge" ? CdEdge(target) : MdCenter(target);
                    CdDrag(context, first, source, tab, group, second, point);
                    source.TabClosed -= onClosed;
                    var receiving = second.GetPane(destination == "edge" ? 1 : 0);
                    FolderTab[] received = receiving.Tabs.Where(item => !before.Contains(item)).ToArray();
                    Check(received.Select(item => item.Host?.LocationPath).SequenceEqual(movedPaths)
                        && members.All(member => source.IndexOfTab(member) < 0) && source.TabCount == paths.Length - members.Length,
                        $"cross-window {kind} {destination} drop moves the complete ordered payload without losing outside tabs");
                    Check(receiving.ActiveTab == received[Math.Max(0, selected)] && closed == 0 && !source.CanReopenTab,
                        $"cross-window {kind} {destination} drop preserves the active member and creates no close history");
                    Check(second.CurrentLayout.GetPanes().Length == (destination == "edge" ? 2 : 1)
                        && (destination != "strip" || receiving.Tabs.Take(members.Length).SequenceEqual(received)),
                        $"cross-window {kind} {destination} drop uses the receiving window's layout or insertion position");
                    if (group is not null)
                        Check(receiving.Groups.Single() is { Name: "Dragged group", Color: TabGroupColor.Cyan, Collapsed: false }
                            && receiving.GroupMembers(receiving.Groups.Single()).Count == members.Length,
                            $"cross-window group {destination} drop preserves group metadata and expands its members");
                    TgContiguous(receiving, $"cross-window {kind} {destination}");
                }
                foreach (string kind in new[] { "tab", "selection", "group" })
                {
                    CdReset(first, paths);
                    CdReset(second, [paths[6]]);
                    var source = first.GetPane(0);
                    var target = second.GetPane(0);
                    TgReset(target, 32);
                    var (tab, group, members) = CdPayload(source, kind);
                    var sourceBefore = source.Tabs.ToArray();
                    var targetBefore = target.Tabs.ToArray();
                    second.BringToFront();
                    CdDrag(context, first, source, tab, group, second, MdCenter(target), allowed: false);
                    Check(source.Tabs.SequenceEqual(sourceBefore) && target.Tabs.SequenceEqual(targetBefore) && context.Windows.Count == 2,
                        $"cross-window {kind} capacity rejection leaves both windows unchanged and never tears off");
                    second.ApplyLayout(4);
                    CdDrag(context, first, source, tab, group, second, CdEdge(target), allowed: false);
                    Check(source.Tabs.SequenceEqual(sourceBefore) && second.CurrentLayout.GetPanes().Length == 4,
                        $"cross-window {kind} edge drop rejects a fifth pane instead of relocating a foreign pane");
                }
                CdStripGroupLimits(context, first, second, paths);
                CdGroupJoinDwell(context, first, second, paths);
                CdReentrantRejection(context, first, second, paths);
                await CdEmptiedSource(context, first, second, paths);
                CdCancellation(context, first, second, paths);
                CdOcclusion(context, first, second, paths);
                foreach (bool tray in new[] { false, true })
                foreach (string kind in new[] { "tab", "selection", "group" })
                {
                    context.SetCloseToTray(tray);
                    var sourceWindow = context.CreateWindowFromActive(first);
                    await MultiWindowReady(sourceWindow);
                    CdReset(sourceWindow, kind == "tab" ? [paths[0]] : [paths[0], paths[1]]);
                    MultiWindowPlace(sourceWindow, 0, 0, 1000, 700);
                    var source = sourceWindow.GetPane(0);
                    var (tab, group, members) = CdPayload(source, kind, whole: true);
                    Point point = CdOutsidePoint(context);
                    int count = context.Windows.Count;
                    Rectangle original = sourceWindow.Bounds;
                    CdDrag(context, sourceWindow, source, tab, group, null, point, release: true);
                    var torn = context.Windows.Single(window => window != first && window != second && window != sourceWindow);
                    await MultiWindowReady(torn);
                    Rectangle work = Screen.FromPoint(point).WorkingArea;
                    Check(context.Windows.Count == count && sourceWindow.IsDisposed && torn.Visible && !torn.IsDisposed,
                        $"outside {kind} release closes the emptied source through transfer without exiting or tray hiding (tray={tray})");
                    Check(torn.GetPane(0).CaptureState().Tabs.SequenceEqual(kind == "tab" ? new[] { paths[0] } : new[] { paths[0], paths[1] })
                        && torn.Owner is null && torn.CurrentLayout.GetPanes().Length == 1,
                        $"outside {kind} release seeds exactly its dragged contents in a peer window (tray={tray})");
                    Check(work.Contains(torn.Bounds) && torn.Right == work.Right && torn.Bottom == work.Bottom
                        && torn.Size == new Size(Math.Min(original.Width, work.Width), Math.Min(original.Height, work.Height)),
                        $"outside {kind} release clamps source-sized physical bounds to the release monitor (tray={tray})");
                    torn.Close();
                }
                context.SetCloseToTray(false);
                CdReset(first, paths);
                CdReset(second, [paths[6]]);
                second.BringToFront();
                var originalSource = first.GetPane(0);
                var payload = CdPayload(originalSource, "group");
                CdDrag(context, first, originalSource, payload.tab, payload.group, second, MdCenter(second.GetPane(0)));
                Check(context.SaveSession(), "cross-window drag aggregate session saves successfully");
            }
            finally
            {
                blankTabDrop = null;
                transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
            }
        });
        var persisted = SessionStore.Load(session);
        using var restored = MultiWindowContext(session);
        RunMultiWindowContext(restored, async () =>
        {
            foreach (var window in restored.Windows) await MultiWindowReady(window);
            Check(restored.Windows.Count == persisted.Windows.Count && restored.Windows.Select((window, index) =>
                window.CurrentLayout == persisted.Windows[index].LayoutTree && Enumerable.Range(0, 4).All(id =>
                    MultiWindowPaneEqual(window.GetPane(id).CaptureState(), persisted.Windows[index].Panes[id]))).All(value => value),
                "aggregate cross-window drag session restores every window, layout, ordered path and active tab");
            Check(restored.Windows[1].GetPane(0).Groups.Single() is { Name: "Dragged group", Color: TabGroupColor.Cyan, Collapsed: false },
                "aggregate cross-window drag session restores transferred group metadata in its destination only");
        });
    }

    private static void CdReset(MainForm form, string[] paths)
    {
        TgResetForm(form);
        form.GetPane(0).ReplaceTabs(new PaneState { Tabs = paths.ToList() });
        MultiWindowPlace(form, 0, 0, 1000, 700);
    }

    private static (FolderTab? tab, TabGroup? group, FolderTab[] members) CdPayload(ExplorerPane pane, string kind, bool whole = false)
    {
        if (kind == "group")
        {
            var group = pane.CreateGroup(pane.Tabs[0])!;
            pane.AddToGroup(pane.Tabs[1], group);
            pane.RenameGroup(group, "Dragged group");
            pane.SetGroupColor(group, TabGroupColor.Cyan);
            pane.SelectTab(1, focus: false);
            return (null, group, pane.GroupMembers(group).ToArray());
        }
        if (kind == "selection")
        {
            var tabs = new[] { pane.Tabs[0], pane.Tabs[whole ? 1 : 3] };
            MdSelect(pane, tabs);
            return (tabs[0], null, tabs);
        }
        pane.SelectTab(0, focus: false);
        return (pane.Tabs[0], null, [pane.Tabs[0]]);
    }

    private static Point CdEdge(ExplorerPane pane)
    {
        Rectangle files = pane.FileAreaOnScreen;
        return new Point(files.Left + files.Width / 2, files.Bottom - 1);
    }

    private static Point CdOutsidePoint(FolderSplitterApplicationContext context)
    {
        Rectangle work = Screen.PrimaryScreen!.WorkingArea;
        var point = new Point(work.Right - 2, work.Bottom - 2);
        Check(context.Windows.All(window => !window.Visible || !window.Bounds.Contains(point)),
            "outside-release fixture uses a real monitor point outside all browser bounds");
        return point;
    }

    private static void CdDrag(FolderSplitterApplicationContext context, MainForm form, ExplorerPane pane,
        FolderTab? tab, TabGroup? group, MainForm? target, Point point, bool allowed = true,
        bool release = false, bool escape = false, DragDropEffects? foreignEffect = null, Action? afterDrop = null,
        bool bringTargetToFront = true, Action<TabDockOverlay, DragEventArgs>? hover = null)
    {
        if (bringTargetToFront) target?.BringToFront();
        bool entered = false;
        blankTabDrop = (data, effects) =>
        {
            entered = true;
            var overlays = Application.OpenForms.OfType<TabDockOverlay>().ToArray();
            Check(overlays.Length == context.Windows.Count(window => window.Visible && window.WindowState != FormWindowState.Minimized)
                && overlays.All(overlay => overlay.Visible && overlay.Owner is MainForm),
                "context drag shows one owned overlay on every visible non-minimized browser");
            if (escape && hover is null)
            {
                var query = new QueryContinueDragEventArgs(0, true, DragAction.Continue);
                typeof(Control).GetMethod("OnQueryContinueDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [query]);
                Check(query.Action == DragAction.Cancel, "Escape cancels the real cross-window drag source");
                return DragDropEffects.None;
            }
            if (target is null)
            {
                if (release) context.ContinueTabDrag(new QueryContinueDragEventArgs(0, false, DragAction.Drop), point);
                return foreignEffect ?? DragDropEffects.None;
            }
            var overlay = overlays.Single(item => item.Owner == target);
            var args = new DragEventArgs((IDataObject)data, 0, point.X, point.Y, effects, DragDropEffects.None);
            foreach (string name in new[] { "OnDragEnter", "OnDragOver" })
                typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);
            hover?.Invoke(overlay, args);
            if (escape)
            {
                var query = new QueryContinueDragEventArgs(0, true, DragAction.Continue);
                typeof(Control).GetMethod("OnQueryContinueDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [query]);
                Check(query.Action == DragAction.Cancel, "Escape cancels the real cross-window drag source after hover");
                return DragDropEffects.None;
            }
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);
            Check(args.Effect == (allowed ? DragDropEffects.Move : DragDropEffects.None),
                "cross-window button drag resolves allowed or rejected destination through its own overlay");
            if (release) context.ContinueTabDrag(new QueryContinueDragEventArgs(0, false, DragAction.Drop), point);
            afterDrop?.Invoke();
            return args.Effect;
        };
        try
        {
            if (group is not null)
                ((Action)typeof(TabGroupChip).GetField("DragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(TgChip(pane, group))!)();
            else
                ((EventHandler)typeof(FolderTabButton).GetField("DragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tab!.Button)!)(tab.Button, EventArgs.Empty);
            Check(entered && !Application.OpenForms.OfType<TabDockOverlay>().Any(),
                "cross-window button or chip drag disposes every destination overlay after completion");
        }
        finally { blankTabDrop = null; }
    }

    private static async Task CdEmptiedSource(FolderSplitterApplicationContext context, MainForm first, MainForm second, string[] paths)
    {
        CdReset(second, [paths[6]]);
        var window = context.CreateWindowFromActive(first);
        await MultiWindowReady(window);
        CdReset(window, [paths[0], paths[1]]);
        window.ApplyLayout(2);
        window.GetPane(1).ReplaceTabs(new PaneState { Tabs = [paths[2]] });
        var source = window.GetPane(0);
        var payload = CdPayload(source, "group", whole: true);
        Check(context.ToggleSavedGroup(source, payload.group!), "cross-window fixture saves the complete dragged group");
        string? savedId = payload.group!.SavedId;
        CdDrag(context, window, source, null, payload.group, second, MdCenter(second.GetPane(0)));
        Check(window.CurrentLayout.GetPanes().SequenceEqual(new[] { 1 }) && source.TabCount == 0 && !window.IsDisposed
            && second.GetPane(0).Groups.Single().SavedId == savedId
            && context.SavedGroups.Single(saved => saved.Id == savedId).Paths.SequenceEqual([paths[0], paths[1]]),
            "cross-window whole-group transfer removes the empty source split and preserves its shared saved link");
        source = window.GetPane(1);
        CdDrag(context, window, source, source.Tabs[0], null, second,
            TgStripPoint(second.GetPane(0), second.GetPane(0).Tabs[0].Button, after: false));
        Check(window.IsDisposed && context.Windows.Count == 2 && second.GetPane(0).Tabs[0].Host?.LocationPath == paths[2],
            "cross-window transfer of the final source tab closes only that emptied browser and keeps both peers alive");
    }

    private static void CdStripGroupLimits(FolderSplitterApplicationContext context, MainForm first, MainForm second, string[] paths)
    {
        CdReset(first, paths);
        CdReset(second, [paths[6]]);
        var source = first.GetPane(0);
        var target = second.GetPane(0);
        var payload = CdPayload(source, "selection");
        var group = target.AddGroup(Enumerable.Repeat<string?>(null, 11).ToArray(), "Full join", TabGroupColor.Blue)!;
        second.BringToFront();
        var original = source.Tabs.ToArray();
        CdDrag(context, first, source, payload.tab, null, second, TgStripPoint(target, TgChip(target, group), after: true), allowed: false);
        Check(source.Tabs.SequenceEqual(original) && target.GroupMembers(group).Count == 11,
            "cross-window strip join rejects a complete selection beyond twelve members without partial moves");
        target.CloseTab(target.TabCount - 1, remember: false);
        CdDrag(context, first, source, payload.tab, null, second, TgStripPoint(target, TgChip(target, group), after: true));
        Check(target.GroupMembers(group).Count == 12 && source.TabCount == paths.Length - 2 && target.SelectedIndex == 2,
            "cross-window strip join accepts the exact twelve-member limit and keeps the selected member");
        TgContiguous(target, "cross-window exact group join");
    }

    private static void CdReentrantRejection(FolderSplitterApplicationContext context, MainForm first, MainForm second, string[] paths)
    {
        CdReset(first, paths);
        CdReset(second, [paths[6]]);
        var source = first.GetPane(0);
        var target = second.GetPane(0);
        var original = source.Tabs.ToArray();
        second.BringToFront();
        CdDrag(context, first, source, source.Tabs[0], null, second, MdCenter(target),
            afterDrop: () => { while (target.TabCount < 32) target.AddBlankTab(); });
        Check(source.Tabs.SequenceEqual(original) && target.TabCount == 32 && context.Windows.Count == 2,
            "commit rechecks destination capacity after OLE reentrancy and retains every source tab");
        CdReset(second, [paths[6]]);
        second.BringToFront();
        CdDrag(context, first, source, source.Tabs[0], null, second, CdEdge(second.GetPane(0)),
            afterDrop: () => second.ApplyLayout(4));
        Check(source.Tabs.SequenceEqual(original) && second.CurrentLayout.GetPanes().Length == 4,
            "commit rechecks the receiving window's pane limit after OLE reentrancy");
        CdReset(first, paths);
        CdReset(second, [paths[6]]);
        var payload = CdPayload(source, "selection");
        var joining = target.AddGroup(Enumerable.Repeat<string?>(null, 10).ToArray(), "Reentrant join", TabGroupColor.Red)!;
        second.BringToFront();
        CdDrag(context, first, source, payload.tab, null, second, TgStripPoint(target, TgChip(target, joining), after: true),
            afterDrop: () => target.AddTabInGroup(joining));
        Check(source.TabCount == paths.Length && target.GroupMembers(joining).Count == 11,
            "commit rechecks the twelve-member strip group limit after OLE reentrancy");
    }

    private static void CdCancellation(FolderSplitterApplicationContext context, MainForm first, MainForm second, string[] paths)
    {
        foreach (string kind in new[] { "tab", "selection", "group" })
        {
            CdReset(first, paths);
            CdReset(second, [paths[6]]);
            var source = first.GetPane(0);
            var payload = CdPayload(source, kind);
            var original = source.Tabs.ToArray();
            Point outside = CdOutsidePoint(context);
            CdDrag(context, first, source, payload.tab, payload.group, null, outside, escape: true);
            CdDrag(context, first, source, payload.tab, payload.group, null, outside);
            CdDrag(context, first, source, payload.tab, payload.group, null, outside, release: true, foreignEffect: DragDropEffects.Move);
            Check(context.Windows.Count == 2 && source.Tabs.SequenceEqual(original) && second.GetPane(0).TabCount == 1,
                $"Escape, transport cancellation and an accepted non-browser drop leave a dragged {kind} unchanged");
            second.BringToFront();
            Point chrome = new(second.Left + second.Width / 2, second.Top + 4);
            CdDrag(context, first, source, payload.tab, payload.group, null, chrome, release: true);
            Check(context.Windows.Count == 2 && source.Tabs.SequenceEqual(original),
                $"a {kind} release over browser chrome is not mistaken for an outside tear-off");
        }
    }

    private static void CdOcclusion(FolderSplitterApplicationContext context, MainForm first, MainForm second, string[] paths)
    {
        CdReset(first, paths);
        CdReset(second, [paths[6]]);
        first.Bounds = second.Bounds;
        first.BringToFront();
        var source = first.GetPane(0);
        var original = source.Tabs.ToArray();
        CdDrag(context, first, source, source.Tabs[0], null, second, MdCenter(second.GetPane(0)), allowed: false, bringTargetToFront: false);
        Check(source.Tabs.SequenceEqual(original) && second.GetPane(0).TabCount == 1,
            "browser hit-testing uses native z-order rather than registration order and ignores owned overlays");
        second.WindowState = FormWindowState.Minimized;
        CdDrag(context, first, source, source.Tabs[0], null, null, MdCenter(source));
        second.WindowState = FormWindowState.Normal;
        second.Hide();
        CdDrag(context, first, source, source.Tabs[0], null, null, MdCenter(source));
        second.Show();
        Check(source.Tabs.SequenceEqual(original), "hidden and minimized browser windows have no drag overlays or mutations");
    }
}
