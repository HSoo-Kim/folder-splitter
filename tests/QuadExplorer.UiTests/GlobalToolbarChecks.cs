using QuadExplorer;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text.Json.Nodes;

internal static partial class Program
{
    // The private test desktop has no real cursor, so Windows immediately posts
    // WM_NCMOUSELEAVE after a synthetic title-bar move. Suppress only that
    // automatic departure while testing a continuous hover.
    private sealed class HoldSyntheticCaption(nint window) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message) => message.HWnd == window && message.Msg == 0x02A2;
    }

    private static void RunGlobalToolbarScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "GlobalToolbar")).FullName;
        string file = Path.Combine(folder, "selected.txt");
        File.WriteAllText(file, "toolbar state fixture");
        string session = Path.Combine(root, "global-toolbar.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 4, GlobalHotkey = false, KeepInTray = false,
            CheckUpdates = false, AutoRestartMemory = 0, ExplorerFavoritesImported = true,
            ShowPreview = true, PreviewWidth = 200,
            Panes = Enumerable.Range(0, 4).Select(_ => new PaneState { Tabs = [folder, folder] }).ToList()
        });
        var legacy = JsonNode.Parse(File.ReadAllText(session))!.AsObject();
        legacy.Remove("GlobalToolbarPinned");
        File.WriteAllText(session, legacy.ToJsonString());
        Check(SessionStore.Load(session).GlobalToolbarPinned, "legacy sessions default to a pinned global toolbar");

        ToolStrip Bar(MainForm form) => Descendants(form).OfType<ToolStrip>().Single(s => s.AccessibleName == "Global toolbar");
        ToolStripButton Pin(MainForm form) => Bar(form).Items.OfType<ToolStripButton>().Single(i => i.Name == "GlobalToolbarPin");
        void Shortcut(MainForm form) => typeof(MainForm).GetMethod("HandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [Keys.Control | Keys.Shift | Keys.F11]);
        async Task Settle(MainForm form)
        {
            await Task.Delay(500); // The real delayed mouse-leave path must have time to run.
            await NativeAppearanceDispatch(form);
        }
        nint CaptionPoint(MainForm form)
        {
            Point point = form.PointToScreen(new Point(form.ClientSize.Width / 2, -SystemInformation.CaptionHeight / 2));
            return (nint)(((point.Y & 0xffff) << 16) | (point.X & 0xffff));
        }
        async Task Reveal(MainForm form)
        {
            var hold = new HoldSyntheticCaption(form.Handle);
            Application.AddMessageFilter(hold);
            try
            {
                PostMessage(form.Handle, 0x00A0, 2, CaptionPoint(form)); // WM_NCMOUSEMOVE, HTCAPTION
                await Task.Delay(200);
                PostMessage(form.Handle, 0x00A0, 2, CaptionPoint(form)); // moving within the caption doesn't restart the wait
                await Task.Delay(250);
                await NativeAppearanceDispatch(form);
                Check(Bar(form).Visible, "remaining on the title bar reveals the hidden toolbar");
            }
            finally { Application.RemoveMessageFilter(hold); }
            PostMessage(form.Handle, 0x02A2, 0, 0); // move from the non-client title bar to the toolbar
            PostMessage(Bar(form).Handle, 0x0200, 0, (nint)((1 << 16) | 1));
            await NativeAppearanceDispatch(form);
        }
        async Task Leave(MainForm form)
        {
            PostMessage(form.Handle, 0x02A2, 0, 0);
            PostMessage(Bar(form).Handle, 0x0200, 0, (nint)((1 << 16) | 1));
            PostMessage(Bar(form).Handle, 0x02A3, 0, 0);
            await Settle(form);
        }
        async Task UnpinAndLeave(MainForm form)
        {
            var address = Descendants(form.GetPane(0)).OfType<TextBox>()
                .Single(box => box.AccessibleName?.EndsWith(" address") == true);
            address.Focus();
            await Leave(form);
            Pin(form).PerformClick();
            await Settle(form);
        }
        async Task ClickPin(MainForm form)
        {
            var bar = Bar(form);
            var bounds = Pin(form).Bounds;
            nint point = (nint)(((bounds.Top + bounds.Height / 2) << 16) | ((bounds.Left + bounds.Width / 2) & 0xffff));
            PostMessage(bar.Handle, 0x0200, 0, point);
            PostMessage(bar.Handle, 0x0201, 1, point);
            PostMessage(bar.Handle, 0x0202, 0, point);
            await NativeAppearanceDispatch(form);
        }
        void Capture(MainForm form, string name, bool cropPin)
        {
            if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is not { Length: > 0 } output) return;
            Directory.CreateDirectory(output);
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            string path = Path.Combine(output, "global-toolbar-" + name + ".png");
            image.Save(path);
            Console.WriteLine("SCREENSHOT " + path);
            if (!cropPin || !Bar(form).Visible) return;
            var bar = Bar(form);
            using var strip = new Bitmap(bar.Width, bar.Height);
            bar.DrawToBitmap(strip, new Rectangle(Point.Empty, bar.Size));
            Rectangle bounds = Rectangle.Intersect(bar.ClientRectangle, Pin(form).Bounds);
            if (bounds.Width == 0 || bounds.Height == 0) return;
            int inset = Math.Min(3, Math.Min((bounds.Width - 1) / 2, (bounds.Height - 1) / 2));
            bounds.Inflate(-inset, -inset);
            using var zoom = new Bitmap(bounds.Width * 6, bounds.Height * 6);
            using (var graphics = Graphics.FromImage(zoom))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(strip, new Rectangle(Point.Empty, zoom.Size), bounds, GraphicsUnit.Pixel);
            }
            path = Path.Combine(output, "global-toolbar-" + name + "-pin-zoom.png");
            zoom.Save(path);
            Console.WriteLine("SCREENSHOT " + path);
        }
        void CheckPin(MainForm form, string context)
        {
            var bar = Bar(form);
            var pin = Pin(form);
            var sidebar = bar.Items.Cast<ToolStripItem>().Single(i => i.AccessibleName == "Sidebar");
            var visibleLeft = bar.Items.Cast<ToolStripItem>()
                .Where(i => i.Available && i.Placement == ToolStripItemPlacement.Main && i.Alignment == ToolStripItemAlignment.Left).ToArray();
            Check(bar.Visible && visibleLeft.Length >= 2 && ReferenceEquals(visibleLeft[0], pin)
                && ReferenceEquals(visibleLeft[1], sidebar) && pin.Bounds.Right <= sidebar.Bounds.Left
                && bar.ClientRectangle.Contains(pin.Bounds), context + " pin is the first visible left item immediately before Sidebar");
            Check(!string.IsNullOrWhiteSpace(pin.AccessibleName) && !string.IsNullOrWhiteSpace(pin.ToolTipText)
                && (pin.AccessibleDescription ?? pin.ToolTipText).Contains("Ctrl+Shift+F11", StringComparison.Ordinal),
                context + " exposes a localized accessible action and shortcut");
            Check(!bar.Items.Cast<ToolStripItem>().Where(i => !ReferenceEquals(i, pin) && i.Available
                && i.Placement == ToolStripItemPlacement.Main).Any(i => i.Bounds.IntersectsWith(pin.Bounds)),
                context + " pin does not overlap another toolbar item");
        }
        using (var form = new MainForm(session, []))
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    var pane = form.GetPane(0);
                    var host = pane.ActiveView!;
                    var initialViews = Enumerable.Range(0, 4).Select(i => form.GetPane(i).ActiveView!).ToArray();
                    await Task.WhenAll(initialViews.Select(view => view.HasNavigated ? Task.CompletedTask : NavigationSignal(view)))
                        .WaitAsync(TimeSpan.FromSeconds(20));
                    Check(host.SelectPaths([file]) == 1 && SelectedPaths(host).SequenceEqual([file]),
                        "the native file view starts with the actual fixture selected");
                    form.Preview.SetPinned(true);
                    Check(Pin(form).Checked && Bar(form).Visible, "legacy session opens with the global toolbar pinned");
                    CheckPin(form, "legacy session");
                    foreach (var layout in new[] { (1, true, true), (2, true, true), (3, true, true),
                        (3, false, false), (4, true, true) })
                    {
                        form.ApplyLayout(layout.Item1, layout.Item2, layout.Item3);
                        await NativeAppearanceDispatch(form);
                        var tree = form.CurrentLayout;
                        var tabs = Enumerable.Range(0, 4).Select(i => form.GetPane(i).CaptureState())
                            .Select(p => string.Join("|", p.Tabs) + ":" + p.SelectedTab).ToArray();
                        Rectangle pinnedArea = pane.FileAreaOnScreen;
                        if (layout.Item1 == 1)
                        {
                            await ClickPin(form);
                            await Settle(form);
                            Check(!Pin(form).Checked, "mouse click unpins the global toolbar");
                            if (Bar(form).Visible) await Leave(form);
                        }
                        else await UnpinAndLeave(form);
                        Check(!Pin(form).Checked && !Bar(form).Visible, $"layout {layout} unpin hides the actual global toolbar");
                        Rectangle hiddenArea = pane.FileAreaOnScreen;
                        Check(hiddenArea.Height > pinnedArea.Height, $"layout {layout} hidden toolbar gives workspace more height");
                        if (layout.Item1 == 1)
                        {
                            Point abovePane = form.PointToClient(new Point(pane.PointToScreen(Point.Empty).X + 10,
                                form.PointToScreen(Point.Empty).Y + 1));
                            Control top = form.GetChildAtPoint(abovePane) ?? form;
                            Point local = top.PointToClient(form.PointToScreen(abovePane));
                            PostMessage(top.Handle, 0x0200, 0, (nint)(((local.Y & 0xffff) << 16) | (local.X & 0xffff)));
                            await NativeAppearanceDispatch(form);
                            Check(!Bar(form).Visible, "hovering the top of the workspace does not reveal the toolbar");
                            PostMessage(form.Handle, 0x00A0, 20, CaptionPoint(form)); // HTCLOSE
                            await NativeAppearanceDispatch(form);
                            Check(!Bar(form).Visible, "hovering a window caption button does not reveal the toolbar");
                            Check(SendMessage(form.Handle, 0x0084, 0, CaptionPoint(form)) == 2,
                                "title-bar hover point is in the draggable native caption");
                            var hold = new HoldSyntheticCaption(form.Handle);
                            Application.AddMessageFilter(hold);
                            try
                            {
                                PostMessage(form.Handle, 0x00A0, 2, CaptionPoint(form));
                                await Task.Delay(150);
                                await NativeAppearanceDispatch(form);
                                Check(!Bar(form).Visible, "the title bar does not reveal the toolbar immediately");
                            }
                            finally { Application.RemoveMessageFilter(hold); }
                            PostMessage(form.Handle, 0x02A2, 0, 0);
                            await Task.Delay(450);
                            await NativeAppearanceDispatch(form);
                            Check(!Bar(form).Visible, "leaving the title bar before the delay cancels the reveal");
                            Application.AddMessageFilter(hold);
                            try
                            {
                                PostMessage(form.Handle, 0x00A0, 2, CaptionPoint(form));
                                await NativeAppearanceDispatch(form);
                                PostMessage(form.Handle, 0x00A0, 20, CaptionPoint(form));
                                await NativeAppearanceDispatch(form);
                            }
                            finally { Application.RemoveMessageFilter(hold); }
                            await Task.Delay(450);
                            await NativeAppearanceDispatch(form);
                            Check(!Bar(form).Visible, "moving to a window button before the delay cancels the reveal");
                            Application.AddMessageFilter(hold);
                            try
                            {
                                PostMessage(form.Handle, 0x00A0, 2, CaptionPoint(form));
                                await NativeAppearanceDispatch(form);
                                Shortcut(form);
                            }
                            finally { Application.RemoveMessageFilter(hold); }
                            Check(Pin(form).Checked && Bar(form).Visible, "keyboard pinning remains immediate during the hover delay");
                            Shortcut(form);
                            await Settle(form);
                            Check(!Pin(form).Checked && !Bar(form).Visible, "a pending hover cannot reopen after keyboard unpinning");
                        }
                        await Reveal(form);
                        Check(Bar(form).Visible && !Pin(form).Checked && Bar(form).Dock == DockStyle.None,
                            $"layout {layout} hovering the title bar reveals the same unpinned toolbar as an overlay");
                        Check(Bar(form).Width == form.ClientSize.Width,
                            $"layout {layout} hover overlay spans the full window width");
                        Check(pane.FileAreaOnScreen == hiddenArea && form.CurrentLayout == tree,
                            $"layout {layout} overlay does not resize the file area or change split proportions");
                        Check(tabs.SequenceEqual(Enumerable.Range(0, 4).Select(i => form.GetPane(i).CaptureState())
                                .Select(p => string.Join("|", p.Tabs) + ":" + p.SelectedTab))
                            && SelectedPaths(host).SequenceEqual([file]) && form.PreviewVisible && form.Preview.Pinned
                            && form.FolderTreeVisible && form.FolderTree.Visible,
                            $"layout {layout} preserves tabs, selected file, sidebar and pinned preview");
                        await Leave(form);
                        Check(!Bar(form).Visible && pane.FileAreaOnScreen == hiddenArea,
                            $"layout {layout} moving away hides overlay without reflow");
                        await Reveal(form);
                        await ClickPin(form);
                        await Settle(form);
                        Check(Pin(form).Checked && Bar(form).Visible && pane.FileAreaOnScreen == pinnedArea
                            && form.CurrentLayout == tree, $"layout {layout} clicking pin restores the fixed toolbar and layout");
                    }
                    foreach (string theme in new[] { "light", "dark" })
                    foreach (bool minimum in new[] { false, true })
                    {
                        form.SetColorTheme(theme);
                        form.Size = minimum ? form.MinimumSize : new Size(1400, 900);
                        await NativeAppearanceDispatch(form);
                        string name = theme + (minimum ? "-minimum" : "-normal");
                        CheckPin(form, name + " pinned");
                        Capture(form, name + "-pinned", true);
                        await UnpinAndLeave(form);
                        Check(!Bar(form).Visible && !Pin(form).Checked, name + " hides after unpinning");
                        Capture(form, name + "-hidden", false);
                        await Reveal(form);
                        CheckPin(form, name + " hover-revealed");
                        Capture(form, name + "-hover-revealed", true);
                        Pin(form).PerformClick();
                        await Settle(form);
                        Check(Pin(form).Checked && Bar(form).Visible, name + " stays visible when pinned again");
                        Capture(form, name + "-pinned-again", true);
                    }
                    foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                    foreach (string size in new[] { "normal", "large" })
                    {
                        form.SetLanguage(language);
                        form.SetUiFont(null, size);
                        await NativeAppearanceDispatch(form);
                        string pinnedAction = Pin(form).AccessibleName!;
                        CheckPin(form, language + " " + size + " pinned");
                        await UnpinAndLeave(form);
                        await Reveal(form);
                        CheckPin(form, language + " " + size + " unpinned");
                        Check(!Pin(form).Checked
                            && pinnedAction == new UiText(language).T("Unpin global toolbar")
                            && Pin(form).AccessibleName == new UiText(language).T("Pin global toolbar"),
                            language + " " + size + " exposes localized actions for both pin states");
                        Pin(form).PerformClick();
                    }
                    // Font-metric sizing is not a substitute for a physical monitor DPI transition.
                    var originalFont = Bar(form).Font;
                    try
                    {
                        foreach (float scale in new[] { 1.25f, 1.5f, 2f, 1f })
                        {
                            using var font = new Font(originalFont.FontFamily, originalFont.SizeInPoints * scale);
                            Bar(form).Font = font;
                            await NativeAppearanceDispatch(form);
                            CheckPin(form, "DPI-equivalent font scale " + scale);
                        }
                    }
                    finally { Bar(form).Font = originalFont; }
                    form.SetLanguage("en");
                    form.SetUiFont(null, "normal");
                    form.Size = new Size(1400, 900);
                    var bar = Bar(form);
                    await UnpinAndLeave(form);
                    await Reveal(form);
                    var menu = bar.Items.OfType<ToolStripDropDownButton>().First(i => i.Available && i.Placement == ToolStripItemPlacement.Main);
                    menu.ShowDropDown();
                    await NativeAppearanceDispatch(form);
                    Check(menu.DropDown.Visible, "global toolbar menu opens on the revealed toolbar");
                    await Leave(form);
                    Check(bar.Visible && menu.DropDown.Visible, "open toolbar menu prevents auto-hide on pointer departure");
                    menu.HideDropDown();
                    var selector = bar.Items.OfType<ToolStripComboBox>().Single();
                    selector.ComboBox.Focus();
                    await NativeAppearanceDispatch(form);
                    Check(selector.ComboBox.ContainsFocus, "toolbar language selector accepts keyboard focus");
                    await Leave(form);
                    Check(bar.Visible && selector.ComboBox.ContainsFocus, "focused toolbar input is not hidden on pointer departure");
                    selector.DroppedDown = true;
                    await NativeAppearanceDispatch(form);
                    await Leave(form);
                    Check(bar.Visible && selector.DroppedDown, "open toolbar combo is not dismissed by auto-hide");
                    selector.DroppedDown = false;
                    Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName?.EndsWith(" address") == true).Focus();
                    await Leave(form);
                    Check(!bar.Visible, "overlay hides after focus and pointer leave the toolbar");
                    Shortcut(form);
                    await NativeAppearanceDispatch(form);
                    Check(Pin(form).Checked && bar.Visible, "Ctrl+Shift+F11 shortcut restores and pins a hidden toolbar");
                    bar.Focus();
                    Pin(form).Select();
                    var enter = typeof(ToolStrip).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Check((bool)enter.Invoke(bar, [Keys.Enter])! && !Pin(form).Checked,
                        "Enter activates the focused pin without a mouse");
                    form.SetSidebarVisible(false);
                    form.SetPreviewVisible(false);
                    await Settle(form);
                    Check(!form.PreviewVisible && !form.FolderTree.Visible,
                        "unpinning does not reveal an intentionally hidden sidebar or preview");
                    pane.AddBlankTab();
                    int blankTabs = pane.TabCount;
                    Shortcut(form);
                    Check(Pin(form).Checked && pane.ActiveView is null && pane.TabCount == blankTabs,
                        "keyboard pinning preserves an empty active tab");
                    Shortcut(form);
                    Check(!Pin(form).Checked && pane.ActiveView is null && pane.TabCount == blankTabs,
                        "keyboard unpinning preserves an empty active tab");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        Check(!SessionStore.Load(session).GlobalToolbarPinned, "unpinned preference is saved to the isolated session");
        using (var reopened = new MainForm(session, []))
        {
            reopened.Shown += async (_, _) =>
            {
                try
                {
                    Check(!Pin(reopened).Checked && !Bar(reopened).Visible,
                        "a fresh window restores the hidden unpinned state");
                    await Reveal(reopened);
                    Check(Bar(reopened).Visible && !Pin(reopened).Checked, "hover restores the real toolbar after reopening");
                    Pin(reopened).PerformClick();
                    await Settle(reopened);
                    Check(Pin(reopened).Checked && Bar(reopened).Visible, "reopened toolbar can be pinned");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { reopened.ExitApplication(); }
            };
            Application.Run(reopened);
        }
        Check(SessionStore.Load(session).GlobalToolbarPinned, "pinning persists the saved choice");
        using (var reopened = new MainForm(session, []))
        {
            reopened.Shown += (_, _) =>
            {
                try { Check(Pin(reopened).Checked && Bar(reopened).Visible, "a fresh window restores the pinned state"); }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { reopened.ExitApplication(); }
            };
            Application.Run(reopened);
        }
    }
}
