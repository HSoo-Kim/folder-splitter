using QuadExplorer;

internal static partial class Program
{
    private static void RunChromeGroupScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "ChromeGroups")).FullName;
        string[] paths = Enumerable.Range(1, 8)
            .Select(number => Directory.CreateDirectory(Path.Combine(folder, $"Folder {number}")).FullName).ToArray();
        string session = Path.Combine(root, "chrome-groups.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, CheckUpdates = false,
            GlobalHotkey = false, StartWithWindows = false, Panes = [new PaneState { Tabs = [paths[0]] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try { await TgChromeRefinements(form, form.GetPane(0), paths); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + error); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }

    private static async Task TgChromeRefinements(MainForm form, ExplorerPane pane, string[] paths)
    {
        TgResetForm(form);
        await TgNamedPins(form, pane, paths);
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(6).ToList() });
        var tabs = pane.Tabs.ToArray();
        var group = pane.CreateGroup(tabs[2])!;
        pane.AddToGroup(tabs[3], group);
        TgOpenTabMenu(pane, tabs[2]).Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pin tab").PerformClick();
        Check(pane.PinnedCount == 1 && pane.Tabs[0] == tabs[2] && tabs[2].Group is null
            && pane.GroupMembers(group).SequenceEqual(new[] { tabs[3] }),
            "pin menu removes a grouped tab from its group and inserts it in the pinned prefix");
        Check(tabs[2].Button.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText && tabs[2].Button.Text!.Length > 0
            && !tabs[2].Button.PinBounds.IsEmpty && tabs[2].Button.CloseBounds.IsEmpty
            && tabs[2].Button.ToolTipText == paths[2] && tabs[2].Button.AccessibleName!.Contains("Pinned"),
            "pinned tab shows its fitted folder name and trailing pin, has no close hit target, and exposes its path and pinned accessible name");
        pane.SelectTabWithModifiers(tabs[0], Keys.None);
        pane.SelectTabWithModifiers(tabs[1], Keys.Control);
        TgRightClick(pane, tabs[1].Button);
        TgVisibleMenu("Pin tab").Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pin tab").PerformClick();
        Check(pane.PinnedCount == 3 && pane.Tabs.Take(3).SequenceEqual(new[] { tabs[2], tabs[0], tabs[1] }),
            "pin menu applies to the complete multi-selection and preserves its strip order");
        pane.ClearTabSelection();
        TgOpenTabMenu(pane, tabs[1]).Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Unpin tab").PerformClick();
        Check(pane.PinnedCount == 2 && pane.Tabs[2] == tabs[1] && !tabs[1].Pinned,
            "unpin menu places the tab immediately after the pinned prefix");
        pane.SelectTab(pane.IndexOfTab(tabs[2]), focus: false);
        TgKey(form, Keys.Control | Keys.T);
        Check(!pane.ActiveTab!.Pinned && pane.IndexOfTab(pane.ActiveTab) >= pane.PinnedCount,
            "Ctrl+T never inserts into the pinned prefix");
        pane.CloseTab(remember: false);
        pane.AddToGroup(tabs[2], group);
        Check(!tabs[2].Pinned && pane.PinnedCount == 1 && tabs[2].Group == group,
            "adding a pinned tab to a group unpins it");
        TgOpenTabMenu(pane, tabs[2]).Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pin tab").PerformClick();
        pane.SetGroupCollapsed(group, true);
        Check(pane.Tabs.Take(pane.PinnedCount).All(tab => tab.Button.Available),
            "collapsing groups never hides pinned tabs");
        pane.SelectTab(pane.IndexOfTab(tabs[3]), focus: false);
        int before = pane.TabCount;
        TgKey(form, Keys.Alt | Keys.Shift | Keys.C);
        Check(pane.TabCount == before + 1 && pane.ActiveTab!.Group == group,
            "Alt+Shift+C creates and selects a real blank member of the active group");
        TgKey(form, Keys.Alt | Keys.Shift | Keys.W);
        Check(!pane.Groups.Contains(group) && pane.TabCount == before - 1,
            "Alt+Shift+W closes the whole active group in one operation");
        TgKey(form, Keys.Control | Keys.Shift | Keys.T);
        group = pane.Groups.Single();
        Check(pane.GroupMembers(group).Count == 2, "one undo restores the entire shortcut-closed group");
        pane.SelectTab(0, focus: false);
        before = pane.TabCount;
        TgKey(form, Keys.Alt | Keys.Shift | Keys.C);
        Check(pane.TabCount == before && Descendants(pane).OfType<Label>().Any(label => label.Text.Contains("not in a group")),
            "group shortcut on an ungrouped tab reports status without changing tabs");
        TgKey(form, Keys.Alt | Keys.Shift | Keys.W);
        Check(pane.TabCount == before && Descendants(pane).OfType<Label>().Any(label => label.Text.Contains("not in a group")),
            "close-group shortcut on an ungrouped tab reports status without closing it");

        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(6).ToList(), PinnedCount = 2 });
        var firstPin = pane.Tabs[0];
        var secondPin = pane.Tabs[1];
        var keep = pane.Tabs[^1];
        TgTabMenu(pane, keep, "Close other tabs").PerformClick();
        Check(pane.TabCount == 3 && pane.Tabs.Take(2).SequenceEqual(new[] { firstPin, secondPin }) && pane.Tabs[2] == keep,
            "Close other tabs preserves every pinned tab");
        pane.SelectTab(0, focus: false);
        TgKey(form, Keys.Control | Keys.W);
        Check(pane.PinnedCount == 1 && !pane.Tabs.Contains(firstPin), "Ctrl+W closes a pinned tab");
        TgKey(form, Keys.Control | Keys.Shift | Keys.T);
        Check(pane.PinnedCount == 2 && pane.ActiveTab!.Pinned, "undo of pinned Ctrl+W restores its pin");
        firstPin = pane.ActiveTab!;
        var point = new Point(firstPin.Button.Bounds.Left + firstPin.Button.Width / 2, firstPin.Button.Bounds.Top + firstPin.Button.Height / 2);
        nint packed = (nint)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(pane.TabStrip.Handle, 0x207, 0x10, packed);
        SendMessage(pane.TabStrip.Handle, 0x208, 0, packed);
        Check(pane.PinnedCount == 1 && !pane.Tabs.Contains(firstPin), "native middle-click closes a pinned tab");

        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(6).ToList(), PinnedCount = 2 });
        group = pane.CreateGroup(pane.Tabs[2])!;
        pane.AddToGroup(pane.Tabs[3], group);
        pane.RenameGroup(group, "Research 연구");
        pane.SetGroupColor(group, TabGroupColor.Blue);
        var collapsed = pane.CreateGroup(pane.Tabs[4])!;
        pane.AddToGroup(pane.Tabs[5], collapsed);
        pane.RenameGroup(collapsed, "Archive");
        pane.SetGroupColor(collapsed, TabGroupColor.Purple);
        pane.SetGroupCollapsed(collapsed, true);
        foreach (string theme in new[] { "light", "dark" })
        {
            form.SetColorTheme(theme);
            pane.SelectTab(2, focus: false);
            if (theme == "dark")
            {
                var picker = TgOpenGroupMenu(pane, group);
                var row = picker.Items.OfType<ToolStripControlHost>().Select(item => item.Control).OfType<ToolStrip>().Single();
                row.Items.OfType<ToolStripButton>().Last().PerformClick();
                Check(group.Color == TabGroupColor.Orange, "real editor color choice selects the last dot before its ring-clipping capture");
            }
            form.Refresh();
            using (var image = TgPrintStrip(pane.TabStrip))
                TgSave(image, Path.Combine(TgPolishOutput(), "chrome-strip-" + theme + ".png"));
            var menu = TgOpenGroupMenu(pane, group);
            var name = menu.Items.OfType<ToolStripTextBox>().Single();
            Check(name.TextBox.Focused && name.TextBox.PlaceholderText == "Name this group" && name.BorderStyle == BorderStyle.None,
                theme + " editor focuses its borderless name field with localized placeholder");
            var colors = menu.Items.OfType<ToolStripControlHost>().Select(item => item.Control).OfType<ToolStrip>().Single();
            Check(colors.Items.Count == 9 && colors.Items.OfType<ToolStripButton>().Count(item => item.Checked) == 1
                && colors.Items.OfType<ToolStripButton>().Single(item => item.Checked).AccessibleName == (theme == "dark" ? "Orange" : "Blue")
                && colors.Items.Cast<ToolStripItem>().All(item => item.Available),
                theme + " editor displays all nine round colors in one row with the current color ring");
            var commands = menu.Items.OfType<ToolStripMenuItem>().ToArray();
            Check(commands.Select(item => item.Text).SequenceEqual(new[] { "New tab in group", "Move group to new window",
                "Move group to pane", "Collapse group", "Save group", "Close group", "Ungroup" })
                && commands[0].ShortcutKeyDisplayString == "Alt+Shift+C" && commands[5].ShortcutKeyDisplayString == "Alt+Shift+W",
                theme + " editor presents the requested command order and shortcut hints");
            int inset = (int)Math.Round(14 * menu.DeviceDpi / 96d);
            int fieldInset = (int)Math.Round(7 * menu.DeviceDpi / 96d);
            int fieldLeft = name.Bounds.Left - fieldInset;
            int dotLeft = menu.PointToClient(colors.PointToScreen(Point.Empty)).X + colors.Items[0].Bounds.Left;
            Console.WriteLine($"METRIC editor {theme} dpi={menu.DeviceDpi} inset={inset} field={fieldLeft} dot={dotLeft} icons={string.Join(',', commands.Select(item => item.Bounds.Left + inset))} width={menu.Width} name={name.Bounds} padding={menu.Padding}");
            Check(Math.Abs(fieldLeft - inset) <= 1 && Math.Abs(dotLeft - inset) <= 1
                && commands.All(item => Math.Abs(item.Bounds.Left) <= 1),
                theme + " editor name pill, first color dot and command icon runs share the scaled 14-DIP left inset");
            Check(Math.Abs(menu.ClientSize.Width - name.Bounds.Right - fieldInset - inset) <= 1,
                theme + " editor name pill shares the same scaled right inset");
            Check(commands.All(item => item.Bounds.Right >= menu.ClientSize.Width - inset),
                theme + " editor command rows include the full right-aligned shortcut and submenu-arrow column");
            // DWM menu popups can return a blank PrintWindow image on a non-input desktop.
            // WM_PRINT through the real control's DrawToBitmap keeps the native editor layout visible.
            using (var image = new Bitmap(menu.Width, menu.Height))
            {
                name.Text = "";
                menu.DrawToBitmap(image, menu.ClientRectangle);
                var nameBounds = new Rectangle(menu.PointToClient(name.TextBox.PointToScreen(Point.Empty)), name.TextBox.Size);
                name.TextBox.DrawToBitmap(image, nameBounds);
                Check(TgPixels(image, nameBounds, Theme.Muted) > 8 && TgContrast(Theme.Muted, name.BackColor) >= 4.5,
                    theme + " focused empty native name field paints its localized placeholder at readable contrast");
                Check(TgPixels(image, new Rectangle(Point.Empty, image.Size), Theme.GroupColor(group.Color)) > 8,
                    theme + " editor capture contains the selected color dot and its colored ring");
                TgSave(image, Path.Combine(TgPolishOutput(), "chrome-editor-" + theme + ".png"));
                TgEditorIconPixels(menu, image, theme);
                TgEditorColorPixels(menu, colors, image, theme + " normal font");
                name.Text = group.Name;
            }
            commands[0].Enabled = false;
            using (var disabled = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(disabled, menu.ClientRectangle);
                TgEditorIconPixels(menu, disabled, theme + " disabled");
            }
            commands[0].Enabled = true;
            menu.Close();
            await NativeAppearanceDispatch(form);
            foreach (string size in new[] { "small", "large" })
            {
                form.SetUiFont(null, size);
                var fontMenu = TgOpenGroupMenu(pane, group);
                Check(Math.Abs(fontMenu.Font.SizeInPoints - form.Font.SizeInPoints) < .01,
                    theme + " " + size + " editor uses the actual selected app text size");
                var fontColors = fontMenu.Items.OfType<ToolStripControlHost>().Select(item => item.Control).OfType<ToolStrip>().Single();
                using (var image = new Bitmap(fontMenu.Width, fontMenu.Height))
                {
                    fontMenu.DrawToBitmap(image, fontMenu.ClientRectangle);
                    TgEditorColorPixels(fontMenu, fontColors, image, theme + " " + size + " font");
                }
                fontMenu.Close();
                await NativeAppearanceDispatch(form);
            }
            form.SetUiFont(null, "normal");
        }
        form.SetColorTheme("light");
        TgOpenGroupMenu(pane, group).Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Save group").PerformClick();
        string savedId = group.SavedId!;
        Check(savedId is not null && form.ApplicationContext.SavedGroups.Any(saved => saved.Id == savedId),
            "group editor save creates the saved copy through the real menu");
        before = pane.TabCount;
        var savedMenu = TgOpenGroupMenu(pane, group);
        using (var savedImage = new Bitmap(savedMenu.Width, savedMenu.Height))
        {
            savedMenu.DrawToBitmap(savedImage, savedMenu.ClientRectangle);
            TgEditorIconPixels(savedMenu, savedImage, "saved group including check and delete");
        }
        savedMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Delete group").PerformClick();
        Check(pane.TabCount == before - 2 && !pane.Groups.Contains(group)
            && form.ApplicationContext.SavedGroups.All(saved => saved.Id != savedId),
            "saved-only Delete group closes all members and removes its saved copy");
        TgKey(form, Keys.Control | Keys.Shift | Keys.T);
        Check(pane.TabCount == before && pane.Groups.Any(candidate => candidate.Name == "Research 연구" && candidate.SavedId is null)
            && form.ApplicationContext.SavedGroups.All(saved => saved.Id != savedId),
            "one undo restores the deleted group tabs without recreating the deleted saved copy");
        int savedPins = pane.CaptureState().PinnedCount;
        form.SaveWorkspace("Pinned workspace");
        pane.ReplaceTabs(new PaneState { Tabs = [paths[7]] });
        form.LoadWorkspace("Pinned workspace");
        Check(pane.PinnedCount == savedPins && pane.CaptureState().PinnedCount == savedPins,
            "workspace save and load retain the pinned prefix");
        form.Size = new Size(920, 760);
        while (pane.TabCount < 32) pane.AddBlankTab();
        Check(pane.PinnedCount == 2 && pane.Tabs.Take(2).All(tab => tab.Button.Available)
            && pane.TabList.Visible && pane.TabList.DropDownItems.OfType<ToolStripMenuItem>().All(item => !item.AccessibleName!.Contains("Pinned tab:")),
            "at 32 tabs overflow leaves both pins on the strip and excludes them from overflow entries");
        before = pane.TabCount;
        TgKey(form, Keys.Control | Keys.T);
        Check(pane.TabCount == before, "pinned tabs still count toward the 32-tab limit");
        form.Size = new Size(1400, 900);

        TgResetForm(form, 2);
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(3).ToList(), PinnedCount = 1 });
        var pin = pane.Tabs[0];
        var target = form.GetPane(1);
        var tabMenu = TgOpenTabMenu(pane, pin);
        tabMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Move tab to pane").DropDownItems
            .OfType<ToolStripMenuItem>().Single(item => item.Text == "Pane 2").PerformClick();
        Check(pane.PinnedCount == 0 && target.PinnedCount == 1 && target.Tabs[0].Host!.LocationPath == paths[0],
            "moving a pinned tab to another pane keeps it pinned in the receiving prefix");
        TgOpenTabMenu(target, target.Tabs[0]).Items.OfType<ToolStripMenuItem>()
            .Single(item => item.Text == "Move tab to new window").PerformClick();
        var peer = form.ApplicationContext.Windows.Single(window => window != form);
        await MultiWindowReady(peer);
        Check(peer.GetPane(0).PinnedCount == 1 && peer.GetPane(0).Tabs[0].Host!.LocationPath == paths[0],
            "moving a pinned tab to a new peer window retains its pinned prefix");
        peer.Close();
        form.Activate();

        TgResetForm(form);
        pane.ReplaceTabs(new PaneState { Tabs = paths.Take(4).ToList(), PinnedCount = 1 });
        var transport = new HarmonyLib.Harmony("FolderSplitter.ChromePins");
        var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop), [typeof(object), typeof(DragDropEffects)]);
        transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
        try
        {
            var moving = pane.Tabs[2];
            point = pane.TabStrip.PointToScreen(new Point(pane.Tabs[0].Button.Bounds.Left + 1, pane.TabStrip.Height / 2));
            TgInterceptDrag(form, pane, null, moving, point);
            Check(pane.PinnedCount == 2 && moving.Pinned && pane.Tabs[0] == moving,
                "real strip drop into the pinned prefix pins the dragged tab");
            var last = pane.Tabs[^1].Button;
            point = pane.TabStrip.PointToScreen(new Point(last.Bounds.Right - 1, pane.TabStrip.Height / 2));
            TgInterceptDrag(form, pane, null, moving, point);
            Check(pane.PinnedCount == 1 && !moving.Pinned && pane.Tabs[^1] == moving,
                "real strip drop out of the pinned prefix unpins the dragged tab");
        }
        finally { blankTabDrop = null; transport.Unpatch(method, HarmonyLib.HarmonyPatchType.All, transport.Id); }
        TgResetForm(form);
    }

    private static async Task TgNamedPins(MainForm form, ExplorerPane pane, string[] paths)
    {
        string[] named = paths.Take(2).Select((path, index) =>
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "Pinned research folder " + index + new string('W', 24))).FullName)
            .Concat(paths.Skip(2).Take(4)).ToArray();
        pane.ReplaceTabs(new PaneState { Tabs = named.ToList(), PinnedCount = 2 });
        var pins = pane.Tabs.Take(2).ToArray();
        var group = pane.CreateGroup(pane.Tabs[2])!;
        pane.AddToGroup(pane.Tabs[3], group);
        pane.RenameGroup(group, "Research");
        pane.SelectTab(0, focus: false);
        foreach (string mode in new[] { "light", "dark", "custom-light", "custom-dark" })
        {
            if (mode == "custom-light") form.SetCustomColors(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
            else if (mode == "custom-dark") form.SetCustomColors(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
            else form.SetColorTheme(mode);
            await NativeAppearanceDispatch(form);
            // Move native hover off the strip so active and inactive glyph colors are distinct.
            SendMessage(pane.TabStrip.Handle, 0x2A3, 0, 0);
            using var image = TgPrintStrip(pane.TabStrip);
            foreach (var tab in pins)
            {
                var button = tab.Button;
                Check(button.Available && button.Text!.Length > 0 && button.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText
                    && button.CloseBounds.IsEmpty && !button.PinBounds.IsEmpty && tab.Group is null
                    && button.Width <= (int)Math.Round(180 * pane.TabStrip.DeviceDpi / 96d)
                    && button.Padding.Right == button.PinBounds.Width,
                    mode + " named pin keeps a fitted label before its pin-only action slot within the scaled 180-DIP maximum");
                TgPinPixels(image, button, mode);
            }
            TgSave(image, Path.Combine(TgPolishOutput(), "named-pins-" + mode + ".png"));
        }
        form.SetColorTheme("light");
        var inactive = pins[1];
        TgPinMouse(pane, inactive.Button, action: false);
        Check(pane.ActiveTab == inactive && inactive.Pinned, "native click on a pinned name selects the tab without unpinning");
        TgPinMouse(pane, inactive.Button, action: true, hoverOnly: true);
        Check(inactive.Button.ToolTipText == "Unpin tab", "native pointer over the pin shows the localized unpin action tooltip");
        TgPinMouse(pane, inactive.Button, action: true);
        Check(!inactive.Pinned && pane.PinnedCount == 1 && pane.Tabs[1] == inactive
            && !inactive.Button.CloseBounds.IsEmpty && inactive.Button.PinBounds.IsEmpty,
            "native click on the pin unpins into the first position after the prefix and restores its close target");
        pane.SetTabsPinned([inactive], true);
        foreach (var (language, label) in new[] { ("ko", "고정 해제"), ("en", "Unpin tab"), ("zh-CN", "取消固定"), ("ja", "固定を解除") })
            Check(new UiText(language).T("Unpin tab") == label, language + " pin tooltip and tab menu share the localized unpin action");

        DockStyle dock = pane.Dock;
        Rectangle bounds = pane.Bounds;
        pane.Dock = DockStyle.None;
        try
        {
            pane.Width = 760;
            pane.PerformLayout();
            await NativeAppearanceDispatch(form);
            Check(pins.All(tab => tab.Button.Text!.EndsWith('\u2026') && tab.Button.Text.Length > 1
                && tab.Button.Available && pane.TabStrip.ClientRectangle.Contains(tab.Button.Bounds)),
                "crowded pinned names shrink with ellipsis before ordinary tabs and remain a visible in-bounds prefix");
            using (var image = TgPrintStrip(pane.TabStrip))
                TgSave(image, Path.Combine(TgPolishOutput(), "named-pins-crowded.png"));
            pane.Width = 420;
            pane.PerformLayout();
            await NativeAppearanceDispatch(form);
            Check(pins.All(tab => tab.Button.Available && tab.Button.Text == "" && !tab.Button.PinBounds.IsEmpty
                && pane.TabStrip.ClientRectangle.Contains(tab.Button.Bounds)
                && tab.Button.Width >= tab.Button.ActionWidth + pane.TabStrip.ImageScalingSize.Width),
                "narrowest strip collapses pinned names to a complete folder icon plus pin without overflowing the prefix");
            using (var image = TgPrintStrip(pane.TabStrip))
                TgSave(image, Path.Combine(TgPolishOutput(), "named-pins-icon-pin.png"));
        }
        finally { pane.Dock = dock; pane.Bounds = bounds; form.PerformLayout(); }
        TgResetForm(form);
    }

    private static void TgPinMouse(ExplorerPane pane, FolderTabButton button, bool action, bool hoverOnly = false)
    {
        var slot = action ? button.PinBounds : new Rectangle(0, 0, button.Width - button.ActionWidth, button.Height);
        var point = new Point(button.Bounds.Left + slot.Left + slot.Width / 2, button.Bounds.Top + slot.Height / 2);
        nint packed = (nint)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(pane.TabStrip.Handle, 0x200, 0, packed);
        if (hoverOnly) return;
        SendMessage(pane.TabStrip.Handle, 0x201, 1, packed);
        SendMessage(pane.TabStrip.Handle, 0x202, 0, packed);
    }

    private static void TgPinPixels(Bitmap image, FolderTabButton button, string context)
    {
        var slot = button.PinBounds;
        slot.Offset(button.Bounds.Location);
        // Scan only the icon interior; rounded tab edges and the divider are not icon pixels.
        slot.Inflate(-(int)Math.Round(5 * button.Owner!.DeviceDpi / 96d), -5);
        Color foreground = button.Checked || button.Selected ? Theme.Text : Theme.Muted;
        Color background = image.GetPixel(slot.Left, slot.Top);
        int dr = foreground.R - background.R, dg = foreground.G - background.G, db = foreground.B - background.B;
        int channel = Math.Abs(dr) >= Math.Abs(dg) && Math.Abs(dr) >= Math.Abs(db) ? 0 : Math.Abs(dg) >= Math.Abs(db) ? 1 : 2;
        int delta = channel == 0 ? dr : channel == 1 ? dg : db;
        int ink = 0, fringes = 0;
        for (int y = slot.Top; y < slot.Bottom; y++)
            for (int x = slot.Left; x < slot.Right; x++)
            {
                Color pixel = image.GetPixel(x, y);
                double coverage = (channel == 0 ? pixel.R - background.R : channel == 1 ? pixel.G - background.G : pixel.B - background.B) / (double)delta;
                if (coverage >= .75) ink++;
                if (coverage < -.02 || coverage > 1.02 || Math.Abs(pixel.R - (background.R + dr * coverage)) > 3
                    || Math.Abs(pixel.G - (background.G + dg * coverage)) > 3 || Math.Abs(pixel.B - (background.B + db * coverage)) > 3) fringes++;
            }
        Check(ink > 3 && fringes == 0 && TgContrast(foreground, background) >= 3,
            context + (button.Checked ? " active" : " inactive") + $" pin is readable monochrome text-color ink without colored fringes (ink={ink}, fringes={fringes})");
    }

    private static void TgEditorColorPixels(ContextMenuStrip menu, ToolStrip colors, Bitmap image, string context)
    {
        int inset = (int)Math.Round(14 * menu.DeviceDpi / 96d);
        int diameter = (int)Math.Round(16 * menu.DeviceDpi / 96d);
        Console.WriteLine($"METRIC color_row {context} dpi={menu.DeviceDpi} font_points={menu.Font.SizeInPoints} row_width={colors.Width}");
        Point origin = menu.PointToClient(colors.PointToScreen(Point.Empty));
        var content = new Rectangle(inset, origin.Y, menu.ClientSize.Width - inset * 2, colors.Height);
        var painted = new List<Rectangle>();
        for (int index = 0; index < colors.Items.Count; index++)
        {
            var item = colors.Items[index];
            var slot = item.Bounds;
            slot.Offset(origin);
            var fullCircle = new Rectangle(content.Left + (content.Width - diameter) * index / 8,
                origin.Y + (item.Height - diameter) / 2, diameter, diameter);
            Color foreground = Theme.GroupColor((TabGroupColor)index), background = colors.BackColor;
            int dr = foreground.R - background.R, dg = foreground.G - background.G, db = foreground.B - background.B;
            int channel = Math.Abs(dr) >= Math.Abs(dg) && Math.Abs(dr) >= Math.Abs(db) ? 0 : Math.Abs(dg) >= Math.Abs(db) ? 1 : 2;
            int delta = channel == 0 ? dr : channel == 1 ? dg : db;
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            var scan = Rectangle.Intersect(slot, new Rectangle(Point.Empty, image.Size));
            for (int y = scan.Top; y < scan.Bottom; y++)
                for (int x = scan.Left; x < scan.Right; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    double coverage = (channel == 0 ? pixel.R - background.R : channel == 1 ? pixel.G - background.G : pixel.B - background.B) / (double)delta;
                    if (coverage < .04 || coverage > 1.02
                        || Math.Abs(pixel.R - (background.R + dr * coverage)) > 3
                        || Math.Abs(pixel.G - (background.G + dg * coverage)) > 3
                        || Math.Abs(pixel.B - (background.B + db * coverage)) > 3) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
            var ink = right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            Check(item.Available && content.Contains(fullCircle) && slot.Contains(fullCircle) && content.Contains(ink)
                && ink.Width >= diameter - 1 && ink.Height >= diameter - 1
                && Math.Abs(ink.Left - fullCircle.Left) <= 1 && Math.Abs(ink.Right - fullCircle.Right) <= 1,
                context + " " + item.AccessibleName + $" full circle{(item is ToolStripButton { Checked: true } ? " and selection ring" : "")} stays inside its hit target and content bounds (circle={fullCircle}, painted={ink}, slot={slot})");
            painted.Add(ink);
        }
        var gaps = painted.Skip(1).Select((bounds, index) => bounds.Left - painted[index].Right).ToArray();
        Check(Math.Abs(painted[0].Left - content.Left) <= 1 && Math.Abs(painted[^1].Right - content.Right) <= 1
            && gaps.Max() - gaps.Min() <= 2,
            context + " nine complete dots span the matching left/right content insets with equal gaps");
    }

    private static void TgEditorIconPixels(ContextMenuStrip menu, Bitmap image, string context)
    {
        int width = (int)Math.Round(18 * menu.DeviceDpi / 96d);
        int inset = (int)Math.Round(14 * menu.DeviceDpi / 96d);
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            Color foreground = item.Enabled ? Theme.Text : Theme.Muted;
            Color background = menu.BackColor;
            if (item.Enabled && (item.Selected || item.Pressed)) background = item.Pressed ? Theme.Selected : Theme.Hover;
            int dr = foreground.R - background.R, dg = foreground.G - background.G, db = foreground.B - background.B;
            int channel = Math.Abs(dr) >= Math.Abs(dg) && Math.Abs(dr) >= Math.Abs(db) ? 0 : Math.Abs(dg) >= Math.Abs(db) ? 1 : 2;
            int delta = channel == 0 ? dr : channel == 1 ? dg : db;
            int ink = 0, coloredFringes = 0, firstInk = int.MaxValue;
            var bounds = Rectangle.Intersect(new Rectangle(inset, item.Bounds.Top, width, item.Height),
                new Rectangle(Point.Empty, image.Size));
            for (int y = bounds.Top; y < bounds.Bottom; y++)
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    if (pixel.ToArgb() == background.ToArgb()) continue;
                    double coverage = (channel == 0 ? pixel.R - background.R : channel == 1 ? pixel.G - background.G : pixel.B - background.B) / (double)delta;
                    if (coverage < -.02 || coverage > 1.02
                        || Math.Abs(pixel.R - (background.R + dr * coverage)) > 3
                        || Math.Abs(pixel.G - (background.G + dg * coverage)) > 3
                        || Math.Abs(pixel.B - (background.B + db * coverage)) > 3) coloredFringes++;
                    if (coverage >= .75) ink++;
                    firstInk = Math.Min(firstInk, x);
                }
            Check(ink > 3 && coloredFringes == 0 && Math.Abs(firstInk - inset) <= 1,
                context + " " + item.Text + $" icon is a visible left-aligned monochrome run in the item text color without colored fringes (ink={ink}, fringes={coloredFringes}, left={firstInk}, expected={inset})");
        }
    }
}
