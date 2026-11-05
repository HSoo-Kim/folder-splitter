using System.Reflection;
using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static void RunTabGroupRestartScenario(string root, string[] paths)
    {
        foreach (string mode in new[] { "only-group-exit", "ctrl-t-exit", "memory-auto-restart" })
        {
            string session = Path.Combine(root, "tabgroups-" + mode + ".json");
            SessionStore.Save(session, new SessionState
            {
                Language = "en", Layout = 1, GlobalHotkey = false, CheckUpdates = false,
                ExplorerFavoritesImported = true, Panes = [new PaneState { Tabs = paths.Take(2).ToList() }]
            });
            bool restartObserved = false;
            using (var form = new MainForm(session, []))
            {
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var pane = form.GetPane(0);
                        if (pane.ActiveView is { HasNavigated: false } view)
                            await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                        var group = pane.CreateGroup(pane.Tabs[0])!;
                        pane.AddToGroup(pane.Tabs[1], group);
                        pane.RenameGroup(group, "Restart collapsed");
                        pane.SetGroupColor(group, TabGroupColor.Purple);
                        pane.SetGroupCollapsed(group, true);
                        Check(group.Collapsed && pane.ActiveTab?.Group is null && pane.ActiveView is null,
                            mode + " fixture has a collapsed only group and selected unsaved blank tab");
                        if (mode == "ctrl-t-exit")
                        {
                            pane.FocusAddress();
                            TgKey(form, Keys.Control | Keys.T);
                            Check(pane.TabCount == 4 && pane.ActiveTab == pane.Tabs[^1] && group.Collapsed,
                                "Ctrl+T after a collapsed trailing group selects another ungrouped blank without expanding it");
                        }
                        if (mode == "memory-auto-restart")
                        {
                            // Deterministic lifecycle setup only: keys and menus never invoke private handlers.
                            // Trigger the actual timer callback after its real threshold and uptime gates.
                            form.SetAutoRestartMemory(100);
                            typeof(MainForm).GetField("startedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .SetValue(form, DateTime.UtcNow - TimeSpan.FromHours(1));
                            typeof(MainForm).GetField("memoryBaseline", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .SetValue(form, -200L * 1024 * 1024);
                            form.RestartLauncher = arguments =>
                            {
                                restartObserved = arguments.Contains("--session") && arguments.Contains(session)
                                    && arguments.Contains("--hidden") && arguments.Contains("--wait-pid");
                                var saved = SessionStore.Load(session);
                                Check(saved.Panes[0].Groups.Single().Collapsed,
                                    "memory restart launcher observes the real saved collapsed session before process handoff");
                                return true;
                            };
                            form.Hide();
                            typeof(MainForm).GetMethod("CheckMemory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                            Check(restartObserved, "actual automatic memory callback restarts through the saved-session launcher with hidden-state arguments");
                        }
                    }
                    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + mode + ": " + ex); }
                    finally { if (!form.IsDisposed) form.ExitApplication(); }
                };
                Application.Run(form);
            }
            var disk = SessionStore.Load(session);
            Check(disk.Panes[0].Tabs.Count == 2 && disk.Panes[0].Groups.Single().Collapsed,
                mode + " persists collapsed state even though the selected outside blank is omitted from disk");
            using var reopened = new MainForm(session, []);
            reopened.Shown += (_, _) =>
            {
                try
                {
                    var pane = reopened.GetPane(0);
                    var group = pane.Groups.Single();
                    Check(group.Collapsed && group.Name == "Restart collapsed" && group.Color == TabGroupColor.Purple
                        && pane.ActiveTab?.Group is null && pane.ActiveView is null && pane.TabCount == 3
                        && pane.GroupMembers(group).All(tab => !tab.Button.Available) && TgChip(pane, group).Available,
                        mode + " reopens MainForm with the group still collapsed and a visible outside blank selected");
                    TgContiguous(pane, mode + " reopened session");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + mode + " reopen: " + ex); }
                finally { reopened.ExitApplication(); }
            };
            Application.Run(reopened);
        }
    }

    private static void TgPolishDrags(MainForm form)
    {
        var transport = new HarmonyLib.Harmony("FolderSplitter.TabGroupPolish");
        var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
            [typeof(object), typeof(DragDropEffects)]);
        transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
        var source = form.GetPane(0);
        var target = form.GetPane(1);
        try
        {
            TgResetForm(form, 2);
            TgReset(source, 1);
            var group = source.AddGroup([null, null], "Single member", TabGroupColor.Cyan)!;
            var members = source.GroupMembers(group).ToArray();
            var move = TgTabMenu(source, members[0], "Move tab to pane");
            move.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pane 2").PerformClick();
            Check(source.GroupMembers(group).SequenceEqual(new[] { members[1] }) && TgChip(source, group).Available
                && target.TabCount == 1 && target.ActiveTab?.Group is null,
                "Move tab to pane moves only the chosen grouped member, shrinks its source chip and creates an ungrouped destination");
            var files = target.FileAreaOnScreen;
            TgInterceptDrag(form, source, null, members[1], new Point(files.Left + files.Width / 2, files.Top + files.Height / 2));
            Check(!source.Groups.Any() && !source.TabStrip.Items.OfType<TabGroupChip>().Any()
                && target.TabCount == 2 && target.Tabs.All(tab => tab.Group is null),
                "dragging the last grouped member into another pane removes the empty source chip and keeps the destination ungrouped");

            TgResetForm(form);
            TgReset(source, 1);
            group = source.AddGroup([null, null], "Split single", TabGroupColor.Orange)!;
            members = source.GroupMembers(group).ToArray();
            var split = TgTabMenu(source, members[0], "Split tab");
            split.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Right").PerformClick();
            Check(form.CurrentLayout.GetPanes().Length == 2 && source.GroupMembers(group).SequenceEqual(new[] { members[1] })
                && target.TabCount == 1 && target.ActiveTab?.Group is null,
                "Split tab menu splits a single member without copying its source group or moving its siblings");
            split = TgTabMenu(source, members[1], "Split tab");
            split.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Bottom").PerformClick();
            Check(form.CurrentLayout.GetPanes().Length == 3 && !source.Groups.Any()
                && !source.TabStrip.Items.OfType<TabGroupChip>().Any()
                && form.GetPane(2).TabCount == 1 && form.GetPane(2).ActiveTab?.Group is null,
                "splitting a group's final member removes its empty source chip and opens only an ungrouped destination tab");

            TgResetForm(form);
            TgReset(source, 5);
            FolderTab[] tabs = source.Tabs.ToArray();
            group = source.CreateGroup(tabs[1])!;
            source.AddToGroup(tabs[2], group);
            TgInterceptDrag(form, source, null, tabs[4], TgStripPoint(source, TgChip(source, group), after: true));
            Check(source.GroupMembers(group).SequenceEqual(new[] { tabs[4], tabs[1], tabs[2] }),
                "dropping on the expanded chip's right half joins as the first member");
            TgInterceptDrag(form, source, null, tabs[0], TgStripPoint(source, tabs[4].Button, after: false));
            Check(source.GroupMembers(group).SequenceEqual(new[] { tabs[0], tabs[4], tabs[1], tabs[2] }),
                "dropping on the first member's left half joins at group index zero rather than staying outside");
            TgContiguous(source, "first-member insertion drops");

            TgResetForm(form, 2);
            TgReset(source, 1);
            var sourceGroup = source.AddGroup([null, null], "Source identity", TabGroupColor.Blue)!;
            members = source.GroupMembers(sourceGroup).ToArray();
            TgReset(target, 3);
            var originalTargets = target.Tabs.ToArray();
            var targetGroup = target.CreateGroup(originalTargets[0])!;
            target.AddToGroup(originalTargets[1], targetGroup);
            TgInterceptDrag(form, source, null, members[0], TgStripPoint(target, TgChip(target, targetGroup), after: true));
            Check(source.GroupMembers(sourceGroup).SequenceEqual(new[] { members[1] }) && target.TabCount == 4
                && target.GroupMembers(targetGroup).Count == 3 && target.GroupMembers(targetGroup)[0] == target.ActiveTab
                && target.ActiveTab?.Group == targetGroup && targetGroup != sourceGroup
                && target.GroupMembers(targetGroup).Skip(1).SequenceEqual(originalTargets.Take(2)),
                "cross-pane strip drop joins the destination identity at its first-member insertion gap and leaves the source group contiguous");
            TgContiguous(source, "cross-pane source");
            TgContiguous(target, "cross-pane destination");

            TgReset(target, 0);
            targetGroup = target.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full target", TabGroupColor.Red)!;
            var beforeSource = source.Tabs.ToArray();
            var beforeTarget = target.Tabs.ToArray();
            TgInterceptDrag(form, source, null, members[1], TgStripPoint(target, TgChip(target, targetGroup), after: true), allowed: false);
            Check(source.Tabs.SequenceEqual(beforeSource) && target.Tabs.SequenceEqual(beforeTarget)
                && source.GroupMembers(sourceGroup).Count == 1 && target.GroupMembers(targetGroup).Count == 12,
                "cross-pane first-member insertion rejects a full group atomically without losing the source's last grouped member");
            TgReset(target, 32);
            beforeTarget = target.Tabs.ToArray();
            TgInterceptDrag(form, source, null, members[1], TgStripPoint(target, target.Tabs[0].Button, after: false), allowed: false);
            Check(source.Tabs.SequenceEqual(beforeSource) && target.Tabs.SequenceEqual(beforeTarget),
                "cross-pane insertion rejects a full 32-tab destination before removing the source member");

            TgResetForm(form);
            TgReset(source, 24);
            group = source.CreateGroup(source.Tabs[0])!;
            source.RenameGroup(group, "Overflow marker");
            form.Size = new Size(900, 600);
            source.SelectTab(0, focus: false);
            form.Refresh();
            tabs = source.Tabs.ToArray();
            var visible = source.Tabs.Where(tab => tab.Button.Available && tab.Button.Placement == ToolStripItemPlacement.Main).ToArray();
            var last = visible[^1];
            int insertion = source.IndexOfTab(last) + 1;
            var dragged = visible[0];
            var plus = source.TabStrip.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Pane 1 new tab");
            Point gap = source.TabStrip.PointToScreen(new Point(Math.Min(plus.Bounds.Left - 1, last.Button.Bounds.Right + 2),
                last.Button.Bounds.Top + last.Button.Height / 2));
            Check(visible.Length < source.TabCount && insertion < source.TabCount,
                "overflow marker fixture has hidden tabs after its final visible member");
            TgInterceptDrag(form, source, null, dragged, gap,
                markerCapture: Path.Combine(TgPolishOutput(), "overflow-visible-gap-insertion-marker.png"));
            var expected = tabs.Where(tab => tab != dragged).ToList();
            expected.Insert(insertion - (Array.IndexOf(tabs, dragged) < insertion ? 1 : 0), dragged);
            Check(source.Tabs.SequenceEqual(expected) && source.IndexOfTab(dragged) < source.TabCount - 1,
                "drop past the last visible tab lands at the captured marker before hidden overflow tabs, not at the pane's end");
        }
        finally
        {
            blankTabDrop = null;
            transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
            form.Size = new Size(1400, 900);
            TgResetForm(form);
        }
    }
}
