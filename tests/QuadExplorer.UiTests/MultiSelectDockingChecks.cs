using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static void RunMultiSelectDockingScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "MultiSelectDocking")).FullName;
        string[] paths = Enumerable.Range(0, 5).Select(i =>
            Directory.CreateDirectory(Path.Combine(fixture, "Folder-" + i)).FullName).ToArray();
        string session = Path.Combine(fixture, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ExplorerFavoritesImported = true, CheckUpdates = false,
            GlobalHotkey = false, StartWithWindows = false, AutoRestartMemory = 0,
            Windows = [new() { Panes = [new() { Tabs = [paths[0]] }], ShowFolderTree = false }]
        });
        using var context = MultiWindowContext(session);
        RunMultiWindowContext(context, async () =>
        {
            var form = context.Windows.Single();
            await MultiWindowReady(form);
            form.SetSidebarVisible(false);
            var transport = new HarmonyLib.Harmony("FolderSplitter.MultiSelectDocking");
            var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
                [typeof(object), typeof(DragDropEffects)]);
            transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
            try
            {
                MdStripReorder(form);
                MdPaneMoves(form);
                MdSplitMoves(form);
                await MdWindowMove(context, form, paths);
            }
            finally
            {
                blankTabDrop = null;
                transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
            }
        });
    }

    private static void MdSelect(ExplorerPane pane, params FolderTab[] tabs)
    {
        MsClickTab(pane, tabs[0]);
        for (int i = 1; i < tabs.Length; i++) MsClickTab(pane, tabs[i], Keys.Control);
        Check(pane.SelectedTabs.SequenceEqual(pane.Tabs.Where(tabs.Contains)),
            "docking fixture selects exactly the requested tabs through native Ctrl-click input");
    }

    private static Point MdCenter(ExplorerPane pane)
    {
        Rectangle files = pane.FileAreaOnScreen;
        return new Point(files.Left + files.Width / 2, files.Top + files.Height / 2);
    }

    private static void MdStripReorder(MainForm form)
    {
        TgResetForm(form);
        var pane = form.GetPane(0);
        TgReset(pane, 6);
        FolderTab[] tabs = pane.Tabs.ToArray();
        MdSelect(pane, tabs[1], tabs[3], tabs[4]);
        TgInterceptDrag(form, pane, null, tabs[3], TgStripPoint(pane, tabs[0].Button, after: false));
        Check(pane.Tabs.SequenceEqual(new[] { tabs[1], tabs[3], tabs[4], tabs[0], tabs[2], tabs[5] })
            && pane.ActiveTab == tabs[4],
            "dragging an inactive selected member gathers the whole nonadjacent selection at one strip marker in source order");
        TgContiguous(pane, "selected batch reorder");

        TgResetForm(form, 2);
        TgReset(pane, 3);
        var target = form.GetPane(1);
        var group = target.AddGroup(Enumerable.Repeat<string?>(null, 11).ToArray(), "Almost full", TabGroupColor.Blue)!;
        target.SelectTab(0, focus: false);
        tabs = pane.Tabs.ToArray();
        MdSelect(pane, tabs[0], tabs[2]);
        FolderTab[] targetBefore = target.Tabs.ToArray();
        var layout = form.CurrentLayout;
        TgInterceptDrag(form, pane, null, tabs[0], TgStripPoint(target, TgChip(target, group), after: true), allowed: false);
        Check(pane.Tabs.SequenceEqual(tabs) && target.Tabs.SequenceEqual(targetBefore)
            && target.GroupMembers(group).Count == 11 && form.CurrentLayout == layout,
            "strip overlay rejects a selected batch that would exceed the twelve-member group limit without moving any member");
        target.CloseTab(target.TabCount - 1, remember: false);
        TgInterceptDrag(form, pane, null, tabs[0], TgStripPoint(target, TgChip(target, group), after: true));
        Check(pane.Tabs.SequenceEqual(new[] { tabs[1] }) && target.GroupMembers(group).Count == 12
            && target.Tabs[0].Group == group && target.Tabs[1].Group == group && target.SelectedIndex == 1,
            "cross-pane strip drop accepts the exact group limit and selects the previously active batch member");
        TgContiguous(target, "selected batch strip join");
    }

    private static void MdPaneMoves(MainForm form)
    {
        TgResetForm(form, 2);
        var source = form.GetPane(0);
        var target = form.GetPane(1);
        TgReset(source, 5);
        TgReset(target, 31);
        FolderTab[] tabs = source.Tabs.ToArray(), targetBefore = target.Tabs.ToArray();
        MdSelect(source, tabs[1], tabs[3]);
        var active = source.ActiveTab;
        var layout = form.CurrentLayout;
        TgInterceptDrag(form, source, null, tabs[1], MdCenter(target), allowed: false);
        Check(source.Tabs.SequenceEqual(tabs) && target.Tabs.SequenceEqual(targetBefore)
            && source.ActiveTab == active && form.CurrentLayout == layout && source.SelectedTabs.Count == 2,
            "center overlay rejects an over-capacity batch without partial moves, selection changes or layout changes");
        TgInterceptDrag(form, source, null, tabs[1], TgStripPoint(target, target.Tabs[^1].Button, after: true), allowed: false);
        Check(source.Tabs.SequenceEqual(tabs) && target.Tabs.SequenceEqual(targetBefore),
            "tab-strip overlay also rejects a batch when only one destination slot remains");
        target.CloseTab(remember: false);
        TgInterceptDrag(form, source, null, tabs[1], MdCenter(target));
        Check(source.Tabs.SequenceEqual(new[] { tabs[0], tabs[2], tabs[4] }) && target.TabCount == 32
            && target.SelectedIndex == 31 && source.SelectedTabs.Count == 1,
            "center overlay moves exactly the selected batch at pane capacity, preserves its active member and clears source multi-selection");

        TgResetForm(form);
        var group = source.AddGroup([null, null], "Whole selected", TabGroupColor.Purple)!;
        group.SavedId = "selected-dock-group";
        source.AddBlankTab();
        tabs = source.Tabs.ToArray();
        MdSelect(source, tabs[0], tabs[1], tabs[2]);
        TgRightClick(source, tabs[0].Button);
        var menu = TgVisibleMenu("Move tabs to pane");
        var move = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move tabs to pane");
        menu.Close();
        move.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pane 2").PerformClick();
        Check(source.TabCount == 0 && !source.Visible && target.TabCount == 3 && target.SelectedIndex == 2
            && target.Groups.Single() is { Name: "Whole selected", Color: TabGroupColor.Purple, SavedId: "selected-dock-group" }
            && target.Tabs[^1].Group is null && form.CurrentLayout.GetPanes().SequenceEqual(new[] { 1 }),
            "selected batch pane menu exposes a hidden pane, preserves a whole saved group plus loose tab and removes the emptied source pane");
        TgContiguous(target, "mixed selected batch pane menu");
    }

    private static void MdSplitMoves(MainForm form)
    {
        TgResetForm(form);
        var source = form.GetPane(0);
        var target = form.GetPane(1);
        TgReset(source, 4);
        FolderTab[] tabs = source.Tabs.ToArray();
        MdSelect(source, tabs[0], tabs[2]);
        Rectangle files = source.FileAreaOnScreen;
        TgInterceptDrag(form, source, null, tabs[0], new Point(files.Left + files.Width / 2, files.Top + 1));
        Check(source.Tabs.SequenceEqual(new[] { tabs[1], tabs[3] }) && target.TabCount == 2 && target.SelectedIndex == 1
            && form.CurrentLayout.GetPanes().Length == 2,
            "edge overlay splits a partial nonadjacent selected batch together and keeps the selected member active");

        TgResetForm(form, 4);
        TgReset(source, 4);
        tabs = source.Tabs.ToArray();
        MdSelect(source, tabs[0], tabs[2]);
        var layout = form.CurrentLayout;
        files = source.FileAreaOnScreen;
        TgInterceptDrag(form, source, null, tabs[0], new Point(files.Left + 1, files.Top + files.Height / 2), allowed: false);
        Check(form.CurrentLayout == layout && source.Tabs.SequenceEqual(tabs) && source.SelectedTabs.Count == 2,
            "edge overlay rejects a selected partial batch at the four-pane limit without partial removals");
        MdSelect(source, tabs[0], tabs[1], tabs[2], tabs[3]);
        files = target.FileAreaOnScreen;
        TgInterceptDrag(form, source, null, tabs[1], new Point(files.Left + files.Width / 2, files.Bottom - 1));
        Check(form.CurrentLayout != layout && form.CurrentLayout.GetPanes().Length == 4
            && source.Tabs.SequenceEqual(tabs) && source.ActiveTab == tabs[3],
            "a whole selected pane relocates at the four-pane limit without copying tabs or changing active identity");
        TgResetForm(form);
    }

    private static async Task MdWindowMove(FolderSplitterApplicationContext context, MainForm form, string[] paths)
    {
        var source = form.GetPane(0);
        source.ReplaceTabs(new PaneState { Tabs = paths.ToList() });
        var group = source.CreateGroup(source.Tabs[0])!;
        source.AddToGroup(source.Tabs[1], group);
        source.RenameGroup(group, "Batch window");
        source.SetGroupColor(group, TabGroupColor.Cyan);
        group.SavedId = "selected-window-group";
        FolderTab[] tabs = source.Tabs.ToArray();
        MdSelect(source, tabs[0], tabs[1], tabs[4]);
        int closed = 0;
        source.TabClosed += (_, _) => closed++;
        int windows = context.Windows.Count;
        TgRightClick(source, tabs[0].Button);
        var menu = TgVisibleMenu("Move tabs to new window");
        var move = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move tabs to new window");
        Point point = new(move.Bounds.Left + move.Width / 2, move.Bounds.Top + move.Height / 2);
        nint packed = (nint)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(menu.Handle, 0x201, 1, packed);
        SendMessage(menu.Handle, 0x202, 0, packed);
        var destination = context.Windows.Single(window => window != form);
        await MultiWindowReady(destination);
        var receiving = destination.GetPane(0);
        Check(context.Windows.Count == windows + 1 && destination.Owner is null && destination.Visible
            && receiving.CaptureState().Tabs.SequenceEqual(new[] { paths[0], paths[1], paths[4] })
            && receiving.SelectedIndex == 2 && receiving.Groups.Single() is
                { Name: "Batch window", Color: TabGroupColor.Cyan, SavedId: "selected-window-group" }
            && receiving.Tabs[^1].Group is null,
            "selected batch new-window menu opens every member in strip order with its active tab and whole saved group intact");
        Check(source.Tabs.SequenceEqual(new[] { tabs[2], tabs[3] }) && closed == 0 && source.SelectedTabs.Count == 1,
            "selected batch new-window cleanup leaves outside tabs and creates no close-undo action");
        TgContiguous(receiving, "selected batch new window");

        MdSelect(source, source.Tabs[0], source.Tabs[1]);
        TgRightClick(source, source.Tabs[0].Button);
        menu = TgVisibleMenu("Move tabs to new window");
        move = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move tabs to new window");
        menu.Close();
        move.PerformClick();
        var finalWindow = context.Windows.Single(window => window != destination);
        await MultiWindowReady(finalWindow);
        Check(form.IsDisposed && context.Windows.Count == 2
            && finalWindow.GetPane(0).CaptureState().Tabs.SequenceEqual(new[] { paths[2], paths[3] }),
            "moving all selected source tabs closes only the emptied browser after its complete destination exists");
    }
}
