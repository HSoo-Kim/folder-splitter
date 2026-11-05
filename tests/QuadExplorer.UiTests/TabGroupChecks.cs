using QuadExplorer;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static void RunTabGroupScenario(string root)
    {
        TgTranslations();
        string folder = Directory.CreateDirectory(Path.Combine(root, "TabGroups")).FullName;
        string[] paths = Enumerable.Range(1, 8)
            .Select(i => Directory.CreateDirectory(Path.Combine(folder, $"Folder {i}")).FullName).ToArray();
        string session = Path.Combine(root, "tab-groups.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, CheckUpdates = false,
            Panes = [new PaneState { Tabs = [paths[0]] }]
        });
        PaneState? expected = null;
        PaneState? expectedMoved = null;
        using (var form = new MainForm(session, []))
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    var pane = form.GetPane(0);
                    if (pane.ActiveView is { HasNavigated: false } view)
                        await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                    TgMembership(pane);
                    TgCapacity(pane);
                    TgCollapse(form, pane);
                    TgOverflow(form, pane);
                    TgCloseHistory(form, pane, paths);
                    TgGroupMoves(form, paths);
                    TgGroupDrags(form);
                    TgAppearance(form, pane);
                    await TgPolish(form, pane, paths);

                    // Persist real folders through MainForm, with a blank group member omitted from disk.
                    pane.ReplaceTabs(new PaneState { Tabs = paths.Take(6).ToList() });
                    var first = pane.CreateGroup(pane.Tabs[0])!;
                    pane.RenameGroup(first, "Research 연구 日本語");
                    pane.SetGroupColor(first, TabGroupColor.Purple);
                    Check(pane.AddToGroup(pane.Tabs[2], first), "session fixture joins a nonadjacent folder");
                    Check(pane.AddTabInGroup(first), "session fixture includes an unsaved blank member");
                    var second = pane.CreateGroup(pane.Tabs[^2])!;
                    Check(pane.AddToGroup(pane.Tabs[^1], second), "session fixture has a second group");
                    pane.SetGroupColor(second, TabGroupColor.Orange);
                    pane.SetGroupCollapsed(second, true);
                    pane.SelectTab(1, focus: false);
                    if (pane.ActiveView is { HasNavigated: false } selected)
                        await NavigationSignal(selected).WaitAsync(TimeSpan.FromSeconds(20));
                    expected = pane.CaptureState();
                    Check(expected.Tabs.Count == 6 && expected.Groups.Count == 2
                        && expected.Groups[0].Start == 0 && expected.Groups[0].Count == 2
                        && expected.Groups[1].Start == 4 && expected.Groups[1].Count == 2
                        && expected.SelectedTab == 1,
                        "capture remaps group ranges and selected member after omitting blank tabs");
                    Check(!form.GetPane(1).Groups.Any(), "tab groups are local to their owning pane");
                    Check(form.DockGroup(pane, first, 1, null), "session fixture moves a group into a hidden pane");
                    expected = pane.CaptureState();
                    expectedMoved = form.GetPane(1).CaptureState();
                    Check(expectedMoved.Tabs.SequenceEqual(new[] { paths[0], paths[2] })
                        && expectedMoved.Groups.Single().Name == "Research 연구 日本語"
                        && expectedMoved.Groups.Single().Color == "Purple"
                        && expectedMoved.SelectedTab == 1,
                        "moved group capture preserves folder order, Unicode name, color and selected member while omitting blank");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        if (expected is null || expectedMoved is null) return;
        var saved = SessionStore.Load(session, out var warning);
        Check(warning is null && saved.Panes[0].Tabs.SequenceEqual(expected.Tabs)
            && saved.Panes[0].Groups.SequenceEqual(expected.Groups)
            && saved.Panes[0].SelectedTab == expected.SelectedTab,
            "MainForm saves group names, colors, collapsed state, ranges and selection on exit");
        Check(saved.Panes[1].Tabs.SequenceEqual(expectedMoved.Tabs)
            && saved.Panes[1].Groups.SequenceEqual(expectedMoved.Groups)
            && saved.Panes[1].SelectedTab == expectedMoved.SelectedTab,
            "MainForm saves the moved group in its destination pane");
        using (var reopened = new MainForm(session, []))
        {
            reopened.Shown += async (_, _) =>
            {
                try
                {
                    var pane = reopened.GetPane(0);
                    if (pane.ActiveView is { HasNavigated: false } view)
                        await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                    var actual = pane.CaptureState();
                    Check(actual.Tabs.SequenceEqual(expected.Tabs) && actual.Groups.SequenceEqual(expected.Groups)
                        && actual.SelectedTab == expected.SelectedTab,
                        "reopening MainForm restores the saved session end to end");
                    TgContiguous(pane, "restored session");
                    var collapsed = pane.Groups.Single(group => group.Collapsed);
                    Check(pane.GroupMembers(collapsed).All(tab => !tab.Button.Available)
                        && TgChip(pane, collapsed).Available,
                        "restored collapsed group hides members but retains its chip");
                    var moved = reopened.GetPane(1);
                    var movedState = moved.CaptureState();
                    Check(moved.Visible && movedState.Tabs.SequenceEqual(expectedMoved.Tabs)
                        && movedState.Groups.SequenceEqual(expectedMoved.Groups)
                        && movedState.SelectedTab == expectedMoved.SelectedTab
                        && moved.Groups.Single().Name == "Research 연구 日本語"
                        && moved.Groups.Single().Color == TabGroupColor.Purple
                        && collapsed.Name == "" && collapsed.Color == TabGroupColor.Orange,
                        "reopening restores moved and collapsed groups in their independent owning panes");
                    TgContiguous(moved, "restored moved group");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { reopened.ExitApplication(); }
            };
            Application.Run(reopened);
        }
        RunTabGroupRestartScenario(root, paths);
        Theme.Set("light");
    }

    private static void TgTranslations()
    {
        string[] keys = ["Add tab to new group", "Add tab to group", "Remove from group",
            "A group holds up to 12 tabs.", "Tab group name", "New tab in group", "Collapse group",
            "Expand group", "Ungroup", "Close group", "collapsed", "expanded", "Grey", "Blue",
            "Red", "Yellow", "Green", "Pink", "Purple", "Cyan", "Orange",
            "Move group to pane", "This group is no longer available.",
            "Destination cannot hold this group.\nClose tabs first.",
            "Maximum 4 panes.\nDrop in a pane's center to move the group instead.",
            "Reorder tab", "Reorder group",
            "Drop near a file-list edge to split, in the center to move, or on the tab strip to reorder.\nEsc cancels."];
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            var text = new UiText(language);
            Check(keys.All(key => !string.IsNullOrWhiteSpace(text.T(key))
                && (language == "en" || text.T(key) != key)), language + " translates every tab-group command and color");
            string label = text.T("Tab group: {0}, {1} tabs, {2}", "Research", 12, text.T("collapsed"));
            Check(label.Contains("Research") && label.Contains("12") && label.Contains(text.T("collapsed"))
                && !label.Contains("{0}"), language + " formats the accessible group name and state");
            Check(text.T("Move group to pane {0}", 3).Contains("3")
                && !text.T("Move group to pane {0}", 3).Contains("{0}"),
                language + " formats the group move destination");
        }
    }

    private static void TgReset(ExplorerPane pane, int count)
    {
        while (pane.TabCount > 0) pane.CloseTab(pane.TabCount - 1, remember: false);
        for (int i = 0; i < count; i++) pane.AddBlankTab();
        if (count > 0) pane.SelectTab(0, focus: false);
    }

    private static void TgMembership(ExplorerPane pane)
    {
        TgReset(pane, 6);
        FolderTab[] tabs = pane.Tabs.ToArray();
        TgTabMenu(pane, tabs[1], "Add tab to new group").PerformClick();
        var first = tabs[1].Group!;
        Check(first is not null && pane.Groups.Count == 1 && pane.TabCount == 6,
            "new-group menu creates a group around the existing tab without opening a tab");
        Check(pane.AddToGroup(tabs[4], first!) && pane.Tabs.SequenceEqual(new[] { tabs[0], tabs[1], tabs[4], tabs[2], tabs[3], tabs[5] }),
            "joining a group moves a later tab immediately after its last member");
        Check(pane.AddToGroup(tabs[0], first!) && pane.GroupMembers(first!).SequenceEqual(new[] { tabs[1], tabs[4], tabs[0] }),
            "joining from before a group preserves member order and contiguity");
        var second = pane.CreateGroup(tabs[5])!;
        Check(pane.AddToGroup(tabs[4], second) && pane.GroupMembers(first!).SequenceEqual(new[] { tabs[1], tabs[0] })
            && pane.GroupMembers(second).SequenceEqual(new[] { tabs[5], tabs[4] }),
            "transferring a middle member keeps both groups contiguous");
        TgContiguous(pane, "join and transfer");
        TgTabMenu(pane, tabs[1], "Remove from group").PerformClick();
        Check(tabs[1].Group is null && pane.Tabs.ToList().IndexOf(tabs[1]) == pane.Tabs.ToList().IndexOf(tabs[0]) + 1,
            "remove-group menu moves the member just after the remaining group");
        pane.RemoveFromGroup(tabs[0]);
        Check(!pane.Groups.Contains(first!) && !pane.TabStrip.Items.OfType<TabGroupChip>().Any(chip => chip.Group == first),
            "removing a group's final member removes its model and chip");
        FolderTab[] order = pane.Tabs.ToArray();
        pane.Ungroup(second);
        Check(pane.Tabs.SequenceEqual(order) && pane.Tabs.All(tab => tab.Group is null) && pane.Groups.Count == 0,
            "ungroup preserves tab order and tabs while removing the group");
        var single = pane.CreateGroup(tabs[2])!;
        pane.RenameGroup(single, "  " + new string('R', 48) + "  ");
        Check(single.Name == new string('R', 40), "group names are trimmed and limited to forty characters");
        pane.RenameGroup(single, "   ");
        Check(single.Name.Length == 0 && TgChip(pane, single).Text!.Length == 0,
            "an empty group name uses an unnamed dot chip");
        pane.CloseTab(pane.IndexOfTab(tabs[2]), remember: false);
        Check(!pane.Groups.Contains(single), "closing a group's final tab removes the empty group");
        TgContiguous(pane, "remove, ungroup and close");
    }

    private static void TgCapacity(ExplorerPane pane)
    {
        TgReset(pane, 2);
        var group = pane.CreateGroup(pane.Tabs[0])!;
        var outsider = pane.Tabs[1];
        for (int i = 1; i < TabGroup.MaxTabs; i++)
            Check(pane.AddTabInGroup(group), $"group accepts member {i + 1}");
        Check(pane.GroupMembers(group).Count == 12 && outsider.Group is null && pane.Tabs[^1] == outsider,
            "new group tabs insert before an unrelated trailing tab");
        FolderTab[] order = pane.Tabs.ToArray();
        int selected = pane.SelectedIndex;
        Check(!pane.AddToGroup(outsider, group) && pane.Tabs.SequenceEqual(order) && pane.SelectedIndex == selected,
            "a thirteenth join is rejected without moving or selecting any tab");
        TgLimitStatus(pane, "thirteenth join");
        Check(!pane.AddTabInGroup(group) && pane.Tabs.SequenceEqual(order),
            "a thirteenth new group tab is rejected without opening a tab");
        TgLimitStatus(pane, "thirteenth new tab");
        var add = TgTabMenu(pane, outsider, "Add tab to group");
        var destination = add.DropDownItems.OfType<ToolStripMenuItem>().Single();
        Check(!destination.Enabled && destination.ToolTipText == new UiText("en").T("A group holds up to 12 tabs."),
            "a full destination is disabled in the group menu and explains its limit");
        add.Owner?.Hide();
        TgContiguous(pane, "capacity rejection");
    }

    private static void TgLimitStatus(ExplorerPane pane, string action) =>
        Check(Descendants(pane).OfType<Label>().Any(label => label.Text.Contains("A group holds up to 12 tabs.", StringComparison.Ordinal)),
            action + " reports the localized twelve-tab limit in the pane status");

    private static void TgCollapse(MainForm form, ExplorerPane pane)
    {
        TgReset(pane, 4);
        FolderTab[] tabs = pane.Tabs.ToArray();
        var group = pane.CreateGroup(tabs[1])!;
        pane.AddToGroup(tabs[2], group);
        pane.RenameGroup(group, "Research");
        pane.SelectTab(pane.IndexOfTab(tabs[1]), focus: false);
        pane.SetGroupCollapsed(group, true);
        var chip = TgChip(pane, group);
        Check(group.Collapsed && pane.ActiveTab == tabs[3] && chip.Available && chip.Visible
            && pane.GroupMembers(group).All(tab => !tab.Button.Available && !tab.Button.Visible),
            "collapsing the selected group selects the next outside tab and leaves only its chip");
        Check(chip.AccessibleName == new UiText("en").T("Tab group: {0}, {1} tabs, {2}", "Research", 2, "collapsed")
            && chip.Text!.Contains('2'), "collapsed chip announces its name, member count and state");
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            form.SetLanguage(language);
            var text = new UiText(language);
            Check(chip.AccessibleName == text.T("Tab group: {0}, {1} tabs, {2}", "Research", 2, text.T("collapsed")),
                language + " refreshes an existing chip's accessible state without replacing its group");
        }
        form.SetLanguage("en");
        TgShortcut(form, Keys.Control | Keys.Shift | Keys.Tab);
        Check(pane.ActiveTab == tabs[0] && group.Collapsed, "Ctrl+Shift+Tab skips collapsed members");
        TgShortcut(form, Keys.Control | Keys.Tab);
        Check(pane.ActiveTab == tabs[3] && group.Collapsed, "Ctrl+Tab skips collapsed members");
        TgShortcut(form, Keys.Control | Keys.Tab);
        Check(pane.ActiveTab == tabs[0], "Ctrl+Tab wraps over visible tabs only");
        pane.SelectAdjacentTab(-1, wrap: false);
        Check(pane.ActiveTab == tabs[0], "nonwrapping navigation stops before the first visible tab");
        var point = pane.TabStrip.PointToScreen(new Point(pane.TabStrip.Width / 2, pane.TabStrip.Height / 2));
        var wheel = Message.Create(pane.TabStrip.Handle, 0x20A, (nint)(-120 << 16),
            (nint)((point.Y << 16) | (point.X & 0xffff)));
        Check(form.PreFilterMessage(ref wheel) && pane.ActiveTab == tabs[3] && group.Collapsed,
            "wheel navigation skips collapsed members without expanding them");
        pane.SelectTab(pane.IndexOfTab(tabs[2]), focus: false);
        Check(!group.Collapsed && pane.ActiveTab == tabs[2] && tabs[2].Button.Available,
            "programmatic selection expands and reveals a collapsed member");
        chip.PerformClick();
        Check(group.Collapsed, "clicking the group chip collapses its members");
        TgChipKey(chip, Keys.Enter);
        Check(!group.Collapsed, "Enter expands the focused chip");
        TgChipKey(chip, Keys.Space);
        Check(group.Collapsed, "Space collapses the focused chip");
        var entry = TgTabListEntry(pane, tabs[1]);
        entry.PerformClick();
        Check(!group.Collapsed && pane.ActiveTab == tabs[1] && tabs[1].Button.Available,
            "selecting a member from the all-tabs list expands its group");

        TgReset(pane, 3);
        var previous = pane.Tabs[0];
        var trailing = pane.CreateGroup(pane.Tabs[1])!;
        pane.AddToGroup(pane.Tabs[2], trailing);
        pane.SelectTab(1, focus: false);
        pane.SetGroupCollapsed(trailing, true);
        Check(pane.ActiveTab == previous && pane.TabCount == 3,
            "collapsing a trailing selected group falls back to the previous outside tab");
        TgReset(pane, 2);
        var only = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], only);
        pane.SetGroupCollapsed(only, true);
        Check(pane.TabCount == 3 && pane.ActiveTab == pane.Tabs[^1] && pane.ActiveTab!.Group is null
            && pane.ActiveView is null && only.Collapsed && TgChip(pane, only).Available,
            "collapsing a group containing every tab creates and selects one blank outside tab");
        pane.CloseTab(remember: false);
        Check(pane.TabCount == 2 && pane.ActiveTab?.Group == only && !only.Collapsed,
            "closing the only outside tab reduces the count and reveals a remaining group member");
    }

    private static void TgOverflow(MainForm form, ExplorerPane pane)
    {
        TgReset(pane, 28);
        var first = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], first);
        pane.RenameGroup(first, "Research");
        var last = pane.CreateGroup(pane.Tabs[^2])!;
        pane.AddToGroup(pane.Tabs[^1], last);
        pane.RenameGroup(last, "Archive");
        form.Size = new Size(900, 600);
        form.Refresh();
        pane.SelectTab(pane.TabCount - 1, focus: false);
        Check(pane.TabList.Available && pane.TabButtons.Count(button => button.Available) < pane.TabCount
            && pane.ActiveTab!.Button.Available && TgChip(pane, last).Available,
            "overflow keeps the selected final member and its group chip visible");
        var headers = pane.TabList.DropDownItems.OfType<ToolStripMenuItem>().Where(item => !item.Enabled).ToArray();
        Check(headers.Any(item => item.Text!.Contains("Research")) && headers.Any(item => item.Text!.Contains("Archive")),
            "the all-tabs list includes disabled headers for every group");
        Check(pane.TabList.DropDownItems.OfType<ToolStripMenuItem>().Count(item => item.Enabled) == pane.TabCount,
            "the all-tabs list retains every tab across chip-aware overflow");
        pane.SetGroupCollapsed(first, true);
        var firstEntry = TgTabListEntry(pane, pane.GroupMembers(first)[0]);
        firstEntry.PerformClick();
        Check(!first.Collapsed && pane.SelectedIndex == 0 && pane.TabButtons[0].Available && TgChip(pane, first).Available,
            "choosing an overflowed collapsed member expands and reveals its chip and tab");
        var plus = pane.TabStrip.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Pane 1 new tab");
        Check(plus.Available && pane.TabList.Bounds.Right <= pane.TabStrip.ClientSize.Width
            && pane.TabButtons.Where(button => button.Available).All(button => button.Bounds.Right <= plus.Bounds.Left),
            "chips do not push selected tabs, new-tab or all-tabs buttons beyond the strip");
        TgContiguous(pane, "overflow and expansion");
        form.Size = new Size(1400, 900);
    }


    private static void TgAppearance(MainForm form, ExplorerPane pane)
    {
        TgReset(pane, 3);
        var group = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], group);
        pane.RenameGroup(group, "Research 연구");
        pane.SelectTab(0, focus: false);
        string output = Path.Combine(Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR")
            ?? AppContext.BaseDirectory, "artifacts", "tab-groups");
        Directory.CreateDirectory(output);
        foreach (string mode in SessionStore.ColorThemes.Concat(new[] { "custom-light", "custom-dark" }))
        {
            if (mode == "custom-light") form.SetCustomColors(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
            else if (mode == "custom-dark") form.SetCustomColors(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
            else form.SetColorTheme(mode);
            foreach (var color in Enum.GetValues<TabGroupColor>())
            {
                pane.SetGroupColor(group, color);
                form.Refresh();
                var chip = TgChip(pane, group);
                Color fill = Theme.GroupColor(color), ink = Theme.GroupTextColor(color);
                Check(TgContrast(fill, ink) >= 4.5 && TgContrast(fill, Theme.Canvas) >= 3,
                    $"{mode}/{color} chip text and group marker have readable contrast");
                using var image = TgPrintStrip(pane.TabStrip);
                Rectangle middle = Rectangle.Inflate(chip.Bounds, -6, -4);
                Check(TgPixels(image, middle, fill) >= 20 && TgPixels(image, middle, ink) >= 2,
                    $"{mode}/{color} PrintWindow contains the colored chip and visible label pixels");
                foreach (var tab in pane.GroupMembers(group))
                {
                    Rectangle bounds = tab.Button.Bounds;
                    var underline = new Rectangle(bounds.Left + 6, bounds.Bottom - Math.Max(5, pane.TabStrip.DeviceDpi / 24),
                        Math.Max(1, bounds.Width - 12), Math.Max(5, pane.TabStrip.DeviceDpi / 24));
                    Check(tab.Button.GroupColor == color && TgPixels(image, underline, fill) >= underline.Width,
                        $"{mode}/{color} {(tab == pane.ActiveTab ? "selected" : "idle")} member retains its visible underline");
                }
                TgSave(image, Path.Combine(output, $"tab-groups-{mode}-{color.ToString().ToLowerInvariant()}.png"));
            }
            using (var full = PrintForm(form, 2)) TgSave(full, Path.Combine(output, $"tab-groups-{mode}-window.png"));
            pane.SetGroupCollapsed(group, true);
            form.Refresh();
            using (var collapsed = TgPrintStrip(pane.TabStrip))
                TgSave(collapsed, Path.Combine(output, $"tab-groups-{mode}-collapsed.png"));
            Check(TgChip(pane, group).Available && pane.GroupMembers(group).All(tab => !tab.Button.Available),
                mode + " collapsed capture retains the chip without member tabs");
            pane.SelectTab(0, focus: false);
        }
        form.SetColorTheme("light");
        pane.RenameGroup(group, "");
        using (var unnamed = TgPrintStrip(pane.TabStrip))
        {
            Check(TgChip(pane, group).Text!.Length == 0
                && TgPixels(unnamed, TgChip(pane, group).Bounds, Theme.GroupColor(group.Color)) > 10,
                "PrintWindow renders an unnamed expanded group as a visible colored dot");
            TgSave(unnamed, Path.Combine(output, "tab-groups-unnamed.png"));
        }
    }

    private static TabGroupChip TgChip(ExplorerPane pane, TabGroup group) =>
        pane.TabStrip.Items.OfType<TabGroupChip>().Single(chip => ReferenceEquals(chip.Group, group));

    private static ToolStripMenuItem TgTabListEntry(ExplorerPane pane, FolderTab tab) =>
        pane.TabList.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item.Enabled)
            .ElementAt(pane.IndexOfTab(tab));

    private static ToolStripMenuItem TgTabMenu(ExplorerPane pane, FolderTab tab, string key)
    {
        var menu = TgOpenTabMenu(pane, tab);
        var item = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == new UiText("en").T(key));
        menu.Close();
        return item;
    }

    private static void TgShortcut(MainForm form, Keys keys)
    {
        if (!form.ContainsFocus) form.GetPane(form.CurrentLayout.GetPanes()[0]).FocusAddress();
        TgKey(form, keys);
    }

    private static void TgChipKey(TabGroupChip chip, Keys key)
    {
        chip.Owner!.Focus();
        chip.Select();
        TgKey(chip.Owner, key);
    }

    private static void TgContiguous(ExplorerPane pane, string operation)
    {
        Check(pane.Groups.All(group =>
        {
            var members = pane.GroupMembers(group);
            if (members.Count is < 1 or > TabGroup.MaxTabs) return false;
            int start = pane.IndexOfTab(members[0]);
            return pane.Tabs.Skip(start).Take(members.Count).SequenceEqual(members)
                && pane.Tabs.Count(tab => ReferenceEquals(tab.Group, group)) == members.Count;
        }) && pane.Tabs.All(tab => tab.Group is null || pane.Groups.Contains(tab.Group)),
            operation + " preserves contiguous members and has no empty or orphaned group");
    }

    private static Bitmap TgPrintStrip(ToolStrip strip)
    {
        strip.Refresh();
        var image = new Bitmap(strip.Width, strip.Height);
        using var graphics = Graphics.FromImage(image);
        nint dc = graphics.GetHdc();
        bool rendered;
        try { rendered = PrintWindow(strip.Handle, dc, 3); }
        finally { graphics.ReleaseHdc(dc); }
        if (!rendered)
        {
            image.Dispose();
            throw new InvalidOperationException("Could not PrintWindow the tab-group strip.");
        }
        return image;
    }

    private static int TgPixels(Bitmap image, Rectangle bounds, Color color)
    {
        bounds.Intersect(new Rectangle(Point.Empty, image.Size));
        int pixels = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (image.GetPixel(x, y).ToArgb() == color.ToArgb()) pixels++;
        return pixels;
    }

    private static double TgContrast(Color a, Color b)
    {
        static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color value) => 0.2126 * Linear(value.R) + 0.7152 * Linear(value.G) + 0.0722 * Linear(value.B);
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static void TgSave(Bitmap image, string path)
    {
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("SCREENSHOT " + path);
    }
}
