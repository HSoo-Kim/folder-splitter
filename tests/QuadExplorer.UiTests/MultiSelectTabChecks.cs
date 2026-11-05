using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static void RunMultiSelectTabScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "MultiSelectTabs")).FullName;
        string[] paths = Enumerable.Range(1, 8).Select(index =>
            Directory.CreateDirectory(Path.Combine(folder, $"Selected folder {index}")).FullName).ToArray();
        string session = Path.Combine(root, "multi-select-tabs.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, CheckUpdates = false,
            Panes = [new PaneState { Tabs = [paths[0]] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                form.Size = new Size(1800, 900);
                var pane = form.GetPane(0);
                if (pane.ActiveView is { HasNavigated: false } view)
                    await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                MsSelectionInput(form, pane);
                MsAppearance(form, pane);
                await MsGrouping(form, pane);
                await MsMenuScope(form, pane, paths);
                MsCloseHistory(form, pane, paths);
                MsCollapsedReplacement(form, pane);
            }
            catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + exception); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        Theme.Set("light");
    }

    // Deliver native mouse messages to this private HWND with the same thread-local modifier
    // table used by the existing keyboard checks; never move the user's cursor or send global input.
    private static void MsClickTab(ExplorerPane pane, FolderTab tab, Keys modifiers = Keys.None)
    {
        pane.TabStrip.PerformLayout();
        if (!tab.Button.Available || tab.Button.Bounds.Width <= tab.Button.CloseWidth)
            throw new InvalidOperationException("The multi-select click target is not visible outside its close button.");
        Rectangle bounds = tab.Button.Bounds;
        Point point = new(bounds.Left + Math.Min(12, (bounds.Width - tab.Button.CloseWidth) / 2),
            bounds.Top + bounds.Height / 2);
        nint packed = (nint)((point.Y << 16) | (point.X & 0xffff));
        byte[] saved = new byte[256], state = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("Could not read modifier state for tab click.");
        if ((modifiers & Keys.Control) != 0) state[0x11] = state[0xA2] = 0x80;
        if ((modifiers & Keys.Shift) != 0) state[0x10] = state[0xA0] = 0x80;
        nint mouseKeys = ((modifiers & Keys.Control) != 0 ? 8 : 0) | ((modifiers & Keys.Shift) != 0 ? 4 : 0);
        try
        {
            if (!PnSetKeyboardState(state)) throw new InvalidOperationException("Could not set tab-click modifiers.");
            SendMessage(pane.TabStrip.Handle, 0x200, mouseKeys, packed);
            SendMessage(pane.TabStrip.Handle, 0x201, mouseKeys | 1, packed);
            SendMessage(pane.TabStrip.Handle, 0x202, mouseKeys, packed);
            SendMessage(pane.TabStrip.Handle, 0x2A3, 0, 0); // WM_MOUSELEAVE: no synthetic hover in captures
        }
        finally
        {
            if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore tab-click modifiers.");
        }
    }

    private static void MsSelected(ExplorerPane pane, string action, params FolderTab[] expected)
    {
        Check(pane.SelectedTabs.SequenceEqual(expected)
            && pane.ActiveTab is { } active && expected.Contains(active),
            action + " keeps exactly the expected strip-ordered selection including the active tab");
        Check(pane.Tabs.All(tab => tab.Button.MultiSelected == expected.Contains(tab)),
            action + " updates every selected and unselected tab button");
    }

    private static ContextMenuStrip MsOpenMenu(ExplorerPane pane, FolderTab tab, string command)
    {
        // TgOpenTabMenu deliberately selects one tab first, so it must not be used for a batch.
        TgRightClick(pane, tab.Button);
        return TgVisibleMenu(command);
    }

    private static void MsCommand(ExplorerPane pane, FolderTab tab, string command)
    {
        var menu = MsOpenMenu(pane, tab, command);
        try { menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == command).PerformClick(); }
        finally { if (!menu.IsDisposed) menu.Close(); }
    }

    private static async Task MsCloseGroupEditor()
    {
        // Creating a group opens the actual name editor with BeginInvoke.
        await MultiWindowIdle();
        var menu = TgVisibleMenu("Close group");
        menu.Close();
    }

    private static void MsSelectionInput(MainForm form, ExplorerPane pane)
    {
        TgReset(pane, 7);
        FolderTab[] tabs = pane.Tabs.ToArray();
        MsClickTab(pane, tabs[1]);
        MsSelected(pane, "plain click", tabs[1]);
        MsClickTab(pane, tabs[4], Keys.Control);
        Check(pane.ActiveTab == tabs[4], "Ctrl-click makes the newly added tab active");
        MsSelected(pane, "Ctrl-click adds a nonadjacent tab", tabs[1], tabs[4]);
        MsClickTab(pane, tabs[4], Keys.Control);
        MsSelected(pane, "Ctrl-click cannot remove the active tab from a batch", tabs[1], tabs[4]);
        MsClickTab(pane, tabs[1], Keys.Control);
        Check(pane.ActiveTab == tabs[4], "Ctrl-toggle of an inactive member does not change the active tab");
        MsSelected(pane, "Ctrl-click removes an inactive tab", tabs[4]);
        MsClickTab(pane, tabs[4], Keys.Control);
        MsSelected(pane, "Ctrl-click cannot remove the sole active tab", tabs[4]);

        MsClickTab(pane, tabs[1]);
        MsClickTab(pane, tabs[4], Keys.Shift);
        MsSelected(pane, "forward Shift range", tabs[1], tabs[2], tabs[3], tabs[4]);
        MsClickTab(pane, tabs[2], Keys.Shift);
        MsSelected(pane, "repeated Shift range retains its original plain-click anchor", tabs[1], tabs[2]);
        MsClickTab(pane, tabs[5], Keys.Control);
        MsClickTab(pane, tabs[3], Keys.Shift);
        MsSelected(pane, "backward Shift range uses the last Ctrl-click anchor", tabs[3], tabs[4], tabs[5]);
        MsClickTab(pane, tabs[0]);
        MsSelected(pane, "plain click clears a preceding range", tabs[0]);

        var group = pane.CreateGroup(new[] { tabs[2], tabs[3] })!;
        pane.RenameGroup(group, "Hidden range");
        // Collapse through the real chip, not by changing its model flag.
        Rectangle chip = TgChip(pane, group).Bounds;
        nint chipPoint = (nint)(((chip.Top + chip.Height / 2) << 16) | ((chip.Left + chip.Width / 2) & 0xffff));
        SendMessage(pane.TabStrip.Handle, 0x201, 1, chipPoint);
        SendMessage(pane.TabStrip.Handle, 0x202, 0, chipPoint);
        Check(group.Collapsed && !tabs[2].Button.Available && !tabs[3].Button.Available,
            "real chip click hides both collapsed members before Shift selection");
        MsClickTab(pane, tabs[1]);
        MsClickTab(pane, tabs[5], Keys.Shift);
        MsSelected(pane, "Shift range skips collapsed members", tabs[1], tabs[4], tabs[5]);
        Check(group.Collapsed, "Shift-click across a collapsed group never expands it");
        MsClickTab(pane, tabs[6]);
        MsClickTab(pane, tabs[0], Keys.Shift);
        MsSelected(pane, "backward Shift range skips collapsed members", tabs[0], tabs[1], tabs[4], tabs[5], tabs[6]);

        pane.TabStrip.Focus();
        TgKey(pane.TabStrip, Keys.Escape);
        MsSelected(pane, "Esc in the strip clears multi-selection", tabs[0]);
        MsClickTab(pane, tabs[1], Keys.Control);
        pane.FocusAddress();
        TgKey(form, Keys.Control | Keys.Tab);
        Check(pane.ActiveTab == tabs[4] && group.Collapsed,
            "Ctrl+Tab switches to the next visible tab rather than a collapsed member");
        MsSelected(pane, "keyboard tab switching clears multi-selection", tabs[4]);
        MsClickTab(pane, tabs[6], Keys.Control);
        pane.FocusAddress();
        TgKey(form, Keys.Control | Keys.Shift | Keys.Tab);
        MsSelected(pane, "reverse keyboard switching also clears multi-selection", tabs[5]);

        MsClickTab(pane, tabs[0]);
        MsClickTab(pane, tabs[1], Keys.Control);
        MsClickTab(pane, tabs[6], Keys.Control);
        // Middle-click an unselected tab without first opening a menu or changing selection.
        Rectangle closing = tabs[4].Button.Bounds;
        nint closePoint = (nint)(((closing.Top + closing.Height / 2) << 16) | ((closing.Left + 12) & 0xffff));
        SendMessage(pane.TabStrip.Handle, 0x207, 0x10, closePoint);
        SendMessage(pane.TabStrip.Handle, 0x208, 0, closePoint);
        Check(!pane.Tabs.Contains(tabs[4]) && pane.ActiveTab == tabs[6],
            "middle-click removes only its unselected target without changing the active tab");
        MsSelected(pane, "removing an unrelated tab clears the existing batch", tabs[6]);
        TgContiguous(pane, "multi-select input and collapsed ranges");
    }

    private static void MsAppearance(MainForm form, ExplorerPane pane)
    {
        TgReset(pane, 4);
        FolderTab[] tabs = pane.Tabs.ToArray();
        var group = pane.CreateGroup(new[] { tabs[0], tabs[1] })!;
        pane.SetGroupColor(group, TabGroupColor.Purple);
        pane.RenameGroup(group, "Selected group");
        string output = Path.Combine(Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR")
            ?? AppContext.BaseDirectory, "artifacts", "multi-select-tabs");
        Directory.CreateDirectory(output);
        foreach (string mode in new[] { "light", "dark", "custom-light", "custom-dark" })
        {
            if (mode == "custom-light") form.SetCustomColors(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
            else if (mode == "custom-dark") form.SetCustomColors(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
            else form.SetColorTheme(mode);
            MsClickTab(pane, tabs[0]);
            MsClickTab(pane, tabs[3], Keys.Control);
            pane.FocusAddress();
            form.Refresh();
            MsSelected(pane, mode + " themed selection", tabs[0], tabs[3]);
            Check(tabs[0].Button.AccessibilityObject.State.HasFlag(AccessibleStates.Selected)
                && tabs[3].Button.AccessibilityObject.State.HasFlag(AccessibleStates.Selected)
                && !tabs[1].Button.AccessibilityObject.State.HasFlag(AccessibleStates.Selected)
                && !tabs[2].Button.AccessibilityObject.State.HasFlag(AccessibleStates.Selected),
                mode + " accessibility exposes Selected for both active and inactive selected tabs only");
            using var image = TgPrintStrip(pane.TabStrip);
            int y = Math.Max(7, (int)Math.Round(9 * pane.TabStrip.DeviceDpi / 96d));
            Color Sample(FolderTab tab) => image.GetPixel(tab.Button.Bounds.Left + tab.Button.Width / 2,
                tab.Button.Bounds.Top + y);
            Color selected = Sample(tabs[0]), ordinary = Sample(tabs[1]), raised = Sample(tabs[3]);
            Rectangle interior = new(tabs[0].Button.Bounds.Left + 8, tabs[0].Button.Bounds.Top + y - 1,
                tabs[0].Button.Width - 16, 3);
            Check(selected.ToArgb() != ordinary.ToArgb() && selected.ToArgb() != raised.ToArgb()
                && TgPixels(image, interior, selected) >= interior.Width,
                mode + " pixels distinguish the quiet multi-selection fill from idle and raised active tabs");
            Check(raised.ToArgb() == Theme.Surface.ToArgb() && TgContrast(selected, Theme.Text) >= 4.5,
                mode + " keeps the active surface raised and multi-selected tab text readable");
            Rectangle underline = new(tabs[0].Button.Bounds.Left + 6, tabs[0].Button.Bounds.Bottom - 2,
                tabs[0].Button.Width - 12, 2);
            Check(TgPixels(image, underline, Theme.GroupColor(group.Color)) >= underline.Width,
                mode + " inactive multi-selected group member retains its colored underline");
            TgSave(image, Path.Combine(output, $"multi-select-{mode}-strip.png"));
            using var window = PrintForm(form, 2);
            TgSave(window, Path.Combine(output, $"multi-select-{mode}-window.png"));
        }
        form.SetColorTheme("light");
    }

    private static async Task MsGrouping(MainForm form, ExplorerPane pane)
    {
        TgReset(pane, 8);
        FolderTab[] tabs = pane.Tabs.ToArray();
        var first = pane.CreateGroup(new[] { tabs[1], tabs[2] })!;
        var second = pane.CreateGroup(new[] { tabs[4], tabs[5] })!;
        pane.RenameGroup(first, "Merge first");
        pane.RenameGroup(second, "Merge second");
        MsClickTab(pane, tabs[1]);
        foreach (int index in new[] { 2, 4, 5, 7 }) MsClickTab(pane, tabs[index], Keys.Control);
        MsCommand(pane, tabs[2], "Add 5 tabs to new group");
        await MsCloseGroupEditor();
        var merged = tabs[1].Group!;
        Check(pane.Groups.Count == 1 && merged != first && merged != second
            && pane.GroupMembers(merged).SequenceEqual(new[] { tabs[1], tabs[2], tabs[4], tabs[5], tabs[7] })
            && pane.Tabs.SequenceEqual(new[] { tabs[0], tabs[1], tabs[2], tabs[4], tabs[5], tabs[7], tabs[3], tabs[6] })
            && pane.ActiveTab == tabs[7],
            "batch new-group menu merges whole groups and loose tabs at the first selection in strip order");
        TgContiguous(pane, "mixed whole-group selection merge");
        MsCommand(pane, tabs[1], "Remove from group");
        Check(pane.Groups.Count == 0 && pane.Tabs.All(tab => tab.Group is null) && pane.TabCount == 8,
            "batch Remove from group removes the entire merged selection without closing tabs");

        // Partial old groups must survive as contiguous runs around a gathered new group.
        TgReset(pane, 7);
        tabs = pane.Tabs.ToArray();
        first = pane.CreateGroup(new[] { tabs[1], tabs[2], tabs[3] })!;
        second = pane.CreateGroup(new[] { tabs[4], tabs[5] })!;
        MsClickTab(pane, tabs[2]);
        MsClickTab(pane, tabs[4], Keys.Control);
        MsClickTab(pane, tabs[6], Keys.Control);
        MsCommand(pane, tabs[2], "Add 3 tabs to new group");
        await MsCloseGroupEditor();
        merged = tabs[2].Group!;
        Check(pane.GroupMembers(first).SequenceEqual(new[] { tabs[1], tabs[3] })
            && pane.GroupMembers(second).SequenceEqual(new[] { tabs[5] })
            && pane.GroupMembers(merged).SequenceEqual(new[] { tabs[2], tabs[4], tabs[6] }),
            "new-group menu gathers partial groups and a loose tab without stealing unselected members");
        TgContiguous(pane, "partial old-group merge");
        MsCommand(pane, tabs[4], "Remove from group");
        Check(new[] { tabs[2], tabs[4], tabs[6] }.All(tab => tab.Group is null)
            && pane.Groups.Count == 2 && pane.GroupMembers(first).Count == 2 && pane.GroupMembers(second).Count == 1,
            "batch ungroup leaves unselected source groups unchanged");

        // Fourteen minimum-width tabs at the private desktop's DPI need more room than the normal capture fixture.
        form.Width = 2600;
        TgReset(pane, 14);
        tabs = pane.Tabs.ToArray();
        MsClickTab(pane, tabs[0]);
        MsClickTab(pane, tabs[12], Keys.Shift);
        FolderTab[] before = pane.Tabs.ToArray();
        var active = pane.ActiveTab;
        MsCommand(pane, tabs[3], "Add 13 tabs to new group");
        Check(!pane.Groups.Any() && pane.Tabs.SequenceEqual(before) && pane.ActiveTab == active
            && pane.SelectedTabs.SequenceEqual(tabs.Take(13)),
            "thirteen-tab new-group menu refuses atomically without changing order, selection or groups");
        TgLimitStatus(pane, "batch thirteen-tab new group");
        MsClickTab(pane, tabs[0]);
        MsClickTab(pane, tabs[11], Keys.Shift);
        MsCommand(pane, tabs[4], "Add 12 tabs to new group");
        await MsCloseGroupEditor();
        var full = tabs[0].Group!;
        Check(pane.GroupMembers(full).SequenceEqual(tabs.Take(12)) && tabs[12].Group is null && tabs[13].Group is null,
            "exactly twelve selected tabs form a group through the counted context command");

        TgReset(pane, 14);
        tabs = pane.Tabs.ToArray();
        var destination = pane.CreateGroup(tabs.Take(11).ToArray())!;
        pane.RenameGroup(destination, "Eleven members");
        MsClickTab(pane, tabs[1]);
        MsClickTab(pane, tabs[11], Keys.Control);
        MsClickTab(pane, tabs[12], Keys.Control);
        var menu = MsOpenMenu(pane, tabs[1], "Add tabs to group");
        try
        {
            var add = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Add tabs to group");
            var item = add.DropDownItems.OfType<ToolStripMenuItem>().Single();
            Check(!item.Enabled && item.ToolTipText == "A group holds up to 12 tabs."
                && pane.GroupMembers(destination).Count == 11 && tabs[11].Group is null && tabs[12].Group is null,
                "existing-group submenu counts only incoming tabs and disables a batch that would make thirteen");
        }
        finally { menu.Close(); }
        MsClickTab(pane, tabs[1]);
        MsClickTab(pane, tabs[11], Keys.Control);
        menu = MsOpenMenu(pane, tabs[1], "Add tabs to group");
        try
        {
            var add = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Add tabs to group");
            var item = add.DropDownItems.OfType<ToolStripMenuItem>().Single();
            Check(item.Enabled, "existing destination accepts a selected current member plus its twelfth incoming tab");
            item.PerformClick();
        }
        finally { menu.Close(); }
        Check(pane.GroupMembers(destination).Count == 12 && tabs[1].Group == destination && tabs[11].Group == destination
            && tabs[12].Group is null && tabs[13].Group is null && pane.ActiveTab == tabs[11],
            "batch Add tabs to group joins at exact capacity without double-counting an existing selected member");
        TgContiguous(pane, "batch grouping capacity boundaries");
        form.Width = 1800;
        form.Refresh();
    }

    private static async Task MsMenuScope(MainForm form, ExplorerPane pane, string[] paths)
    {
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(4).ToList() });
        FolderTab[] tabs = pane.Tabs.ToArray();
        if (pane.ActiveView is { HasNavigated: false } view)
            await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
        MsClickTab(pane, tabs[0]);
        MsClickTab(pane, tabs[2], Keys.Control);
        var menu = MsOpenMenu(pane, tabs[0], "Close 2 tabs");
        try
        {
            string?[] commands = menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
            Check(commands.Contains("Add 2 tabs to new group") && commands.Contains("Add tabs to group")
                && commands.Contains("Move tabs to pane") && commands.Contains("Move tabs to new window")
                && commands.Contains("Duplicate tab") && !commands.Contains("Close tab    Ctrl+W")
                && pane.ActiveTab == tabs[2] && pane.SelectedTabs.SequenceEqual(new[] { tabs[0], tabs[2] }),
                "right-click on an inactive selected tab preserves the batch and exposes plural commands with counted close/group labels");
            menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Duplicate tab").PerformClick();
        }
        finally { menu.Close(); }
        Check(pane.TabCount == 5 && pane.Tabs.Count(tab => tab.Host?.LocationPath == paths[0]) == 2
            && pane.Tabs.Count(tab => tab.Host?.LocationPath == paths[2]) == 1,
            "Duplicate remains single-tab and duplicates the right-clicked selected member rather than the entire batch or active member");
        MsClickTab(pane, tabs[0]);
        MsClickTab(pane, tabs[2], Keys.Control);
        menu = MsOpenMenu(pane, tabs[3], "Add tab to new group");
        try
        {
            Check(pane.ActiveTab == tabs[3] && pane.SelectedTabs.SequenceEqual(new[] { tabs[3] })
                && menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "Close tab    Ctrl+W")
                && !menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "Close 2 tabs"),
                "right-click outside the selection resets to that tab and single-tab commands");
        }
        finally { menu.Close(); }
        pane.FocusAddress();
        TgKey(form, Keys.Control | Keys.Tab);
        Check(pane.SelectedTabs.Count == 1, "menu dismissal leaves ordinary keyboard tab switching single-selected");
    }

    private static void MsCloseHistory(MainForm form, ExplorerPane pane, string[] paths)
    {
        pane.ReplaceTabs(new PaneState { Tabs = paths.ToList() });
        FolderTab[] tabs = pane.Tabs.ToArray();
        var whole = pane.CreateGroup(new[] { tabs[1], tabs[2] })!;
        var partial = pane.CreateGroup(new[] { tabs[4], tabs[5] })!;
        pane.RenameGroup(whole, "Whole selection 연구");
        pane.SetGroupColor(whole, TabGroupColor.Cyan);
        pane.RenameGroup(partial, "Surviving group");
        pane.SetGroupColor(partial, TabGroupColor.Orange);
        MsClickTab(pane, tabs[1]);
        foreach (int index in new[] { 2, 4, 6 }) MsClickTab(pane, tabs[index], Keys.Control);
        int closed = 0;
        void Closed(object? sender, EventArgs args) => closed++;
        pane.TabClosed += Closed;
        try
        {
            MsCommand(pane, tabs[2], "Close 4 tabs");
            Check(closed == 1 && pane.Tabs.SequenceEqual(new[] { tabs[0], tabs[3], tabs[5], tabs[7] })
                && !pane.Groups.Contains(whole) && pane.GroupMembers(partial).SequenceEqual(new[] { tabs[5] })
                && pane.SelectedTabs.Count == 1,
                "Close N menu removes only the mixed selection, clears the batch and emits exactly one undoable close");
            var globalUndo = TgHistoryButton(form, null, "Undo");
            var globalRedo = TgHistoryButton(form, null, "Redo");
            var localUndo = TgHistoryButton(form, pane, "Undo");
            var localRedo = TgHistoryButton(form, pane, "Redo");
            Check(globalUndo.Enabled && localUndo.Enabled, "both undo toolbars offer the single batch-close action");
            globalUndo.PerformClick();
            Check(pane.TabCount == 8 && pane.Tabs.Select(tab => tab.Host?.LocationPath).SequenceEqual(paths)
                && pane.LastReopenedTabs.Select(tab => tab.Host?.LocationPath).SequenceEqual(new[] { paths[1], paths[2], paths[4], paths[6] })
                && pane.ActiveTab?.Host?.LocationPath == paths[6],
                "one global undo restores every selected path at its original position and restores the active member");
            var restoredWhole = pane.Groups.Single(group => group.Name == "Whole selection 연구");
            var restoredPartial = pane.Groups.Single(group => group.Name == "Surviving group");
            Check(restoredWhole.Color == TabGroupColor.Cyan && restoredPartial == partial
                && restoredPartial.Color == TabGroupColor.Orange
                && TgPaths(pane, restoredWhole).SequenceEqual(new[] { paths[1], paths[2] })
                && TgPaths(pane, restoredPartial).SequenceEqual(new[] { paths[4], paths[5] }),
                "undo restores whole and partial group membership, names, colors and the surviving group identity");
            TgContiguous(pane, "batch undo restoration");
            Check(globalRedo.Enabled && localRedo.Enabled, "both redo toolbars target the entire reopened selection");
            MsClickTab(pane, tabs[0]);
            localRedo.PerformClick();
            Check(closed == 2 && pane.Tabs.SequenceEqual(new[] { tabs[0], tabs[3], tabs[5], tabs[7] })
                && pane.ActiveTab == tabs[0] && pane.SelectedTabs.SequenceEqual(new[] { tabs[0] })
                && !pane.Groups.Contains(restoredWhole) && pane.GroupMembers(partial).SequenceEqual(new[] { tabs[5] }),
                "one local redo closes the entire reopened batch even after selecting an outside tab");
            while (pane.TabCount < 29) pane.AddBlankTab();
            FolderTab[] before = pane.Tabs.ToArray();
            Check(!globalUndo.Enabled && !localUndo.Enabled,
                "batch undo is disabled when all four selected tabs cannot fit together");
            localUndo.PerformClick();
            Check(pane.Tabs.SequenceEqual(before), "disabled undo cannot partially restore a batch into three free slots");
            pane.CloseTab(remember: false);
            Check(globalUndo.Enabled && localUndo.Enabled, "freeing the fourth slot enables atomic batch undo");
            localUndo.PerformClick();
            Check(pane.TabCount == 32 && pane.Tabs.Take(8).Select(tab => tab.Host?.LocationPath).SequenceEqual(paths)
                && pane.LastReopenedTabs.Count == 4,
                "local undo restores all four batch members at exact pane capacity and retains their original positions");
            globalRedo.PerformClick();
            Check(pane.TabCount == 28 && closed == 3 && pane.SelectedTabs.Count == 1,
                "global redo closes the exact-capacity restored batch in one further history action");
            TgContiguous(pane, "batch capacity undo and redo");
        }
        finally { pane.TabClosed -= Closed; }
    }

    private static void MsCollapsedReplacement(MainForm form, ExplorerPane pane)
    {
        (string Name, int Count, int CloseStart, int[] HiddenStarts, int? Expected)[] layouts =
        [
            ("next visible", 6, 1, [3], 5),
            ("previous visible", 7, 3, [1, 5], 0),
            ("trailing removal", 5, 3, [1], 0),
            ("collapsed-only remainder", 4, 2, [0], null)
        ];
        foreach (string action in new[] { "close", "move", "redo" })
        foreach (var layout in layouts)
        {
            TgResetForm(form, action == "move" ? 2 : 1);
            TgReset(pane, layout.Count);
            FolderTab[] tabs = pane.Tabs.ToArray();
            var groups = layout.HiddenStarts.Select(start =>
                pane.CreateGroup(new[] { tabs[start], tabs[start + 1] })!).ToArray();
            foreach (var group in groups) pane.SetGroupCollapsed(group, true);
            FolderTab[] removed = [tabs[layout.CloseStart], tabs[layout.CloseStart + 1]];
            MsClickTab(pane, removed[0]);
            MsClickTab(pane, removed[1], Keys.Control);
            string scenario = action + " with " + layout.Name;
            MsSelected(pane, scenario + " fixture", removed);

            if (action == "move")
            {
                var target = form.GetPane(1);
                FolderTab[] before = target.Tabs.ToArray();
                var menu = MsOpenMenu(pane, removed[0], "Move tabs to pane");
                try
                {
                    var move = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move tabs to pane");
                    move.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pane 2").PerformClick();
                }
                finally { if (!menu.IsDisposed) menu.Close(); }
                Check(target.TabCount == before.Length + 2 && target.Tabs.Take(before.Length).SequenceEqual(before)
                    && target.ActiveTab == target.Tabs[^1] && target.SelectedTabs.Count == 1,
                    scenario + " transfers the whole selected batch through the pane menu");
            }
            else
            {
                MsCommand(pane, removed[0], "Close 2 tabs");
                if (action == "redo")
                {
                    var replacement = pane.ActiveTab;
                    var undo = TgHistoryButton(form, pane, "Undo");
                    Check(undo.Enabled, scenario + " offers the batch close in the real undo toolbar");
                    undo.PerformClick();
                    removed = pane.LastReopenedTabs.ToArray();
                    Check(removed.Length == 2 && pane.ActiveTab == removed[1],
                        scenario + " undo restores both selected members and their active member");
                    if (layout.Expected is null)
                    {
                        // Remove the first close's spare blank so redo also has only collapsed survivors.
                        Check(replacement is { Host: null, Group: null } && pane.Tabs.Contains(replacement),
                            scenario + " first close supplies an ungrouped blank without opening the hidden group");
                        if (replacement is not null) pane.CloseTab(pane.IndexOfTab(replacement), remember: false);
                    }
                    var redo = TgHistoryButton(form, pane, "Redo");
                    Check(redo.Enabled, scenario + " offers the restored batch in the real redo toolbar");
                    redo.PerformClick();
                }
            }

            FolderTab[] survivors = tabs.Where(tab => tab != tabs[layout.CloseStart]
                && tab != tabs[layout.CloseStart + 1]).ToArray();
            Check(removed.All(tab => !pane.Tabs.Contains(tab))
                && pane.Tabs.Take(survivors.Length).SequenceEqual(survivors)
                && pane.TabCount == survivors.Length + (layout.Expected is null ? 1 : 0),
                scenario + " removes only the batch and retains every original survivor in strip order");
            Check(groups.All(group => group.Collapsed && pane.GroupMembers(group).Count == 2
                    && pane.GroupMembers(group).All(tab => !tab.Button.Available)),
                scenario + " keeps every collapsed group closed and every member hidden");
            if (layout.Expected is { } expected)
                Check(pane.ActiveTab == tabs[expected],
                    scenario + " chooses the next visible tab, otherwise the nearest previous visible tab");
            else
                Check(pane.ActiveTab is { Host: null, Group: null } blank && !tabs.Contains(blank)
                    && pane.Tabs[^1] == blank && blank.Button.Available,
                    scenario + " creates and selects a visible ungrouped blank after the collapsed-only remainder");
            if (pane.ActiveTab is { } active) MsSelected(pane, scenario + " replacement", active);
            TgContiguous(pane, scenario);
        }
    }
}
