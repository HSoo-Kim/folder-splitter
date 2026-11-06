using QuadExplorer;
using System.Collections;
using System.Reflection;
using System.Text.Json;

internal static partial class Program
{
    // Only thread-local WinForms dispatch is used. The entry point has already rejected
    // execution outside PrivateDesktopQa; no SendInput or input-desktop switching occurs.
    private static void GcAnswerDialog(Action command, Action<Form> answer)
    {
        int found = GcObserveDialogs(command, answer);
        if (found != 1) throw new InvalidOperationException($"Expected one group confirmation, observed {found}.");
    }

    private static int GcObserveDialogs(Action command, Action<Form> answer)
    {
        int found = 0;
        Exception? error = null;
        var answered = new HashSet<Form>();
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            foreach (var dialog in Application.OpenForms.Cast<Form>().Where(form =>
                form.GetType().Name == "GroupCloseDialog" && form.Visible).ToArray())
            {
                if (!answered.Add(dialog)) continue;
                found++;
                try { answer(dialog); }
                catch (Exception exception) { error ??= exception; }
                finally
                {
                    // A failed assertion/answer must not strand the suite in a nested modal loop.
                    if (!dialog.IsDisposed && dialog.Visible) dialog.Close();
                }
            }
        };
        timer.Start();
        try { command(); }
        finally { timer.Stop(); }
        if (answered.Any(dialog => !dialog.IsDisposed && dialog.Visible))
            throw new InvalidOperationException("Group confirmation remained visible after its modal command returned.");
        if (error is not null) throw new InvalidOperationException("Group confirmation modal handler failed.", error);
        return found;
    }

    private static ToolStripMenuItem GcSetting(MainForm window)
    {
        var settings = window.Controls.OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>())
            .Single(item => item.AccessibleName == "Settings");
        return settings.DropDownItems.OfType<ToolStripMenuItem>().Single(item =>
            item.Text == new UiText(window.Language).T("Confirm before closing a group"));
    }

    private static ContextMenuStrip GcEditor(ExplorerPane pane, TabGroup group)
    {
        TgRightClick(pane, TgChip(pane, group));
        return TgVisibleMenu(new UiText(((MainForm)pane.FindForm()!).Language).T("Close group"));
    }

    private static void GcEditorCommand(ExplorerPane pane, TabGroup group, string key, string? pendingName = null)
    {
        var menu = GcEditor(pane, group);
        if (pendingName is not null) menu.Items.OfType<ToolStripTextBox>().Single().Text = pendingName;
        try
        {
            menu.Items.OfType<ToolStripMenuItem>().Single(item =>
                item.Text == new UiText(((MainForm)pane.FindForm()!).Language).T(key)).PerformClick();
        }
        finally { if (!menu.IsDisposed) menu.Close(); }
    }

    private static void GcShortcut(MainForm window, Keys keys)
    {
        MultiWindowActivated(window);
        window.GetPane(window.CurrentLayout.GetPanes()[0]).FocusAddress();
        TgKey(window, keys);
    }

    private static object[] GcHistory(object owner, string field) => ((IEnumerable)owner.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!).Cast<object>().ToArray();

    private static Action GcUnchanged(MainForm window, ExplorerPane pane, TabGroup group, string reason)
    {
        var tabs = pane.Tabs.ToArray();
        var members = pane.GroupMembers(group).ToArray();
        var groups = pane.Groups.ToArray();
        var selected = pane.SelectedTabs.ToArray();
        var active = pane.ActiveTab;
        string state = JsonSerializer.Serialize(pane.CaptureState());
        string saved = JsonSerializer.Serialize(window.ApplicationContext.SavedGroups);
        bool preference = window.ApplicationContext.State.ConfirmCloseGroup;
        var local = GcHistory(pane, "closedTabs");
        var global = GcHistory(window, "closedItems");
        var redo = GcHistory(window, "reopenedItems");
        return () =>
        {
            Check(pane.Tabs.SequenceEqual(tabs) && pane.Groups.SequenceEqual(groups)
                && pane.GroupMembers(group).SequenceEqual(members) && pane.ActiveTab == active
                && pane.SelectedTabs.SequenceEqual(selected) && JsonSerializer.Serialize(pane.CaptureState()) == state,
                reason + " preserves member identities, order, group metadata, name and selection");
            Check(GcHistory(pane, "closedTabs").SequenceEqual(local)
                && GcHistory(window, "closedItems").SequenceEqual(global)
                && GcHistory(window, "reopenedItems").SequenceEqual(redo),
                reason + " leaves local/global undo and redo records unchanged");
            Check(window.ApplicationContext.State.ConfirmCloseGroup == preference
                && JsonSerializer.Serialize(window.ApplicationContext.SavedGroups) == saved,
                reason + " changes neither preference nor saved catalog");
        };
    }

    private static void GcCheckDialog(Form dialog, MainForm owner, TabGroup group, int count,
        bool deleteSaved, string? capture, string label)
    {
        var text = new UiText(owner.Language);
        var controls = Descendants(dialog).ToArray();
        Control Named(string name) => controls.Single(control => control.Name == name);
        string message = deleteSaved
            ? text.T("Delete the saved group '{0}'? Its {1} open tabs will also close.", group.Name, count)
            : string.IsNullOrWhiteSpace(group.Name) ? text.T("Close {0} tabs in this group?", count)
            : text.T("Close {1} tabs in the '{0}' group?", group.Name, count);
        var accept = (Button)Named("GroupCloseAccept");
        var cancel = (Button)Named("GroupCloseCancel");
        Check(Application.OpenForms.Cast<Form>().Count(form => form.GetType().Name == "GroupCloseDialog") == 1
            && dialog.Name == "GroupCloseConfirmation" && dialog.AccessibleName == dialog.Text
            && dialog.AccessibilityObject.Name == dialog.Text,
            label + " exposes exactly one real accessible confirmation form");
        Check(dialog.Modal && dialog.Owner == owner && !GcIsWindowEnabled(owner.Handle) && !dialog.ShowInTaskbar && !dialog.TopMost
            && dialog.StartPosition == FormStartPosition.CenterParent && !dialog.MinimizeBox && !dialog.MaximizeBox,
            label + " is modal to the invoking window rather than a detached or topmost prompt");
        Check(dialog.Text == text.T(deleteSaved ? "Delete group" : "Close group")
            && Named("GroupCloseMessage").Text == message
            && accept.Text == text.T(deleteSaved ? "Delete" : "Close") && cancel.Text == text.T("Cancel"),
            label + " localizes title, exact original group name/member count and both actions");
        Check(controls.Where(control => control.Name.StartsWith("GroupClose", StringComparison.Ordinal))
                .All(control => control.AccessibleName == control.Text && control.AccessibilityObject.Name == control.Text),
            label + " gives every named control its localized text as its accessible name");
        Check(dialog.AcceptButton == accept && dialog.CancelButton == cancel && accept.DialogResult == DialogResult.OK
            && cancel.DialogResult == DialogResult.Cancel && accept.Enabled && cancel.Enabled && accept.TabStop && cancel.TabStop,
            label + " wires default Enter and Escape buttons with keyboard-accessible actions");
        var checkboxes = controls.OfType<CheckBox>().ToArray();
        var hints = controls.Where(control => control.Name == "GroupCloseUndoHint").ToArray();
        Check(deleteSaved ? checkboxes.Length == 0 && hints.Length == 0
            : checkboxes.Length == 1 && checkboxes[0].Name == "GroupCloseDontAskAgain" && !checkboxes[0].Checked
                && checkboxes[0].Text == text.T("Don't ask again") && checkboxes[0].TabStop
                && hints.Length == 1 && hints[0].Text == text.T("You can reopen them with Ctrl+Shift+T after closing."),
            label + (deleteSaved ? " never offers preference suppression or a misleading delete-undo hint"
                : " explains one-step tab undo and starts its opt-out unchecked"));
        int tolerance = (int)Math.Ceiling(4 * dialog.DeviceDpi / 96d);
        var work = Screen.FromControl(owner).WorkingArea;
        int centerX = Math.Clamp(owner.Left + (owner.Width - dialog.Width) / 2, work.Left, Math.Max(work.Left, work.Right - dialog.Width));
        int centerY = Math.Clamp(owner.Top + (owner.Height - dialog.Height) / 2, work.Top, Math.Max(work.Top, work.Bottom - dialog.Height));
        Check(Math.Abs(dialog.Left - centerX) <= tolerance && Math.Abs(dialog.Top - centerY) <= tolerance
            && work.Contains(dialog.Bounds), label + " centers over its owner and fits the monitor work area");
        Check(dialog.DeviceDpi == owner.DeviceDpi && MultiWindowNativeDpi(dialog.Handle) == (uint)owner.DeviceDpi
            && controls.All(control => control.DeviceDpi == dialog.DeviceDpi),
            label + " uses the owner's actual native DPI throughout its control tree");
        Check(accept.Width >= (int)Math.Round(90 * dialog.DeviceDpi / 96d)
            && cancel.Width >= (int)Math.Round(90 * dialog.DeviceDpi / 96d)
            && accept.Height >= (int)Math.Round(30 * dialog.DeviceDpi / 96d),
            label + " scales action hit targets from 96-DPI logical dimensions");
        Check(dialog.BackColor == Theme.Canvas && accept.ForeColor == Theme.Text && cancel.ForeColor == Theme.Text,
            label + " uses the current light/dark application palette");
        var visible = controls.Where(control => control.Name.StartsWith("GroupClose", StringComparison.Ordinal)).ToArray();
        Check(visible.All(control => control.Visible && control.Width > 0 && control.Height > 0
            && dialog.ClientRectangle.Contains(dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))))
            && controls.OfType<Label>().All(control => control.Height >= control.GetPreferredSize(new Size(control.Width, 0)).Height)
            && !accept.Bounds.IntersectsWith(cancel.Bounds)
            && accept.Width >= TextRenderer.MeasureText(accept.Text, accept.Font).Width
            && cancel.Width >= TextRenderer.MeasureText(cancel.Text, cancel.Font).Width,
            label + " fits all wrapped copy and nonoverlapping action labels without clipping");
        if (capture is not null)
        {
            dialog.Refresh();
            using var image = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
            image.Save(capture);
            Console.WriteLine($"SCREENSHOT {capture} dpi={dialog.DeviceDpi} owner={owner.Handle} bounds={dialog.Bounds}");
        }
    }

    private static void GcAnswer(Form dialog, string action, bool dontAsk = false)
    {
        if (dontAsk)
        {
            var checkbox = Descendants(dialog).OfType<CheckBox>().Single(control => control.Name == "GroupCloseDontAskAgain");
            checkbox.Focus();
            TgKey(checkbox, Keys.Space);
            SendMessage(checkbox.Handle, 0x101, (nint)Keys.Space, 0); // WM_KEYUP completes the native checkbox click.
            Check(checkbox.Checked, "Space toggles the actual Don't ask again checkbox");
        }
        switch (action)
        {
            case "escape":
                Descendants(dialog).OfType<Button>().Single(button => button.Name == "GroupCloseCancel").Focus();
                TgKey(dialog, Keys.Escape);
                break;
            case "enter":
                Descendants(dialog).OfType<Button>().Single(button => button.Name == "GroupCloseAccept").Focus();
                TgKey(dialog, Keys.Enter);
                break;
            case "close-box": SendMessage(dialog.Handle, 0x112, 0xF060, 0); break; // WM_SYSCOMMAND / SC_CLOSE
            default:
                Descendants(dialog).OfType<Button>().Single(button => button.Name ==
                    (action == "accept" ? "GroupCloseAccept" : "GroupCloseCancel")).PerformClick();
                break;
        }
        Check(dialog.DialogResult == (action is "enter" or "accept" ? DialogResult.OK : DialogResult.Cancel),
            "real dialog " + action + " returns the expected modal result (actual " + dialog.DialogResult + ")");
    }

    private static void GcWithoutPrompt(Action command, string reason)
    {
        int observed = GcObserveDialogs(command, dialog => GcAnswer(dialog, "cancel"));
        Check(observed == 0, reason + " does not show a group-close confirmation");
    }

    private static void RunGroupCloseConfirmScenario(string root, string? output)
    {
        if (output is not null) Directory.CreateDirectory(output);
        string fixture = Directory.CreateDirectory(Path.Combine(root, "GroupCloseConfirm")).FullName;
        string[] paths = Enumerable.Range(0, 8).Select(index => Directory.CreateDirectory(Path.Combine(fixture, "Folder-" + index)).FullName).ToArray();
        string session = Path.Combine(fixture, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ColorTheme = "light", ExplorerFavoritesImported = true,
            GlobalHotkey = false, StartWithWindows = false, CheckUpdates = false, AutoRestartMemory = 0, KeepInTray = false,
            Windows = Enumerable.Range(0, 2).Select(index => new BrowserWindowState
            {
                WindowWidth = 1100, WindowHeight = 760, ShowFolderTree = false,
                Panes = [new() { Tabs = paths.Skip(index * 3).Take(3).ToList(),
                    Groups = [new() { Name = index == 0 ? "Research 연구 日本語" : "Peer", Color = "Blue", Count = 2 }] }]
            }).ToList()
        });
        using (var context = MultiWindowContext(session))
        RunMultiWindowContext(context, async () =>
        {
            foreach (var window in context.Windows) await MultiWindowReady(window);
            var first = context.Windows[0];
            var second = context.Windows[1];
            var pane = first.GetPane(0);
            var group = pane.Groups.Single();
            Check(context.State.ConfirmCloseGroup && context.Windows.All(window => GcSetting(window).Checked),
                "confirmation defaults on in the shared state and both Settings menus");
            if (output is not null)
                Check(first.DeviceDpi == 120 && second.DeviceDpi == 120,
                    $"focused light/dark captures run at real 125% DPI (owners {first.DeviceDpi}/{second.DeviceDpi})");

            // Keep both undo and redo populated while cancellation is exercised.
            pane.AddBlankTab();
            pane.CloseTab();
            pane.AddBlankTab();
            pane.CloseTab();
            GcShortcut(first, Keys.Control | Keys.Shift | Keys.T);
            pane.SelectTab(pane.IndexOfTab(pane.GroupMembers(group)[0]), focus: false);
            foreach (string action in new[] { "escape", "cancel", "close-box" })
            {
                var unchanged = GcUnchanged(first, pane, group, "editor " + action);
                GcAnswerDialog(() => GcEditorCommand(pane, group, "Close group", "Uncommitted rename"), dialog =>
                {
                    GcCheckDialog(dialog, first, group, 2, false, null, "editor " + action);
                    GcAnswer(dialog, action, dontAsk: true);
                });
                unchanged();
                await NativeAppearanceDispatch(first);
            }
            foreach (string action in new[] { "escape", "cancel", "close-box" })
            {
                var unchanged = GcUnchanged(first, pane, group, "Alt+Shift+W " + action);
                GcAnswerDialog(() => GcShortcut(first, Keys.Alt | Keys.Shift | Keys.W), dialog => GcAnswer(dialog, action));
                unchanged();
            }

            foreach (string theme in new[] { "light", "dark" })
            foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
            foreach (bool unnamed in new[] { false, true })
            {
                first.SetColorTheme(theme);
                first.SetLanguage(language);
                pane.RenameGroup(group, unnamed ? "" : "Research 연구 日本語");
                pane.SelectTab(pane.IndexOfTab(pane.GroupMembers(group)[0]), focus: false);
                await NativeAppearanceDispatch(first);
                string label = $"{theme}/{language}/{(unnamed ? "unnamed" : "named")}";
                string? capture = output is null ? null : Path.Combine(output,
                    "group-close-" + label.Replace('/', '-') + $"-dpi{first.DeviceDpi}.png");
                var unchanged = GcUnchanged(first, pane, group, label + " cancellation");
                GcAnswerDialog(() =>
                {
                    if (unnamed) GcShortcut(first, Keys.Alt | Keys.Shift | Keys.W);
                    else GcEditorCommand(pane, group, "Close group");
                }, dialog =>
                {
                    GcCheckDialog(dialog, first, group, 2, false, capture, label);
                    GcAnswer(dialog, "escape");
                });
                unchanged();
                Check(context.Windows.All(window => GcSetting(window).CheckOnClick && GcSetting(window).Checked
                    && GcSetting(window).Text == new UiText(language).T("Confirm before closing a group")),
                    label + " localizes the checkable shared Settings preference in every window");
            }
            first.SetLanguage("en");
            pane.RenameGroup(group, "Research 연구 日本語");

            foreach (string action in new[] { "enter", "accept" })
            {
                var members = pane.GroupMembers(group).Select(tab => tab.Host?.LocationPath).ToArray();
                int before = pane.TabCount;
                int closed = 0;
                void OnClosed(object? sender, EventArgs args) => closed++;
                pane.TabClosed += OnClosed;
                try
                {
                    GcAnswerDialog(() =>
                    {
                        if (action == "enter") GcShortcut(first, Keys.Alt | Keys.Shift | Keys.W);
                        else GcEditorCommand(pane, group, "Close group");
                    }, dialog => GcAnswer(dialog, action));
                    Check(pane.TabCount == before - members.Length && !pane.Groups.Contains(group) && closed == 1,
                        action + " closes the complete group as one history action");
                    GcShortcut(first, Keys.Control | Keys.Shift | Keys.T);
                    group = pane.Groups.Single();
                    Check(pane.TabCount == before && group.Name == "Research 연구 日本語" && group.Color == TabGroupColor.Blue
                        && pane.GroupMembers(group).Select(tab => tab.Host?.LocationPath).SequenceEqual(members),
                        "one Ctrl+Shift+T restores all " + action + "-closed members and metadata");
                }
                finally { pane.TabClosed -= OnClosed; }
            }

            GcAnswerDialog(() => GcEditorCommand(pane, group, "Close group"), dialog => GcAnswer(dialog, "accept", dontAsk: true));
            Check(!context.State.ConfirmCloseGroup && context.Windows.All(window => !GcSetting(window).Checked)
                && context.SaveSession() && !SessionStore.Load(session).ConfirmCloseGroup,
                "accepted Don't ask again persists false and refreshes every window's Settings checkmark");
            GcShortcut(first, Keys.Control | Keys.Shift | Keys.T);
            group = pane.Groups.Single();
            GcWithoutPrompt(() => GcEditorCommand(pane, group, "Close group"), "opted-out editor close");
            GcShortcut(first, Keys.Control | Keys.Shift | Keys.T);
            group = pane.Groups.Single();
            var peer = second.GetPane(0);
            GcWithoutPrompt(() => GcShortcut(second, Keys.Alt | Keys.Shift | Keys.W), "opted-out peer shortcut close");
            Check(peer.Groups.Count == 0, "peer shortcut actually closes its group while opted out");
            GcShortcut(second, Keys.Control | Keys.Shift | Keys.T);
            context.CreateWindowFromActive(second);
            var third = context.Windows.Single(window => window != first && window != second);
            await MultiWindowReady(third);
            var thirdPane = third.GetPane(0);
            var thirdGroup = thirdPane.AddGroup(paths.Take(2).ToArray(), "New window", TabGroupColor.Green)!;
            thirdPane.SelectTab(thirdPane.IndexOfTab(thirdPane.GroupMembers(thirdGroup)[0]), focus: false);
            GcWithoutPrompt(() => GcShortcut(third, Keys.Alt | Keys.Shift | Keys.W), "opted-out newly-created window close");
            Check(!thirdPane.Groups.Contains(thirdGroup) && !GcSetting(third).Checked,
                "new windows inherit the existing disabled preference");

            GcSetting(second).PerformClick();
            Check(context.State.ConfirmCloseGroup && context.Windows.All(window => GcSetting(window).Checked),
                "real Settings toggle re-enables confirmation app-wide");
            pane.SelectTab(pane.IndexOfTab(pane.GroupMembers(group)[0]), focus: false);
            GcAnswerDialog(() => GcShortcut(first, Keys.Alt | Keys.Shift | Keys.W), dialog => GcAnswer(dialog, "cancel"));
            GcAnswerDialog(() => GcShortcut(second, Keys.Alt | Keys.Shift | Keys.W), dialog => GcAnswer(dialog, "cancel"));
            GcShortcut(third, Keys.Control | Keys.Shift | Keys.T);
            GcAnswerDialog(() => GcShortcut(third, Keys.Alt | Keys.Shift | Keys.W), dialog => GcAnswer(dialog, "cancel"));
            GcSetting(first).PerformClick();
            SavedGroupToggle(pane, group);
            string savedId = group.SavedId!;
            foreach (string theme in new[] { "light", "dark" })
            foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
            {
                first.SetColorTheme(theme);
                first.SetLanguage(language);
                await NativeAppearanceDispatch(first);
                var unchanged = GcUnchanged(first, pane, group, $"{theme}/{language} delete cancellation");
                GcAnswerDialog(() => GcEditorCommand(pane, group, "Delete group", "Uncommitted saved rename"), dialog =>
                {
                    GcCheckDialog(dialog, first, group, 2, true, output is null ? null
                        : Path.Combine(output, $"group-delete-{theme}-{language}-dpi{first.DeviceDpi}.png"), $"{theme}/{language} saved delete");
                    GcAnswer(dialog, "cancel");
                });
                unchanged();
                Check(!context.State.ConfirmCloseGroup && context.SavedGroups.Any(saved => saved.Id == savedId),
                    "saved deletion always asks even with close prompts disabled and cancellation keeps the saved entry");
            }
            first.SetLanguage("en");
            int deleteBefore = pane.TabCount;
            var deletedPaths = pane.GroupMembers(group).Select(tab => tab.Host?.LocationPath).ToArray();
            string deletedName = group.Name;
            var deletedColor = group.Color;
            GcAnswerDialog(() => GcEditorCommand(pane, group, "Delete group"), dialog => GcAnswer(dialog, "accept"));
            Check(pane.TabCount == deleteBefore - 2 && context.SavedGroups.All(saved => saved.Id != savedId)
                && context.Windows.All(window => SavedGroupList(window).Items.Count == 0),
                "confirmed saved Delete closes members and removes the saved copy in every sidebar");
            GcShortcut(first, Keys.Control | Keys.Shift | Keys.T);
            group = pane.Groups.Single();
            Check(pane.TabCount == deleteBefore && group.SavedId is null
                && group.Name == deletedName && group.Color == deletedColor
                && pane.GroupMembers(group).Select(tab => tab.Host?.LocationPath).SequenceEqual(deletedPaths)
                && context.SavedGroups.All(saved => saved.Id != savedId),
                "one undo restores deleted group tabs without resurrecting its saved copy");

            GcSetting(first).PerformClick();
            await GcSideEffectCloses(context, first, paths);
            // Leave a real grouped session with the disabled preference for a fresh-context check.
            if (context.State.ConfirmCloseGroup) GcSetting(first).PerformClick();
            first.GetPane(0).AddGroup(paths.Take(2).ToArray(), "Restart group", TabGroupColor.Purple);
            Check(context.SaveSession() && !SessionStore.Load(session).ConfirmCloseGroup,
                "disabled preference is serialized with the final grouped session");
        });
        using var restarted = MultiWindowContext(session);
        RunMultiWindowContext(restarted, async () =>
        {
            foreach (var window in restarted.Windows.ToArray())
            {
                await MultiWindowReady(window);
                Check(!restarted.State.ConfirmCloseGroup && !GcSetting(window).Checked,
                    "fresh context restores disabled confirmation in every Settings menu");
                var pane = window.GetPane(window.CurrentLayout.GetPanes()[0]);
                var group = pane.AddGroup(paths.Take(2).ToArray(), "Restart close", TabGroupColor.Orange)!;
                pane.SelectTab(pane.IndexOfTab(pane.GroupMembers(group)[0]), focus: false);
                GcWithoutPrompt(() => GcShortcut(window, Keys.Alt | Keys.Shift | Keys.W), "restart window shortcut");
                Check(!pane.Groups.Contains(group), "restart shortcut closes the real group without prompting");
            }
        });
    }


    private static async Task GcSideEffectCloses(FolderSplitterApplicationContext context, MainForm window, string[] paths)
    {
        var pane = window.GetPane(0);
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(3).ToList(),
            Groups = [new() { Name = "Side effects", Color = "Cyan", Count = 2 }] });
        var group = pane.Groups.Single();
        var outside = pane.Tabs.Single(tab => tab.Group is null);
        GcWithoutPrompt(() =>
        {
            var menu = TgOpenTabMenu(pane, outside);
            try { menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Close other tabs").PerformClick(); }
            finally { if (!menu.IsDisposed) menu.Close(); }
        }, "Close other tabs removing an entire group");
        Check(pane.TabCount == 1 && pane.Groups.Count == 0, "Close other tabs performs its real grouped side effect");
        GcShortcut(window, Keys.Control | Keys.Shift | Keys.T);
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(3).ToList(),
            Groups = [new() { Name = "Multi close", Color = "Cyan", Count = 2 }] });
        group = pane.Groups.Single();
        var members = pane.GroupMembers(group).ToArray();
        MsClickTab(pane, members[0]);
        MsClickTab(pane, members[1], Keys.Control);
        GcWithoutPrompt(() => MsCommand(pane, members[1], "Close 2 tabs"), "multi-selection close containing a complete group");
        Check(!pane.Groups.Contains(group) && pane.TabCount == 1, "multi-selection closes all selected grouped members");
        GcShortcut(window, Keys.Control | Keys.Shift | Keys.T);
        group = pane.Groups.Single();
        GcWithoutPrompt(() => TgMoveMenu(pane, group, 2), "moving a group to another pane");
        var target = window.GetPane(1);
        Check(target.Groups.Any(candidate => candidate.Name == "Multi close") && !pane.Groups.Contains(group),
            "move executes the real editor transfer rather than closing its members");
        GcWithoutPrompt(() => target.TabStrip.Items.OfType<ToolStripButton>()
            .Single(button => button.AccessibleName == "Pane 2 close pane").PerformClick(), "pane close containing a group");
        Check(!target.Visible, "pane-close consumer hides the grouped pane");
        GcShortcut(window, Keys.Control | Keys.Shift | Keys.T);
        Check(target.Visible && target.Groups.Count == 1, "pane close still has its ordinary one-step undo");
        pane.AddGroup(paths.Take(2).ToArray(), "Workspace replacement group", TabGroupColor.Pink);
        window.SaveWorkspace("Grouped confirmation workspace");
        pane.ReplaceTabs(new PaneState { Tabs = [paths[7]] });
        window.SaveWorkspace("Replacement confirmation workspace");
        window.LoadWorkspace("Grouped confirmation workspace");
        GcWithoutPrompt(() => window.LoadWorkspace("Replacement confirmation workspace"), "workspace replacement of grouped panes");
        Check(pane.CaptureState().Tabs.SequenceEqual([paths[7]]), "workspace replacement actually loads its saved tabs");
        context.CreateWindowFromActive(window);
        var transient = context.Windows[^1];
        await MultiWindowReady(transient);
        transient.GetPane(0).AddGroup(paths.Take(2).ToArray(), "Window close", TabGroupColor.Red);
        GcWithoutPrompt(transient.Close, "whole-window close containing a group");
        Check(!context.Windows.Contains(transient), "whole-window close unregisters its grouped window");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GcIsWindowEnabled(nint window);
}
