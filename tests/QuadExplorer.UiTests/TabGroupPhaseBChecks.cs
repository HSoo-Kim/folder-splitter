using System.Reflection;
using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static ToolStripButton TgHistoryButton(MainForm form, ExplorerPane? pane, string action) =>
        (pane is null ? form.Controls.OfType<ToolStrip>() : Descendants(pane).OfType<ToolStrip>())
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(button => button.AccessibleName == (pane is null
                ? $"{action} close" : $"Pane {pane.PaneNumber} {action} close"));

    private static string?[] TgPaths(ExplorerPane pane, TabGroup group) =>
        pane.GroupMembers(group).Select(tab => tab.Host?.LocationPath).ToArray();

    private static void TgCloseHistory(MainForm form, ExplorerPane pane, string[] paths)
    {
        TgReset(pane, 1);
        var outside = pane.ActiveTab;
        string?[] members = [paths[0], null, paths[1]];
        var group = pane.AddGroup(members, "Atomic 연구", TabGroupColor.Cyan, selectedMember: 2)!;
        int closed = 0;
        void Closed(object? sender, EventArgs args) => closed++;
        pane.TabClosed += Closed;
        try
        {
            pane.CloseGroup(group);
            Check(pane.TabCount == 1 && pane.ActiveTab == outside && pane.Groups.Count == 0 && closed == 1,
                "close group removes all members and emits exactly one close-history action");
            var globalUndo = TgHistoryButton(form, null, "Undo");
            var globalRedo = TgHistoryButton(form, null, "Redo");
            var localUndo = TgHistoryButton(form, pane, "Undo");
            var localRedo = TgHistoryButton(form, pane, "Redo");
            Check(globalUndo.Enabled && localUndo.Enabled, "both history toolbars offer whole-group undo");
            globalUndo.PerformClick();
            var restored = pane.Groups.Single();
            Check(pane.TabCount == 4 && TgPaths(pane, restored).SequenceEqual(members)
                && restored.Name == "Atomic 연구" && restored.Color == TabGroupColor.Cyan
                && pane.ActiveTab == pane.GroupMembers(restored)[2] && !restored.Collapsed,
                "global undo atomically restores order, blank path, name, color and previously selected member");
            Check(globalRedo.Enabled && localRedo.Enabled, "both toolbars offer redo of the restored group");
            pane.SelectTab(0, focus: false);
            globalRedo.PerformClick();
            Check(pane.TabCount == 1 && pane.ActiveTab == outside && !pane.Groups.Any() && closed == 2,
                "global redo closes the entire restored group even after selecting its outside neighbor");
            localUndo.PerformClick();
            restored = pane.Groups.Single();
            Check(TgPaths(pane, restored).SequenceEqual(members) && pane.ActiveTab == pane.GroupMembers(restored)[0],
                "local toolbar undo restores the complete group and defaults to its first member when no member was selected at close");
            localRedo.PerformClick();
            Check(pane.TabCount == 1 && !pane.Groups.Any() && closed == 3,
                "local toolbar redo closes exactly one whole group");
            while (pane.TabCount < 30) pane.AddBlankTab();
            var before = pane.Tabs.ToArray();
            var selected = pane.ActiveTab;
            Check(!pane.CanReopenTab && !globalUndo.Enabled && !localUndo.Enabled,
                "whole-group undo is disabled when only two slots remain for three members");
            pane.ReopenTab();
            Check(pane.Tabs.SequenceEqual(before) && pane.ActiveTab == selected && !pane.Groups.Any(),
                "direct reopen cannot partially restore a group when pane capacity is insufficient");
            pane.CloseTab(pane.TabCount - 1, remember: false);
            Check(pane.CanReopenTab && globalUndo.Enabled && localUndo.Enabled,
                "freeing the final required slot enables both whole-group undo buttons");
            localUndo.PerformClick();
            restored = pane.Groups.Single();
            Check(pane.TabCount == 32 && TgPaths(pane, restored).SequenceEqual(members),
                "capacity-blocked history is retained and restores all members at the exact limit");
            int eventsBefore = closed;
            pane.CloseGroup(restored, remember: false);
            Check(pane.TabCount == 29 && closed == eventsBefore && !globalRedo.Enabled && !localRedo.Enabled,
                "silent group removal records no close and invalidates stale group redo");
            var single = pane.ActiveTab!;
            pane.CloseTab();
            localUndo.PerformClick();
            var reopenedSingle = pane.ActiveTab;
            Check(pane.TabCount == 29 && reopenedSingle != single && reopenedSingle!.Group is null,
                "single blank-tab undo remains independent of group snapshots");
            localRedo.PerformClick();
            Check(pane.TabCount == 28 && !pane.Tabs.Contains(reopenedSingle!),
                "single-tab redo still closes only its restored identity");
        }
        finally { pane.TabClosed -= Closed; }
    }

    private static void TgResetForm(MainForm form, int layout = 1)
    {
        form.ApplyLayout(4);
        for (int i = 0; i < 4; i++) TgReset(form.GetPane(i), 0);
        TgShortcut(form, Keys.Alt | Keys.D1);
        form.ApplyLayout(layout);
    }

    private static void TgMoveMenu(ExplorerPane pane, TabGroup group, int targetNumber)
    {
        typeof(ExplorerPane).GetMethod("ShowGroupMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pane, [group]);
        ContextMenuStrip? menu = null;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (Control.FromHandle(window) is ContextMenuStrip { Visible: true } candidate
                && candidate.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "Move group to pane"))
                menu = candidate;
            return true;
        }, 0);
        if (menu is null) throw new InvalidOperationException("Group chip did not open its context menu.");
        try
        {
            var move = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move group to pane");
            move.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == $"Pane {targetNumber}").PerformClick();
        }
        finally { if (!menu.IsDisposed) menu.Close(); }
    }

    private static void TgGroupMoves(MainForm form, string[] paths)
    {
        TgResetForm(form);
        var source = form.GetPane(0);
        var target = form.GetPane(1);
        source.AddBlankTab();
        var outside = source.ActiveTab;
        string?[] members = [paths[2], null, paths[3]];
        var group = source.AddGroup(members, "Move 日本語", TabGroupColor.Pink, selectedMember: 2)!;
        Check(!target.Visible, "group move fixture starts with a hidden destination");
        TgMoveMenu(source, group, 2);
        var moved = target.Groups.Single();
        Check(target.Visible && form.CurrentLayout.GetPanes().SequenceEqual(new[] { 0, 1 })
            && source.TabCount == 1 && source.ActiveTab == outside && !source.Groups.Any()
            && TgPaths(target, moved).SequenceEqual(members) && moved.Name == "Move 日本語"
            && moved.Color == TabGroupColor.Pink && !moved.Collapsed
            && target.ActiveTab == target.GroupMembers(moved)[2],
            "real group menu exposes its numbered hidden pane and preserves member order, metadata and selection");
        TgContiguous(target, "group menu move");
        Check(form.DockGroup(target, moved, 0, null) && !target.Visible && target.TabCount == 0
            && source.TabCount == 4 && form.CurrentLayout.GetPanes().SequenceEqual(new[] { 0 }),
            "moving all source members into another pane removes the emptied source split");
        Check(!form.DockGroup(target, moved, 0, null), "a removed group identity cannot be moved again");

        TgResetForm(form);
        TgReset(source, 1);
        group = source.AddGroup([null, null], "Collapsed move", TabGroupColor.Yellow, collapsed: true)!;
        Check(group.Collapsed && form.DockGroup(source, group, 1, null)
            && !target.Groups.Single().Collapsed && target.GroupMembers(target.Groups.Single()).All(tab => tab.Button.Available)
            && target.SelectedIndex == 0,
            "moving a collapsed group exposes every member in the destination and selects the first when source selection was outside");

        TgResetForm(form, 2);
        TgReset(source, 1);
        TgReset(target, 30);
        group = source.AddGroup([null, null, null], "Reject whole", TabGroupColor.Red, selectedMember: 1)!;
        FolderTab[] sourceBefore = source.Tabs.ToArray(), targetBefore = target.Tabs.ToArray();
        var active = source.ActiveTab;
        var layout = form.CurrentLayout;
        Check(!form.DockGroup(source, group, 1, null) && source.Tabs.SequenceEqual(sourceBefore)
            && target.Tabs.SequenceEqual(targetBefore) && source.ActiveTab == active
            && source.Groups.Single() == group && form.CurrentLayout == layout,
            "insufficient destination capacity rejects a group without partially moving tabs or changing selection/layout");
        Check(Descendants(source).OfType<Label>().Any(label => label.Text.Contains("Destination cannot hold this group.", StringComparison.Ordinal)),
            "group capacity rejection explains that destination tabs must be closed");
        target.CloseTab(remember: false);
        Check(form.DockGroup(source, group, 1, null) && target.TabCount == 32 && source.TabCount == 1,
            "whole-group move accepts the exact remaining destination capacity");

        TgResetForm(form);
        TgReset(source, 1);
        group = source.AddGroup([null, null], "Split group", TabGroupColor.Green, selectedMember: 1)!;
        Check(form.DockGroup(source, group, 0, SplitEdge.Right) && form.CurrentLayout.GetPanes().Length == 2
            && source.TabCount == 1 && target.TabCount == 2 && target.Groups.Single().Name == "Split group"
            && target.Groups.Single().Color == TabGroupColor.Green && target.SelectedIndex == 1,
            "splitting a partial group creates one pane containing the complete selected group");

        TgResetForm(form, 4);
        group = source.AddGroup([null, null], "Relocate", TabGroupColor.Orange, selectedMember: 1)!;
        FolderTab[] originals = source.Tabs.ToArray();
        layout = form.CurrentLayout;
        Check(form.DockGroup(source, group, 1, SplitEdge.Bottom) && form.CurrentLayout != layout
            && form.CurrentLayout.GetPanes().Length == 4 && source.Tabs.SequenceEqual(originals)
            && source.Groups.Single() == group && source.ActiveTab == originals[1],
            "a group occupying its whole pane relocates the existing pane at the four-pane limit without reopening tabs");
        source.AddBlankTab();
        layout = form.CurrentLayout;
        Check(!form.DockGroup(source, group, 1, SplitEdge.Left) && form.CurrentLayout == layout
            && source.GroupMembers(group).SequenceEqual(originals),
            "a partial group cannot split a fifth pane and keeps every original member");
        TgResetForm(form);
    }

    private static void TgGroupDrags(MainForm form)
    {
        var transport = new HarmonyLib.Harmony("FolderSplitter.TabGroupPhaseB");
        var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
            [typeof(object), typeof(DragDropEffects)]);
        transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
        string output = Path.Combine(Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR")
            ?? AppContext.BaseDirectory, "artifacts", "tab-groups");
        Directory.CreateDirectory(output);
        var pane = form.GetPane(0);
        try
        {
            TgResetForm(form, 2);
            TgReset(pane, 1);
            var group = pane.AddGroup([null, null], "Drag group", TabGroupColor.Purple, selectedMember: 1)!;
            var target = form.GetPane(1);
            Rectangle files = target.FileAreaOnScreen;
            TgInterceptDrag(form, pane, group, null, new Point(files.Left + files.Width / 2, files.Top + files.Height / 2));
            Check(pane.TabCount == 1 && !pane.Groups.Any() && target.TabCount == 2
                && target.Groups.Single().Name == "Drag group" && target.Groups.Single().Color == TabGroupColor.Purple
                && target.SelectedIndex == 1,
                "chip drag reaches MainForm and moves the complete group through the real overlay");
            using (var image = PrintForm(form, 2)) TgSave(image, Path.Combine(output, "tab-groups-drag-move-result.png"));

            TgResetForm(form);
            TgReset(pane, 1);
            group = pane.AddGroup([null, null], "Drag split", TabGroupColor.Cyan, selectedMember: 1)!;
            files = pane.FileAreaOnScreen;
            TgInterceptDrag(form, pane, group, null, new Point(files.Left + files.Width / 2, files.Top + 1));
            Check(form.CurrentLayout.GetPanes().Length == 2 && pane.TabCount == 1 && target.TabCount == 2
                && target.Groups.Single().Name == "Drag split" && target.SelectedIndex == 1,
                "chip edge drag creates one split holding every group member and the selected member");
            TgResetForm(form, 2);
            TgReset(pane, 1);
            TgReset(target, 31);
            group = pane.AddGroup([null, null], "Blocked drag", TabGroupColor.Red)!;
            var sourceTabs = pane.Tabs.ToArray();
            var targetTabs = target.Tabs.ToArray();
            files = target.FileAreaOnScreen;
            TgInterceptDrag(form, pane, group, null, new Point(files.Left + files.Width / 2, files.Top + files.Height / 2), allowed: false);
            Check(pane.Tabs.SequenceEqual(sourceTabs) && target.Tabs.SequenceEqual(targetTabs),
                "overlay refuses a destination that cannot hold the complete dragged group without any partial move");
            form.ApplyLayout(4);
            files = pane.FileAreaOnScreen;
            TgInterceptDrag(form, pane, group, null, new Point(files.Left + 1, files.Top + files.Height / 2), allowed: false);
            Check(form.CurrentLayout.GetPanes().Length == 4 && pane.Tabs.SequenceEqual(sourceTabs),
                "overlay refuses a partial group split at the four-pane limit");

            TgResetForm(form);
            TgReset(pane, 5);
            FolderTab[] tabs = pane.Tabs.ToArray();
            group = pane.CreateGroup(tabs[1])!;
            pane.AddToGroup(tabs[2], group);
            pane.RenameGroup(group, "Reorder");
            pane.SetGroupColor(group, TabGroupColor.Blue);
            pane.SelectTab(pane.IndexOfTab(tabs[2]), focus: false);
            Point after = TgStripPoint(pane, tabs[4].Button, after: true);
            TgInterceptDrag(form, pane, group, null, after, markerCapture: Path.Combine(output, "tab-groups-insertion-marker.png"));
            Check(pane.Tabs.SequenceEqual(new[] { tabs[0], tabs[3], tabs[4], tabs[1], tabs[2] })
                && pane.ActiveTab == tabs[2] && pane.GroupMembers(group).SequenceEqual(new[] { tabs[1], tabs[2] }),
                "chip strip drag reorders its whole group with member order and active identity intact");
            TgContiguous(pane, "chip strip reorder");
            TgInterceptDrag(form, pane, null, tabs[4], TgStripPoint(pane, tabs[0].Button, after: false));
            Check(pane.Tabs.SequenceEqual(new[] { tabs[4], tabs[0], tabs[3], tabs[1], tabs[2] }) && tabs[4].Group is null,
                "ordinary tab strip drag reorders an ungrouped tab without changing group membership");
            TgInterceptDrag(form, pane, null, tabs[3], TgStripPoint(pane, tabs[2].Button, after: false));
            Check(pane.GroupMembers(group).SequenceEqual(new[] { tabs[1], tabs[3], tabs[2] }) && tabs[3].Group == group,
                "dropping a tab between two group members joins at the actual insertion gap");
            TgInterceptDrag(form, pane, null, tabs[3], TgStripPoint(pane, tabs[4].Button, after: false));
            Check(pane.Tabs[0] == tabs[3] && tabs[3].Group is null && pane.GroupMembers(group).Count == 2,
                "dragging a member outside its group ungroups it without splitting the remaining group");

            TgReset(pane, 6);
            tabs = pane.Tabs.ToArray();
            group = pane.CreateGroup(tabs[0])!;
            pane.AddToGroup(tabs[1], group);
            var otherGroup = pane.CreateGroup(tabs[3])!;
            pane.AddToGroup(tabs[4], otherGroup);
            TgInterceptDrag(form, pane, group, null, TgStripPoint(pane, tabs[4].Button, after: false));
            Check(pane.Tabs.SequenceEqual(new[] { tabs[2], tabs[0], tabs[1], tabs[3], tabs[4], tabs[5] })
                && pane.GroupMembers(otherGroup).SequenceEqual(new[] { tabs[3], tabs[4] })
                && pane.GroupMembers(group).SequenceEqual(new[] { tabs[0], tabs[1] }),
                "dropping a chip at another group's interior snaps to a boundary instead of nesting or splitting groups");
            TgContiguous(pane, "group-boundary chip drop");

            TgReset(pane, 4);
            tabs = pane.Tabs.ToArray();
            group = pane.CreateGroup(tabs[1])!;
            pane.AddToGroup(tabs[2], group);
            pane.SelectTab(0, focus: false);
            pane.SetGroupCollapsed(group, true);
            var chip = TgChip(pane, group);
            TgInterceptDrag(form, pane, null, tabs[3], TgStripPoint(pane, chip, after: false));
            Check(pane.Tabs.SequenceEqual(new[] { tabs[0], tabs[3], tabs[1], tabs[2] })
                && tabs[3].Group is null && group.Collapsed,
                "dropping before a collapsed chip places an ungrouped tab before all hidden members");
            TgInterceptDrag(form, pane, null, tabs[0], TgStripPoint(pane, chip, after: true));
            Check(pane.Tabs.SequenceEqual(new[] { tabs[3], tabs[1], tabs[2], tabs[0] })
                && tabs[0].Group is null && group.Collapsed,
                "dropping after a collapsed chip skips the entire hidden group rather than inserting inside it");
            TgInterceptDrag(form, pane, group, null, TgStripPoint(pane, tabs[3].Button, after: false));
            Check(pane.Tabs.SequenceEqual(new[] { tabs[1], tabs[2], tabs[3], tabs[0] }) && group.Collapsed,
                "a collapsed chip drag moves its entire block and retains collapse state");

            TgReset(pane, 1);
            var outsider = pane.ActiveTab!;
            group = pane.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full", TabGroupColor.Red)!;
            pane.SelectTab(1, focus: false);
            form.Refresh();
            var members = pane.GroupMembers(group);
            var visibleMember = members.Skip(1).First(tab => tab.Button.Available && tab.Button.Placement == ToolStripItemPlacement.Main);
            tabs = pane.Tabs.ToArray();
            TgInterceptDrag(form, pane, null, outsider, TgStripPoint(pane, visibleMember.Button, after: false), allowed: false);
            Check(pane.Tabs.SequenceEqual(tabs) && outsider.Group is null && pane.GroupMembers(group).Count == 12,
                "strip drag rejects a thirteenth group member without moving any tab");
            TgLimitStatus(pane, "thirteenth strip drag");

            TgReset(pane, 3);
            group = pane.CreateGroup(pane.Tabs[0])!;
            pane.AddToGroup(pane.Tabs[1], group);
            pane.SelectTab(1, focus: false);
            tabs = pane.Tabs.ToArray();
            var selected = pane.ActiveTab;
            var layout = form.CurrentLayout;
            files = pane.FileAreaOnScreen;
            TgInterceptDrag(form, pane, group, null, new Point(files.Right - 1, files.Top + files.Height / 2), cancel: true);
            Check(pane.Tabs.SequenceEqual(tabs) && pane.ActiveTab == selected && form.CurrentLayout == layout
                && pane.Groups.Single() == group,
                "Esc cancellation of chip drag leaves all group members, active tab and layout unchanged");
            TgInterceptDrag(form, pane, null, tabs[2], new Point(files.Right - 1, files.Top + files.Height / 2));
            Check(form.CurrentLayout.GetPanes().Length == 2 && pane.GroupMembers(group).SequenceEqual(tabs.Take(2))
                && form.GetPane(1).TabCount == 1 && form.GetPane(1).Groups.Count == 0,
                "existing single blank-tab drag still splits independently of neighboring groups");
            TgContiguous(pane, "all drag operations");
        }
        finally
        {
            blankTabDrop = null;
            transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
            TgResetForm(form);
        }
    }

    private static Point TgStripPoint(ExplorerPane pane, ToolStripItem item, bool after)
    {
        pane.TabStrip.PerformLayout();
        Rectangle bounds = item.Bounds;
        return pane.TabStrip.PointToScreen(new Point(after ? bounds.Right - 1 : bounds.Left + 1,
            bounds.Top + bounds.Height / 2));
    }

    private static void TgInterceptDrag(MainForm form, ExplorerPane pane, TabGroup? group, FolderTab? tab,
        Point point, bool allowed = true, bool cancel = false, string? markerCapture = null)
    {
        bool entered = false;
        blankTabDrop = (data, effects) =>
        {
            entered = true;
            var overlay = Application.OpenForms.OfType<TabDockOverlay>().Single();
            var args = new DragEventArgs((IDataObject)data, 0, point.X, point.Y, effects, DragDropEffects.None);
            foreach (string name in new[] { "OnDragEnter", "OnDragOver" })
                typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);
            Check(overlay.Visible && args.Effect == (allowed ? DragDropEffects.Move : DragDropEffects.None),
                "actual button drag resolves its destination through the visible overlay");
            if (markerCapture is not null)
            {
                double opacity = overlay.Opacity;
                // PrintWindow can return a black bitmap for layered windows on a private desktop.
                overlay.Opacity = 1;
                try
                {
                    using var image = PrintForm(overlay, 3);
                    Rectangle strip = pane.TabStrip.RectangleToScreen(pane.TabStrip.ClientRectangle);
                    strip.Offset(-overlay.Left, -overlay.Top);
                    TgSave(image, markerCapture);
                    Check(TgPixels(image, strip, Theme.Accent) >= Math.Max(4, strip.Height / 2),
                        "PrintWindow captures the accent insertion marker on the tab strip during the intercepted drag");
                }
                finally { overlay.Opacity = opacity; }
            }
            if (cancel)
            {
                var query = new QueryContinueDragEventArgs(0, true, DragAction.Continue);
                typeof(Control).GetMethod("OnQueryContinueDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [query]);
                Check(query.Action == DragAction.Cancel, "Esc cancels the real MainForm group drag source");
                return query.Action == DragAction.Cancel ? DragDropEffects.None : DragDropEffects.Move;
            }
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);
            return args.Effect;
        };
        try
        {
            if (group is not null)
            {
                var chip = TgChip(pane, group);
                ((Action)typeof(TabGroupChip).GetField("DragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chip)!)();
            }
            else
            {
                var button = tab!.Button;
                ((EventHandler)typeof(FolderTabButton).GetField("DragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(button, EventArgs.Empty);
            }
            Check(entered && !Application.OpenForms.OfType<TabDockOverlay>().Any(),
                "button drag completes through MainForm and disposes its overlay");
        }
        finally { blankTabDrop = null; }
    }
}
