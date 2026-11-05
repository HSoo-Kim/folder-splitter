using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static string TgPolishOutput()
    {
        string path = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "tabgroups-polish");
        Directory.CreateDirectory(path);
        return path;
    }

    // Run the same public message-filter/preprocessing/native-HWND path as the WinForms loop.
    // Thread-local keyboard state avoids SendInput and never types into the user's desktop.
    private static void TgKey(Control fallback, Keys keys)
    {
        byte[] saved = new byte[256], state = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("Could not read keyboard state.");
        foreach (var (modifier, key, side) in new[] { (Keys.Control, 0x11, 0xA2),
            (Keys.Shift, 0x10, 0xA0), (Keys.Alt, 0x12, 0xA4) })
            if ((keys & modifier) != 0) state[key] = state[side] = 0x80;
        try
        {
            if (!PnSetKeyboardState(state)) throw new InvalidOperationException("Could not set thread keyboard state.");
            nint focus = PnGetFocus();
            Control target = Control.FromHandle(focus) ?? Control.FromChildHandle(focus) ?? fallback;
            var message = Message.Create(focus != 0 ? focus : fallback.Handle,
                (keys & Keys.Alt) != 0 ? 0x104 : 0x100, (nint)(keys & Keys.KeyCode), 1);
            if (target.FindForm() is MainForm form && form.PreFilterMessage(ref message)) return;
            if (target.PreProcessMessage(ref message)) return;
            SendMessage(message.HWnd, (uint)message.Msg, message.WParam, message.LParam);
        }
        finally
        {
            if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore keyboard state.");
        }
    }

    private static ContextMenuStrip TgVisibleMenu(string command)
    {
        ContextMenuStrip? result = null;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (Control.FromHandle(window) is ContextMenuStrip { Visible: true } menu
                && menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == command)) result = menu;
            return true;
        }, 0);
        return result ?? throw new InvalidOperationException("No visible menu containing " + command);
    }

    private static void TgRightClick(ExplorerPane pane, ToolStripItem item)
    {
        pane.TabStrip.PerformLayout();
        Point point = new(item.Bounds.Left + Math.Min(12, item.Width / 2), item.Bounds.Top + item.Height / 2);
        nint packed = (nint)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(pane.TabStrip.Handle, 0x204, 2, packed); // WM_RBUTTONDOWN
        SendMessage(pane.TabStrip.Handle, 0x205, 0, packed); // WM_RBUTTONUP
    }

    private static ContextMenuStrip TgOpenTabMenu(ExplorerPane pane, FolderTab tab)
    {
        pane.SelectTab(pane.IndexOfTab(tab), focus: false);
        TgRightClick(pane, tab.Button);
        return TgVisibleMenu("Add tab to new group");
    }

    private static ContextMenuStrip TgOpenGroupMenu(ExplorerPane pane, TabGroup group)
    {
        TgRightClick(pane, TgChip(pane, group));
        return TgVisibleMenu("Close group");
    }

    private static async Task TgPolish(MainForm form, ExplorerPane pane, string[] paths)
    {
        TgResetForm(form);
        TgReset(pane, 4);
        var a = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], a);
        var b = pane.CreateGroup(pane.Tabs[2])!;
        pane.AddToGroup(pane.Tabs[3], b);
        pane.SelectTab(0, focus: false);
        pane.SetGroupCollapsed(b, true);
        pane.SetGroupCollapsed(a, true);
        Check(a.Collapsed && b.Collapsed && pane.ActiveTab?.Group is null && pane.TabCount == 5,
            "collapsing next to an already collapsed group never expands its neighbor and creates an outside selection");
        TgReset(pane, 0);
        a = pane.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full A", TabGroupColor.Blue)!;
        b = pane.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Full B", TabGroupColor.Green)!;
        var c = pane.AddGroup(Enumerable.Repeat<string?>(null, 8).ToArray(), "Full C", TabGroupColor.Red)!;
        pane.SetGroupCollapsed(a, true);
        pane.SetGroupCollapsed(b, true);
        pane.SetGroupCollapsed(c, true);
        Check(a.Collapsed && b.Collapsed && !c.Collapsed && pane.TabCount == 32 && pane.ActiveTab?.Group == c,
            "full pane refuses a final collapse rather than hiding selection, expanding another group or exceeding 32 tabs");

        await TgEditorAndKeyboard(form, pane, paths);
        TgDuplicateAndSingleReopen(form, pane, paths);
        await TgWorkspaceAndPaneUndo(form, pane, paths);
        TgCloseOthersAndAtomicSelection(form, pane, paths);
        await TgRelatedTreeTabs(form, pane, paths);
        await TgNarrowAndLargeText(form, pane);
        TgKeyboardReorder(form, pane);
        TgDisabledGroupMoves(form, pane);
        TgPolishDrags(form);
        TgResetForm(form);
    }

    private static async Task TgEditorAndKeyboard(MainForm form, ExplorerPane pane, string[] paths)
    {
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(3).ToList() });
        var original = pane.Tabs[0];
        TgTabMenu(pane, original, "Add tab to new group").PerformClick();
        await NativeAppearanceDispatch(form); // the editor opens after native context-menu teardown
        var menu = TgVisibleMenu("Close group");
        var name = menu.Items.OfType<ToolStripTextBox>().Single();
        var group = original.Group ?? throw new InvalidOperationException("New-group menu did not create a group.");
        Check(name.TextBox.Focused && pane.TabCount == 3,
            "new-group command opens its editor with the native name field focused and no extra tab");
        Rectangle chip = pane.TabStrip.RectangleToScreen(TgChip(pane, group).Bounds);
        Check(Math.Abs(menu.Left - chip.Left) <= 4 && Math.Abs(menu.Top - chip.Bottom) <= 4,
            "new group editor is anchored beneath its chip rather than at the mouse cursor");
        using (var image = PrintForm(form, 2)) TgSave(image, Path.Combine(TgPolishOutput(), "new-group-editor-open-window.png"));
        using (var image = TgPrintStrip(menu)) TgSave(image, Path.Combine(TgPolishOutput(), "new-group-editor-open.png"));
        name.Text = "  Keyboard research  ";
        TgKey(name.TextBox, Keys.Enter);
        Check(group.Name == "Keyboard research" && !menu.Visible,
            "Enter in the focused new-group editor trims and applies the name then closes the editor");

        pane.AddToGroup(pane.Tabs[1], group);
        pane.FocusAddress();
        TgKey(form, Keys.Control | Keys.Shift | Keys.F6);
        Check(pane.TabStrip.ContainsFocus, "Ctrl+Shift+F6 reaches the real tab strip from the address editor");
        // Begin at the first grouped member; one actual left-arrow reaches its preceding chip.
        TgKey(pane.TabStrip, Keys.Left);
        Check(TgChip(pane, group).Selected, "left-arrow navigation reaches the group chip without programmatic chip selection");
        TgKey(pane.TabStrip, Keys.Enter);
        Check(group.Collapsed && TgChip(pane, group).AccessibilityObject.State.HasFlag(AccessibleStates.Collapsed),
            "Enter on the keyboard-reached chip collapses it and exposes the native collapsed accessibility state");
        TgKey(pane.TabStrip, Keys.Space);
        Check(!group.Collapsed && TgChip(pane, group).AccessibilityObject.State.HasFlag(AccessibleStates.Expanded),
            "Space on the keyboard-reached chip expands it and exposes the native expanded accessibility state");
        foreach (Keys contextKey in new[] { Keys.Apps, Keys.Shift | Keys.F10 })
        {
            TgKey(pane.TabStrip, contextKey);
            menu = TgVisibleMenu("Close group");
            name = menu.Items.OfType<ToolStripTextBox>().Single();
            Check(name.Available && menu.Visible,
                contextKey + " opens the keyboard-selected chip's native group editor");
            name.Text = "Close applies";
            menu.Close();
            Check(group.Name == "Close applies", "closing a group editor applies its edited name");
            TgReachChip(form, pane, group);
        }
        var members = pane.GroupMembers(group);
        string tooltip = TgChip(pane, group).ToolTipText!;
        Check(members.All(tab => tooltip.Contains(tab.Button.Title, StringComparison.Ordinal))
            && tooltip.Contains(group.Name, StringComparison.Ordinal),
            "chip tooltip retains the full group name and every member folder title");
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            form.SetLanguage(language);
            var text = new UiText(language);
            Check(members.All(tab => tab.Button.AccessibleName == text.T("Folder tab: {0}, group: {1}", tab.Button.Title, group.Name)),
                language + " member accessible names announce folder and owning group");
        }
        form.SetLanguage("en");
    }

    private static void TgReachChip(MainForm form, ExplorerPane pane, TabGroup group)
    {
        pane.FocusAddress();
        TgKey(form, Keys.Control | Keys.Shift | Keys.F6);
        for (int i = 0; i < pane.TabStrip.Items.Count * 2 && !TgChip(pane, group).Selected; i++)
            TgKey(pane.TabStrip, Keys.Left);
        Check(pane.TabStrip.ContainsFocus && TgChip(pane, group).Selected,
            "native strip shortcut and arrow keys reach the requested chip");
    }

    private static void TgDuplicateAndSingleReopen(MainForm form, ExplorerPane pane, string[] paths)
    {
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(4).ToList() });
        var group = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], group);
        var original = pane.Tabs[0];
        TgTabMenu(pane, original, "Duplicate tab").PerformClick();
        var duplicate = pane.ActiveTab!;
        Check(duplicate != original && duplicate.Group == group && pane.IndexOfTab(duplicate) == pane.IndexOfTab(original) + 1
            && duplicate.Host?.LocationPath == original.Host?.LocationPath,
            "Duplicate tab inserts the same folder directly after its original in the same reference-identity group");
        pane.CloseTab(pane.IndexOfTab(duplicate));
        pane.SetGroupCollapsed(group, true);
        TgHistoryButton(form, pane, "Undo").PerformClick();
        Check(pane.ActiveTab?.Group == group && pane.GroupMembers(group).Count == 3 && !group.Collapsed,
            "single member undo rejoins and expands the existing group identity when it still has room");
        pane.CloseTab();
        while (pane.GroupMembers(group).Count < 12) pane.AddTabInGroup(group);
        TgHistoryButton(form, pane, "Undo").PerformClick();
        Check(pane.ActiveTab?.Group is null && pane.GroupMembers(group).Count == 12,
            "single member undo falls back to an ungrouped tab when its original group is full");
        TgTabMenu(pane, original, "Duplicate tab").PerformClick();
        Check(pane.ActiveTab?.Group is null && pane.ActiveTab?.Host?.LocationPath == original.Host?.LocationPath
            && pane.IndexOfTab(pane.ActiveTab!) == pane.IndexOfTab(pane.GroupMembers(group)[^1]) + 1,
            "duplicate of a full group's member falls back just after the group without splitting it");
        pane.SelectTab(pane.IndexOfTab(original), focus: false);
        pane.CloseTab();
        pane.Ungroup(group);
        var replacement = pane.CreateGroup(pane.Tabs[0])!;
        pane.RenameGroup(replacement, group.Name);
        TgHistoryButton(form, pane, "Undo").PerformClick();
        Check(pane.ActiveTab?.Group is null && replacement != group,
            "reopen never joins a different group merely because its name matches the removed original identity");
        while (pane.TabCount < 32) pane.AddBlankTab();
        FolderTab[] before = pane.Tabs.ToArray();
        TgTabMenu(pane, original = pane.Tabs.First(tab => tab.Host is not null), "Duplicate tab").PerformClick();
        Check(pane.Tabs.SequenceEqual(before), "duplicate at the 32-tab pane limit leaves order and membership unchanged");
        TgContiguous(pane, "duplicate and single-member undo fallbacks");
    }

    private static async Task TgWorkspaceAndPaneUndo(MainForm form, ExplorerPane pane, string[] paths)
    {
        TgResetForm(form, 2);
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(3).ToList() });
        var group = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], group);
        pane.RenameGroup(group, "Workspace collapsed");
        pane.SetGroupColor(group, TabGroupColor.Orange);
        pane.SelectTab(1, focus: false);
        pane.SetGroupCollapsed(group, true);
        form.SaveWorkspace("Collapsed groups");
        var saved = pane.CaptureState();
        pane.Ungroup(group);
        pane.AddBlankTab();
        form.LoadWorkspace("Collapsed groups");
        await NativeAppearanceDispatch(form);
        group = pane.Groups.Single();
        Check(group.Collapsed && group.Color == TabGroupColor.Orange && group.Name == "Workspace collapsed"
            && pane.CaptureState().Groups.SequenceEqual(saved.Groups) && pane.ActiveTab?.Group is null,
            "MainForm workspace save/load restores collapsed metadata and keeps selection outside the collapsed group");
        pane.SelectTab(1, focus: false);
        form.SaveWorkspace("Selected member");
        pane.SetGroupCollapsed(group, true);
        form.LoadWorkspace("Selected member");
        Check(!pane.Groups.Single().Collapsed && pane.SelectedIndex == 1 && pane.ActiveTab?.Group == pane.Groups.Single(),
            "MainForm workspace round-trip restores an expanded group's selected member");
        group = pane.Groups.Single();
        pane.SetGroupCollapsed(group, true);
        var members = pane.GroupMembers(group).ToArray();
        var layout = form.CurrentLayout;
        pane.TabStrip.Items.OfType<ToolStripButton>().Single(button => button.AccessibleName == "Pane 1 close pane").PerformClick();
        Check(!pane.Visible, "native pane close button hides the grouped pane");
        TgHistoryButton(form, null, "Undo").PerformClick();
        Check(pane.Visible && form.CurrentLayout == layout && pane.Groups.Single() == group && group.Collapsed
            && group.Color == TabGroupColor.Orange && pane.GroupMembers(group).SequenceEqual(members),
            "pane-close undo retains group identity, color, collapsed state, members and original split layout");
        form.DeleteWorkspace("Collapsed groups");
        form.DeleteWorkspace("Selected member");
    }

    private static void TgCloseOthersAndAtomicSelection(MainForm form, ExplorerPane pane, string[] paths)
    {
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(6).ToList() });
        var first = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], first);
        var last = pane.CreateGroup(pane.Tabs[4])!;
        pane.AddToGroup(pane.Tabs[5], last);
        var keep = pane.Tabs[2];
        TgTabMenu(pane, keep, "Close other tabs").PerformClick();
        Check(pane.TabCount == 1 && pane.ActiveTab == keep && !pane.Groups.Any()
            && !pane.TabStrip.Items.OfType<TabGroupChip>().Any(),
            "Close other tabs across multiple groups removes every obsolete chip and preserves only the requested tab");
        for (int count = 2; count <= 6; count++)
        {
            TgHistoryButton(form, pane, "Undo").PerformClick();
            Check(pane.TabCount == count && pane.Tabs.Contains(keep),
                "Close other tabs undo restores exactly one closed tab at step " + (count - 1));
            TgContiguous(pane, "close-other-tabs undo " + count);
        }
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(4).ToList() });
        last = pane.CreateGroup(pane.Tabs[1])!;
        pane.AddToGroup(pane.Tabs[2], last);
        pane.AddToGroup(pane.Tabs[3], last);
        pane.SelectTab(pane.TabCount - 1, focus: false);
        var outside = pane.Tabs[0];
        var initiallySelected = pane.ActiveTab;
        var observed = new List<FolderTab?>();
        void Selection(object? sender, EventArgs args) => observed.Add(pane.ActiveTab);
        pane.StateChanged += Selection;
        pane.SelectionChanged += Selection;
        try { TgOpenGroupMenu(pane, last).Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Close group").PerformClick(); }
        finally { pane.StateChanged -= Selection; pane.SelectionChanged -= Selection; }
        Check(pane.ActiveTab == outside && observed.All(tab => tab == outside || tab == initiallySelected || tab is null),
            "closing a trailing group with its last member active never publishes an intermediate closing member to tree or preview observers");
    }

    private static async Task TgRelatedTreeTabs(MainForm form, ExplorerPane pane, string[] paths)
    {
        TgResetForm(form);
        pane.ReplaceTabs(new PaneState { Tabs = [paths[0]] });
        var group = pane.CreateGroup(pane.ActiveTab!)!;
        await form.FolderTree.FollowPathAsync(paths[1]);
        form.FolderTree.RequestOpen(FolderTreeTarget.NewTab);
        Check(pane.ActiveTab?.Group == group && pane.CurrentPath == paths[1] && pane.GroupMembers(group).Count == 2,
            "folder-tree Open in new tab joins the active tab's group");
        while (pane.GroupMembers(group).Count < 12) pane.AddTabInGroup(group);
        await form.FolderTree.FollowPathAsync(paths[2]);
        form.FolderTree.RequestOpen(FolderTreeTarget.NewTab);
        Check(pane.ActiveTab?.Group is null && pane.CurrentPath == paths[2] && pane.GroupMembers(group).Count == 12,
            "related tree tab falls back ungrouped when the active group is full");
        form.RestoreFavorite(paths[3]);
        pane.SelectTab(0, focus: false);
        var favorites = Descendants(form).OfType<ListBox>().Single(list => list.AccessibleName == "Favorites");
        int favorite = Enumerable.Range(0, favorites.Items.Count).Single(index => favorites.Items[index]!.ToString()!.Contains("Folder 4", StringComparison.Ordinal));
        favorites.SelectedIndex = favorite;
        var command = favorites.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Open in new tab");
        command.PerformClick();
        Check(pane.ActiveTab?.Group is null && pane.CurrentPath == paths[3],
            "favorite Open in new tab remains ungrouped even when the active tab is a group member");
    }

    private static async Task TgNarrowAndLargeText(MainForm form, ExplorerPane pane)
    {
        TgResetForm(form);
        TgReset(pane, 5);
        var group = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], group);
        pane.SetGroupCollapsed(group, true);
        DockStyle dock = pane.Dock;
        Rectangle bounds = pane.Bounds;
        pane.Dock = DockStyle.None;
        try
        {
            // Collapsed list-button reservation while all ordinary tabs still fit (no overflow).
            pane.Width = 650;
            pane.PerformLayout();
            Check(pane.TabList.Available && pane.Tabs.Where(tab => tab.Group is null).All(tab => tab.Button.Available)
                && pane.TabStrip.Items.Cast<ToolStripItem>().Where(item => item.Available)
                    .All(item => pane.TabStrip.ClientRectangle.Contains(item.Bounds)),
                "collapsed group reserves the all-tabs button without overflowing a shrunk but fully visible tab row");
            pane.RenameGroup(group, "Research研究日本語" + new string('W', 40));
            // Reveal the chip's adjacent outside tab; unrelated overflow tabs may legitimately hide it.
            pane.SelectTab(2, focus: false);
            pane.Width = 260;
            pane.PerformLayout();
            await NativeAppearanceDispatch(form);
            var chip = TgChip(pane, group);
            Console.WriteLine($"NARROW pane={pane.Bounds} strip={pane.TabStrip.ClientRectangle} dpi={pane.TabStrip.DeviceDpi} selected={pane.SelectedIndex}"
                + $" chip={chip.Bounds}/{chip.Available}/{chip.Placement} list={pane.TabList.Bounds}/{pane.TabList.Available}"
                + " items=" + string.Join(";", pane.TabStrip.Items.Cast<ToolStripItem>().Select(item =>
                    $"{item.GetType().Name}:{item.Bounds}:{item.Available}:{item.Placement}")));
            Check(group.Name.Length == 40 && chip.Available && chip.Placement == ToolStripItemPlacement.Main
                && pane.TabStrip.ClientRectangle.Contains(chip.Bounds) && pane.TabList.Available,
                "260px pane keeps a forty-character collapsed chip and all-tabs control visible and inside the strip");
            using (var image = new Bitmap(pane.Width, pane.Height))
            {
                pane.DrawToBitmap(image, pane.ClientRectangle);
                TgSave(image, Path.Combine(TgPolishOutput(), "260px-long-collapsed-group.png"));
            }
            Font original = pane.TabStrip.Font;
            pane.Width = 650;
            foreach (int dpi in new[] { 144, 192 })
            {
                // Final-font metric coverage, not a claim of a physical monitor DPI transition.
                using var font = new Font(original.FontFamily, original.SizeInPoints * dpi / pane.TabStrip.DeviceDpi * 1.25f);
                pane.TabStrip.Font = font;
                pane.PerformLayout();
                SendMessage(pane.TabStrip.Handle, 0x02E3, 0, 0);
                await NativeAppearanceDispatch(form);
                chip = TgChip(pane, group);
                Check(chip.Available && pane.TabStrip.ClientRectangle.Contains(chip.Bounds)
                    && chip.Height >= TextRenderer.MeasureText(chip.Text, chip.Font, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height,
                    dpi + " DPI-equivalent large text leaves the collapsed chip's label vertically unclipped");
                using var image = TgPrintStrip(pane.TabStrip);
                Check(TgPixels(image, chip.Bounds, Theme.GroupTextColor(group.Color)) >= 2,
                    dpi + " DPI-equivalent large text retains rendered chip label pixels");
                TgSave(image, Path.Combine(TgPolishOutput(), $"long-collapsed-{dpi}-large-text.png"));
            }
            pane.TabStrip.Font = original;
        }
        finally { pane.Dock = dock; pane.Bounds = bounds; form.PerformLayout(); }
    }

    private static void TgKeyboardReorder(MainForm form, ExplorerPane pane)
    {
        TgResetForm(form);
        TgReset(pane, 5);
        FolderTab[] tabs = pane.Tabs.ToArray();
        var group = pane.CreateGroup(tabs[1])!;
        pane.AddToGroup(tabs[2], group);
        pane.SelectTab(3, focus: false);
        pane.FocusAddress();
        TgKey(form, Keys.Control | Keys.Shift | Keys.PageUp);
        Check(pane.ActiveTab == tabs[3] && tabs[3].Group == group && pane.GroupMembers(group).SequenceEqual(new[] { tabs[1], tabs[2], tabs[3] }),
            "Ctrl+Shift+PgUp joins the adjacent group as its last member without crossing a tab");
        TgKey(form, Keys.Control | Keys.Shift | Keys.PageUp);
        TgKey(form, Keys.Control | Keys.Shift | Keys.PageUp);
        TgKey(form, Keys.Control | Keys.Shift | Keys.PageUp);
        Check(pane.ActiveTab == tabs[3] && tabs[3].Group is null && pane.Tabs[1] == tabs[3],
            "Ctrl+Shift+PgUp crossing the first member boundary ungroups the active tab without splitting its old group");
        TgKey(form, Keys.Control | Keys.Shift | Keys.PageDown);
        Check(tabs[3].Group == group && pane.GroupMembers(group)[0] == tabs[3],
            "Ctrl+Shift+PgDn enters the first-member boundary of an expanded group");
        TgReset(pane, 1);
        var outsider = pane.ActiveTab!;
        group = pane.AddGroup(Enumerable.Repeat<string?>(null, 12).ToArray(), "Keyboard full", TabGroupColor.Blue)!;
        pane.SelectTab(0, focus: false);
        pane.FocusAddress();
        var before = pane.Tabs.ToArray();
        TgKey(form, Keys.Control | Keys.Shift | Keys.PageDown);
        Check(pane.Tabs.SequenceEqual(before) && pane.ActiveTab == outsider && outsider.Group is null,
            "Ctrl+Shift+PgDn rejects entry into a twelve-member group without changing tab order or selection");
        TgLimitStatus(pane, "keyboard group capacity");
        TgContiguous(pane, "keyboard boundary moves");
    }

    private static void TgDisabledGroupMoves(MainForm form, ExplorerPane pane)
    {
        TgResetForm(form, 2);
        TgReset(pane, 1);
        var group = pane.AddGroup([null, null], "Capacity menu", TabGroupColor.Green)!;
        TgReset(form.GetPane(1), 31);
        var menu = TgOpenGroupMenu(pane, group);
        var move = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move group to pane");
        var blocked = move.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pane 2");
        Check(!blocked.Enabled && blocked.ToolTipText?.Contains("Destination cannot hold this group.", StringComparison.Ordinal) == true,
            "group move menu disables a full destination and exposes the localized capacity reason as its tooltip");
        Check(move.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item != blocked).All(item => item.Enabled),
            "group move menu keeps available hidden destinations enabled");
        menu.Close();
    }
}
