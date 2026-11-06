using System.Reflection;
using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static System.Windows.Forms.Timer TgJoinTimer(ExplorerPane pane) =>
        (System.Windows.Forms.Timer)typeof(ExplorerPane).GetField("groupJoinTimer",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!;

    private static TabDropPlan TgDwellPlan(TabDockOverlay overlay) =>
        (TabDropPlan)typeof(TabDockOverlay).GetField("preview",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;

    private static void TgDragEvent(TabDockOverlay overlay, string name, EventArgs args) =>
        typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);

    private static Point TgJoinPoint(ExplorerPane pane, TabGroup group) => group.Collapsed
        ? TgStripPoint(pane, TgChip(pane, group), after: true)
        : TgStripPoint(pane, pane.GroupMembers(group).Last().Button, after: true);

    private static void TgAssertDwellPreview(ExplorerPane pane, TabGroup group, TabDockOverlay overlay,
        bool armed, string scenario)
    {
        var plan = TgDwellPlan(overlay);
        Check(plan is { Allowed: true, InsertionIndex: not null }
            && plan.JoinGroup == (armed ? group : null)
            && plan.MarkerColor.ToArgb() == (armed ? Theme.GroupColor(group.Color) : Theme.Accent).ToArgb(),
            scenario + (armed ? " timer refreshes the real overlay to a group-colored join marker"
                : " starts outside the group with an accent insertion marker"));
        Check(TgChip(pane, group).DropJoinArmed == (armed && group.Collapsed),
            scenario + " only tints a collapsed chip while its join is armed");
        Check(TgJoinTimer(pane).Interval == 500 && (armed || TgJoinTimer(pane).Enabled),
            scenario + " uses the native 500 ms group-join dwell timer");
    }

    private static void TgArmJoin(ExplorerPane pane, TabGroup group, TabDockOverlay overlay, Point point, string scenario)
    {
        Cursor.Position = point;
        TgAssertDwellPreview(pane, group, overlay, armed: false, scenario);
        OitFireTimer(TgJoinTimer(pane));
        // No synthetic DragOver here: the timer itself must refresh the live overlay.
        TgAssertDwellPreview(pane, group, overlay, armed: true, scenario);
        Check(TgDwellPlan(overlay).InsertionIndex == pane.IndexOfTab(pane.GroupMembers(group).Last()) + 1,
            scenario + " arms append after the last existing group member");
    }

    private static void TgAssertDwellResult(ExplorerPane target, TabGroup group, FolderTab[] oldMembers,
        FolderTab[] received, bool dwell, bool collapsed, string scenario)
    {
        Check(target.GroupMembers(group).SequenceEqual(dwell ? oldMembers.Concat(received) : oldMembers)
            && received.All(tab => tab.Group == (dwell ? group : null))
            && group.Collapsed == collapsed,
            scenario + (dwell ? " appends the complete payload in order without expanding a collapsed group"
                : " drops the complete payload outside the group before dwell"));
        Check(target.IndexOfTab(received[0]) == target.IndexOfTab(oldMembers.Last()) + 1
            && received.Select(target.IndexOfTab).SequenceEqual(Enumerable.Range(target.IndexOfTab(received[0]), received.Length)),
            scenario + " preserves the actual insertion boundary and payload order");
        Check(!TgJoinTimer(target).Enabled && !TgChip(target, group).DropJoinArmed,
            scenario + " clears the timer and collapsed-chip tint after drop");
        if (collapsed)
            Check(TgChip(target, group).Text!.Contains(target.GroupMembers(group).Count.ToString())
                && target.GroupMembers(group).All(tab => !tab.Button.Available),
                scenario + " keeps the collapsed count current and every joined member hidden");
        TgContiguous(target, scenario);
    }

    private static void TgInternalGroupJoinDwell(MainForm form, string output)
    {
        foreach (bool otherPane in new[] { false, true })
        foreach (string kind in new[] { "tab", "selection" })
        foreach (bool collapsed in new[] { false, true })
        foreach (bool dwell in new[] { false, true })
        {
            TgResetForm(form, otherPane ? 2 : 1);
            var source = form.GetPane(0);
            var target = form.GetPane(otherPane ? 1 : 0);
            TgReset(source, otherPane ? 3 : 6);
            if (otherPane) TgReset(target, 3);
            var group = target.CreateGroup(target.Tabs[1])!;
            target.AddToGroup(target.Tabs[2], group);
            target.SetGroupColor(group, TabGroupColor.Purple);
            var oldMembers = target.GroupMembers(group).ToArray();
            FolderTab[] payload = kind == "selection"
                ? [source.Tabs[0], source.Tabs[otherPane ? 2 : 4]] : [source.Tabs[0]];
            if (kind == "selection") MdSelect(source, payload);
            else source.SelectTab(source.IndexOfTab(payload[0]), focus: false);
            if (otherPane) target.SelectTab(0, focus: false);
            target.SetGroupCollapsed(group, collapsed);
            var before = target.Tabs.ToArray();
            Point point = TgJoinPoint(target, group);
            string scenario = $"{(otherPane ? "other-pane" : "same-strip")} {kind} {(collapsed ? "collapsed" : "end")} {(dwell ? "armed" : "immediate")}";
            TgInterceptDrag(form, source, null, payload[0], point, hover: (overlay, _) =>
            {
                Cursor.Position = point;
                if (dwell) TgArmJoin(target, group, overlay, point, scenario);
                else TgAssertDwellPreview(target, group, overlay, armed: false, scenario);
            });
            var received = otherPane ? target.Tabs.Except(before).ToArray() : payload;
            Check(received.Length == payload.Length && (!otherPane || payload.All(tab => source.IndexOfTab(tab) < 0)),
                scenario + " transports every selected source tab through the real drag source");
            TgAssertDwellResult(target, group, oldMembers, received, dwell, collapsed, scenario);
        }
        TgDwellResetChecks(form);
        TgDwellGroupPayloadChecks(form);
        TgDwellMarkerPixels(form, output);
    }

    private static void TgDwellResetChecks(MainForm form)
    {
        foreach (bool otherPane in new[] { false, true })
        foreach (bool collapsed in new[] { false, true })
        {
            TgResetForm(form, otherPane ? 2 : 1);
            var source = form.GetPane(0);
            var target = form.GetPane(otherPane ? 1 : 0);
            TgReset(source, 4);
            if (otherPane) TgReset(target, 3);
            var group = target.CreateGroup(target.Tabs[1])!;
            target.AddToGroup(target.Tabs[2], group);
            source.SelectTab(0, focus: false);
            if (otherPane) target.SelectTab(0, focus: false);
            target.SetGroupCollapsed(group, collapsed);
            var sourceBefore = source.Tabs.ToArray();
            var targetBefore = target.Tabs.ToArray();
            Point point = TgJoinPoint(target, group);
            string scenario = $"{(otherPane ? "other-pane" : "same-strip")} {(collapsed ? "collapsed" : "end")} reset";
            TgInterceptDrag(form, source, null, source.Tabs[0], point, cancel: true, hover: (overlay, args) =>
            {
                Point beforeChip = TgStripPoint(target, TgChip(target, group), after: false);
                var beforeArgs = new DragEventArgs(args.Data, args.KeyState, beforeChip.X, beforeChip.Y,
                    args.AllowedEffect, DragDropEffects.None);
                TgDragEvent(overlay, "OnDragOver", beforeArgs);
                Check(TgDwellPlan(overlay) is { Allowed: true, JoinGroup: null }
                    && TgDwellPlan(overlay).InsertionIndex == target.IndexOfTab(target.GroupMembers(group).First())
                    && !TgJoinTimer(target).Enabled && !TgChip(target, group).DropJoinArmed,
                    scenario + " moving to the chip's left half preserves before-group insertion and cancels pending dwell");
                TgDragEvent(overlay, "OnDragOver", args);
                TgArmJoin(target, group, overlay, point, scenario);
                TgDragEvent(overlay, "OnDragLeave", EventArgs.Empty);
                Check(!TgJoinTimer(target).Enabled && !TgChip(target, group).DropJoinArmed,
                    scenario + " leaving the real overlay clears its armed timer and tint");
                TgDragEvent(overlay, "OnDragEnter", args);
                TgAssertDwellPreview(target, group, overlay, armed: false, scenario + " reentry");
                OitFireTimer(TgJoinTimer(target));
                TgAssertDwellPreview(target, group, overlay, armed: true, scenario + " rearmed before Escape");
            });
            Check(source.Tabs.SequenceEqual(sourceBefore) && target.Tabs.SequenceEqual(targetBefore)
                && !TgJoinTimer(target).Enabled && !TgChip(target, group).DropJoinArmed,
                scenario + " Escape cancels an armed join without moving tabs or retaining tint");
            TgInterceptDrag(form, source, null, source.Tabs[0], point, cancel: true,
                hover: (overlay, _) => TgAssertDwellPreview(target, group, overlay, armed: false, scenario + " next drag"));
        }
    }

    private static void TgDwellGroupPayloadChecks(MainForm form)
    {
        TgResetForm(form);
        var pane = form.GetPane(0);
        TgReset(pane, 4);
        var first = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], first);
        var second = pane.CreateGroup(pane.Tabs[2])!;
        pane.AddToGroup(pane.Tabs[3], second);
        pane.SelectTab(0, focus: false);
        pane.SetGroupCollapsed(second, true);
        TgInterceptDrag(form, pane, first, null, TgJoinPoint(pane, second), hover: (overlay, _) =>
        {
            Check(TgDwellPlan(overlay) is { Allowed: true, JoinGroup: null }
                && !TgJoinTimer(pane).Enabled && !TgChip(pane, second).DropJoinArmed,
                "whole-group payload never arms a join over another collapsed chip");
        });
        Check(pane.GroupMembers(first).Count == 2 && pane.GroupMembers(second).Count == 2 && second.Collapsed,
            "whole-group drop remains an atomic boundary move without nesting groups");

        foreach (bool otherPane in new[] { false, true })
        foreach (string kind in new[] { "tab", "selection" })
        {
            TgResetForm(form, otherPane ? 2 : 1);
            pane = form.GetPane(0);
            var target = form.GetPane(otherPane ? 1 : 0);
            TgReset(pane, 3);
            if (otherPane) TgReset(target, 1);
            FolderTab[] payload = kind == "selection" ? pane.Tabs.Take(2).ToArray() : [pane.Tabs[0]];
            var full = target.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full dwell", TabGroupColor.Red)!;
            target.SelectTab(0, focus: false);
            target.SetGroupCollapsed(full, true);
            MdSelect(pane, payload);
            var before = target.Tabs.ToArray();
            TgInterceptDrag(form, pane, null, payload[0], TgJoinPoint(target, full), hover: (overlay, _) =>
            {
                Check(TgDwellPlan(overlay) is { Allowed: true, JoinGroup: null }
                    && TgDwellPlan(overlay).MarkerColor.ToArgb() == Theme.Accent.ToArgb()
                    && !TgJoinTimer(target).Enabled && !TgChip(target, full).DropJoinArmed,
                    $"{(otherPane ? "other-pane" : "same-strip")} {kind} cannot arm a full collapsed group but may insert outside it");
            });
            var received = otherPane ? target.Tabs.Except(before).ToArray() : payload;
            Check(target.GroupMembers(full).Count == 12 && received.Length == payload.Length
                && received.All(tab => tab.Group is null),
                $"{(otherPane ? "other-pane" : "same-strip")} {kind} full-group boundary drop leaves group capacity unchanged");
        }
    }

    private static void TgDwellMarkerPixels(MainForm form, string output)
    {
        try
        {
            foreach (string mode in new[] { "light", "dark", "custom-light", "custom-dark" })
            foreach (bool collapsed in new[] { false, true })
            {
                TgResetForm(form);
                if (mode == "custom-light") form.SetCustomColors(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
                else if (mode == "custom-dark") form.SetCustomColors(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
                else form.SetColorTheme(mode);
                var pane = form.GetPane(0);
                TgReset(pane, 4);
                var group = pane.CreateGroup(pane.Tabs[1])!;
                pane.AddToGroup(pane.Tabs[2], group);
                pane.SetGroupColor(group, TabGroupColor.Purple);
                pane.SelectTab(0, focus: false);
                pane.SetGroupCollapsed(group, collapsed);
                Point point = TgJoinPoint(pane, group);
                string name = $"tab-groups-dwell-{mode}-{(collapsed ? "collapsed" : "end")}";
                TgInterceptDrag(form, pane, null, pane.Tabs[0], point, cancel: true, hover: (overlay, _) =>
                {
                    Cursor.Position = point;
                    TgDwellMarkerCapture(overlay, Theme.Accent, Path.Combine(output, name + "-before.png"));
                    using var before = TgPrintStrip(pane.TabStrip);
                    TgArmJoin(pane, group, overlay, point, name);
                    TgDwellMarkerCapture(overlay, Theme.GroupColor(group.Color), Path.Combine(output, name + "-armed-marker.png"));
                    using var after = TgPrintStrip(pane.TabStrip);
                    TgSave(after, Path.Combine(output, name + "-armed-strip.png"));
                    if (collapsed)
                    {
                        Rectangle chip = TgChip(pane, group).Bounds;
                        using var beforeChip = before.Clone(chip, before.PixelFormat);
                        using var afterChip = after.Clone(chip, after.PixelFormat);
                        Check(OitPixelDifference(beforeChip, afterChip) > chip.Height,
                            name + " native strip capture visibly changes the collapsed chip tint while armed");
                    }
                });

                // The existing immediate chip-interior rule remains group-colored without dwell.
                if (!collapsed)
                    TgInterceptDrag(form, pane, null, pane.Tabs[0], TgStripPoint(pane, TgChip(pane, group), after: true),
                        cancel: true, hover: (overlay, _) =>
                        {
                            Check(TgDwellPlan(overlay).JoinGroup == group && !TgJoinTimer(pane).Enabled,
                                mode + " expanded chip interior still joins immediately without arming");
                            TgDwellMarkerCapture(overlay, Theme.GroupColor(group.Color),
                                Path.Combine(output, name + "-immediate-interior.png"));
                        });
                if (!collapsed)
                {
                    TgReset(pane, 1);
                    var outsider = pane.Tabs[0];
                    var full = pane.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full marker", TabGroupColor.Red)!;
                    pane.SelectTab(1, focus: false);
                    form.Refresh();
                    var member = pane.GroupMembers(full).Skip(1)
                        .First(tab => tab.Button.Available && tab.Button.Placement == ToolStripItemPlacement.Main);
                    TgInterceptDrag(form, pane, null, outsider, TgStripPoint(pane, member.Button, after: false),
                        allowed: false, cancel: true, hover: (overlay, _) =>
                        {
                            Check(!TgJoinTimer(pane).Enabled,
                                mode + " rejected immediate group interior does not arm a dwell timer");
                            TgDwellMarkerCapture(overlay, Color.FromArgb(160, 55, 45),
                                Path.Combine(output, name + "-disallowed.png"));
                        });
                }
            }
        }
        finally { form.SetColorTheme("light"); }
    }

    private static void TgDwellMarkerCapture(TabDockOverlay overlay, Color expected, string path)
    {
        var plan = TgDwellPlan(overlay);
        using var image = new Bitmap(overlay.ClientSize.Width, overlay.ClientSize.Height);
        overlay.DrawToBitmap(image, overlay.ClientRectangle);
        Rectangle marker = plan.Preview;
        marker.Offset(-overlay.Left, -overlay.Top);
        Check(plan.MarkerColor.ToArgb() == expected.ToArgb()
            && TgPixels(image, marker, expected) >= Math.Max(4, marker.Height),
            Path.GetFileNameWithoutExtension(path) + " real overlay paints the expected insertion marker pixels");
        TgSave(image, path);
    }

    private static void CdGroupJoinDwell(FolderSplitterApplicationContext context, MainForm first, MainForm second, string[] paths)
    {
        foreach (string kind in new[] { "tab", "selection" })
        foreach (bool collapsed in new[] { false, true })
        foreach (bool dwell in new[] { false, true })
        {
            CdReset(first, paths);
            CdReset(second, [paths[5], paths[6], paths[7]]);
            var source = first.GetPane(0);
            var target = second.GetPane(0);
            var group = target.CreateGroup(target.Tabs[1])!;
            target.AddToGroup(target.Tabs[2], group);
            target.SetGroupColor(group, TabGroupColor.Cyan);
            target.SelectTab(0, focus: false);
            target.SetGroupCollapsed(group, collapsed);
            var oldMembers = target.GroupMembers(group).ToArray();
            var before = target.Tabs.ToArray();
            var payload = CdPayload(source, kind);
            var movedPaths = payload.members.Select(tab => tab.Host?.LocationPath).ToArray();
            Point point = TgJoinPoint(target, group);
            string scenario = $"cross-window {kind} {(collapsed ? "collapsed" : "end")} {(dwell ? "armed" : "immediate")}";
            CdDrag(context, first, source, payload.tab, null, second, point, hover: (overlay, _) =>
            {
                Cursor.Position = point;
                if (dwell) TgArmJoin(target, group, overlay, point, scenario);
                else TgAssertDwellPreview(target, group, overlay, armed: false, scenario);
            });
            var received = target.Tabs.Except(before).ToArray();
            Check(received.Select(tab => tab.Host?.LocationPath).SequenceEqual(movedPaths)
                && payload.members.All(tab => source.IndexOfTab(tab) < 0),
                scenario + " transfers the complete ordered path payload through both real window overlays");
            TgAssertDwellResult(target, group, oldMembers, received, dwell, collapsed, scenario);
        }
        foreach (string kind in new[] { "tab", "selection" })
        foreach (bool collapsed in new[] { false, true })
        {
            CdReset(first, paths);
            CdReset(second, [paths[5], paths[6], paths[7]]);
            var source = first.GetPane(0);
            var target = second.GetPane(0);
            var group = target.CreateGroup(target.Tabs[1])!;
            target.AddToGroup(target.Tabs[2], group);
            target.SelectTab(0, focus: false);
            target.SetGroupCollapsed(group, collapsed);
            var payload = CdPayload(source, kind);
            var sourceBefore = source.Tabs.ToArray();
            var targetBefore = target.Tabs.ToArray();
            Point point = TgJoinPoint(target, group);
            string scenario = $"cross-window {kind} {(collapsed ? "collapsed" : "end")} reset";
            CdDrag(context, first, source, payload.tab, null, second, point, escape: true, hover: (overlay, args) =>
            {
                TgArmJoin(target, group, overlay, point, scenario);
                TgDragEvent(overlay, "OnDragLeave", EventArgs.Empty);
                Check(!TgJoinTimer(target).Enabled && !TgChip(target, group).DropJoinArmed,
                    scenario + " leaving the destination overlay clears dwell and tint");
                TgDragEvent(overlay, "OnDragEnter", args);
                TgAssertDwellPreview(target, group, overlay, armed: false, scenario + " reentry");
                OitFireTimer(TgJoinTimer(target));
                TgAssertDwellPreview(target, group, overlay, armed: true, scenario + " before Escape");
            });
            Check(source.Tabs.SequenceEqual(sourceBefore) && target.Tabs.SequenceEqual(targetBefore)
                && !TgJoinTimer(target).Enabled && !TgChip(target, group).DropJoinArmed,
                scenario + " Escape leaves both windows intact and clears the armed destination");
            CdDrag(context, first, source, payload.tab, null, second, point, escape: true,
                hover: (overlay, _) => TgAssertDwellPreview(target, group, overlay, armed: false, scenario + " next drag"));
        }
        foreach (string kind in new[] { "tab", "selection" })
        {
            CdReset(first, paths);
            CdReset(second, [paths[6]]);
            var source = first.GetPane(0);
            var target = second.GetPane(0);
            var full = target.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full receiver", TabGroupColor.Red)!;
            target.SelectTab(0, focus: false);
            target.SetGroupCollapsed(full, true);
            var payload = CdPayload(source, kind);
            var before = target.Tabs.ToArray();
            CdDrag(context, first, source, payload.tab, null, second, TgJoinPoint(target, full),
                hover: (overlay, _) =>
                {
                    Check(TgDwellPlan(overlay) is { Allowed: true, JoinGroup: null }
                        && !TgJoinTimer(target).Enabled && !TgChip(target, full).DropJoinArmed,
                        "cross-window " + kind + " cannot arm a full collapsed group");
                });
            var received = target.Tabs.Except(before).ToArray();
            Check(received.Length == payload.members.Length && received.All(tab => tab.Group is null)
                && target.GroupMembers(full).Count == 12,
                "cross-window " + kind + " full-group boundary accepts the payload only outside the group");
        }
    }
}
