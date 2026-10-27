using System.Runtime.InteropServices;
using QuadExplorer;

internal static partial class Program
{
    private static void RunPaneNavigationScenario(string root)
    {
        string[] folders = Enumerable.Range(1, 5).Select(n => Path.Combine(root, "navigation-folder-" + n)).ToArray();
        foreach (string folder in folders) Directory.CreateDirectory(folder);
        string session = Path.Combine(root, "pane-navigation.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ColorTheme = "light", Layout = 4, ActivePane = 0,
            ExplorerFavoritesImported = true, GlobalHotkey = false, KeepInTray = false, CheckUpdates = false,
            Panes = folders.Take(4).Select(path => new PaneState { Tabs = [path] }).ToList()
        });
        pnDelivery = new PnDelivery();
        Application.AddMessageFilter(pnDelivery);
        using (var form = new MainForm(session, []))
        {
            pnDelivery.Form = form;
            var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
            async Task Go(int index)
            {
                await PnKey(form, Keys.Alt | (Keys)((int)Keys.D1 + index));
                PnActive(form, index, "Alt+" + (index + 1));
            }
            async Task Open(int source, int target, string path)
            {
                await Go(target);
                await Go(source);
                await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                await PnReady(panes[target]);
                PnCheck(PnSame(panes[target].CurrentPath, path), "open-other destination receives the source folder");
                PnActive(form, target, "open-other success");
            }
            form.Shown += async (_, _) =>
            {
                try
                {
                    foreach (var pane in panes) await PnReady(pane);
                    form.Activate();
                    panes[0].FocusFiles();
                    foreach (int count in new[] { 2, 3, 4 })
                    {
                        ClickLayout(form, count);
                        int[] visible = form.CurrentLayout.GetPanes().Order().ToArray();
                        PnCheck(visible.Length == count, count + "-pane layout is open");
                        await Go(visible[0]);
                        for (int step = 1; step <= count; step++)
                        {
                            await PnKey(form, Keys.F6);
                            PnActive(form, visible[step % count], count + "-pane F6 step " + step);
                        }
                        for (int step = 1; step <= count; step++)
                        {
                            await PnKey(form, Keys.Shift | Keys.F6);
                            PnActive(form, visible[(count - step) % count], count + "-pane Shift+F6 step " + step);
                        }
                    }
                    await Go(3);
                    await Go(0);
                    panes[0].FocusAddress();
                    foreach (var pane in panes)
                    {
                        await ItemCountKnown(pane.ActiveView!);
                        await pane.UpdateFolderSize();
                    }
                    var beforeAltGr = panes.Select(pane => (pane.TabCount, pane.SelectedIndex, pane.CurrentPath)).ToArray();
                    var beforeAltGrStatus = panes.Select(PnStatusText).ToArray();
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O, altGr: true);
                    PnCheck(panes.Select(pane => (pane.TabCount, pane.SelectedIndex, pane.CurrentPath)).SequenceEqual(beforeAltGr)
                        && (int)typeof(MainForm).GetField("activePane", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)! == 0,
                        "AltGr+O leaves all pane tabs, selected folders and the active pane unchanged");
                    PnCheck(panes.Select(PnStatusText).SequenceEqual(beforeAltGrStatus),
                        "AltGr+O leaves every pane's displayed status unchanged");
                    PnCheck(PnGetKeyState(0xA5) >= 0, "releasing the emulated AltGr leaves right Alt up");
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    await PnReady(panes[3]);
                    PnCheck(panes[3].TabCount == beforeAltGr[3].TabCount + 1 && PnSame(panes[3].CurrentPath, folders[0]),
                        "left Ctrl plus left Alt plus O still opens the source folder in the previous pane");
                    PnActive(form, 3, "left-Alt open-other after AltGr release");
                    PnStatus(form, panes[0], "Opened this folder in pane {0}.", 4);
                    panes[3].CloseTab(remember: false);
                    foreach (int index in Enumerable.Range(0, 4)) await Go(index);
                    await PnKey(form, Keys.Shift | Keys.F6);
                    PnActive(form, 2, "Shift+F6 still cycles after AltGr press/release");
                    // The key is addressed to the edit's native HWND, not to MainForm or HandleShortcut.
                    await Go(3);
                    panes[3].FocusAddress();
                    var address = Descendants(panes[3]).OfType<TextBox>().Single(box => box.AccessibleName == "Pane 4 address");
                    string beforeAddress = address.Text;
                    PnCheck(address.Focused, "address edit owns focus before Alt+1");
                    await PnKey(form, Keys.Alt | Keys.D1);
                    PnActive(form, 0, "Alt+1 from address edit");
                    PnCheck(address.Text == beforeAddress && !address.Text.EndsWith("1", StringComparison.Ordinal),
                        "Alt+1 inserts no digit and leaves the address text unchanged");
                    panes[0].FocusAddress();
                    await PnKey(form, Keys.Shift | Keys.F6);
                    PnActive(form, 3, "Shift+F6 from address edit wraps backwards");
                    var focusButton = Descendants(panes[3]).OfType<Button>().Single(button => button.AccessibleName == "Pane 4 Pane options");
                    focusButton.Focus();
                    PnCheck(focusButton.Focused, "pane navigation button owns focus before F6");
                    await PnKey(form, Keys.F6);
                    PnActive(form, 0, "F6 from pane navigation button wraps forwards");

                    panes[2].TabStrip.Items.OfType<ToolStripButton>().Single(b => b.AccessibleName == "Pane 3 close pane").PerformClick();
                    PnCheck(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 }), "closing pane 3 leaves non-contiguous panes 1, 2 and 4");
                    await NativeAppearanceIdle(() => panes[0].FocusAddress());
                    await Go(0);
                    foreach (int target in new[] { 1, 3, 0 })
                    {
                        await PnKey(form, Keys.F6);
                        PnActive(form, target, "sparse forward cycle");
                    }
                    foreach (int target in new[] { 3, 1, 0 })
                    {
                        await PnKey(form, Keys.Shift | Keys.F6);
                        PnActive(form, target, "sparse backward cycle");
                    }
                    foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                    {
                        form.SetLanguage(language);
                        foreach (int target in new[] { 0, 1, 3 }) await Go(target);
                        int oldTabs = panes.Sum(p => p.TabCount);
                        await PnKey(form, Keys.Alt | Keys.D3);
                        PnActive(form, 3, language + " unavailable pane leaves activity unchanged");
                        PnStatus(form, panes[3], "Pane {0} is not open.", 3);
                        PnCheck(panes.Sum(p => p.TabCount) == oldTabs, language + " unavailable shortcut does not change tabs");
                        var text = new UiText(language);
                        PnCheck(new[] { "Shift+F6", "Alt+1 / Alt+2 / Alt+3 / Alt+4", "Ctrl+Alt+O" }.All(text.Help.Contains),
                            language + " help documents every new shortcut");
                    }
                    form.SetLanguage("en");
                    ClickLayout(form, 4);
                    await Go(0);
                    int targetTabs = panes[3].TabCount;
                    await Open(0, 3, folders[0]);
                    PnCheck(panes[3].TabCount == targetTabs + 1 && panes[3].SelectedIndex == targetTabs,
                        "non-adjacent previous pane receives a new selected tab without replacing its old folder");
                    panes[3].SelectTab(0);
                    PnCheck(PnSame(panes[3].CurrentPath, folders[3]), "new-tab opening preserves the destination's original folder");
                    await Go(0);
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnActive(form, 3, "already-open folder selection");
                    PnStatus(form, panes[0], "Pane {0} already shows this folder.", 4);
                    PnCheck(panes[3].TabCount == targetTabs + 1 && panes[3].SelectedIndex == targetTabs,
                        "already-open folder is selected without duplicating its tab");
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnCheck(panes[0].TabCount == 1 && panes[3].TabCount == targetTabs + 1,
                        "repeating open-other immediately does not duplicate either pane's folder");
                    PnActive(form, 0, "immediate repeat returns to the previous pane");

                    // Exercise case/trailing-separator equivalence through a real Shell navigation.
                    await PaneChange(panes[0], () => panes[0].Navigate(folders[0].ToUpperInvariant() + "\\"),
                        () => panes[0].ActiveView is { HasNavigated: true } && PnSame(panes[0].CurrentPath, folders[0]));
                    await Go(3);
                    await Go(0);
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnCheck(panes[3].TabCount == targetTabs + 1 && PnSame(panes[3].CurrentPath, folders[0]),
                        "case and trailing-backslash variants select the existing destination tab");
                    PnStatus(form, panes[0], "Pane {0} already shows this folder.", 4);

                    panes[1].AddBlankTab();
                    int blanks = panes[1].TabCount;
                    int blankIndex = panes[1].SelectedIndex;
                    await Open(0, 1, folders[0]);
                    PnCheck(panes[1].TabCount == blanks && panes[1].SelectedIndex == blankIndex,
                        "a selected blank target tab is filled without adding a tab");
                    PnStatus(form, panes[0], "Opened this folder in pane {0}.", 2);

                    // Close the newly opened tab and restore it through the real shortcut/history path.
                    await Go(3);
                    int beforeClose = panes[3].TabCount;
                    await PnKey(form, Keys.Control | Keys.W);
                    PnCheck(panes[3].TabCount == beforeClose - 1 && PnSame(panes[3].CurrentPath, folders[3]),
                        "Ctrl+W closes the tab created by open-other and reveals the original tab");
                    await PnKey(form, Keys.Control | Keys.Shift | Keys.T);
                    await PnReady(panes[3]);
                    PnCheck(panes[3].TabCount == beforeClose && PnSame(panes[3].CurrentPath, folders[0]),
                        "Ctrl+Shift+T restores the open-other tab with its folder");

                    foreach (string theme in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(theme);
                        await Go(0);
                        PnCapture(form, "navigation-active-" + theme);
                        var (menu, item) = PnOptions(form, panes[0]);
                        PnCheck(item.Enabled && item.Text == new UiText(form.Language).T("Open this folder in the other pane    Ctrl+Alt+O"),
                            theme + " options exposes the enabled localized open-other command");
                        PnCheck(menu.ClientRectangle.Contains(item.Bounds) && item.Height >= TextRenderer.MeasureText(item.Text, item.Font).Height,
                            theme + " new options item fits its visible menu row");
                        PnCapture(menu, "navigation-options-" + theme);
                        menu.Close();
                    }
                    await Go(2);
                    await Go(0);
                    int menuTabs = panes[2].TabCount;
                    var (options, openItem) = PnOptions(form, panes[0]);
                    openItem.PerformClick();
                    options.Close();
                    await PnReady(panes[2]);
                    PnCheck(panes[2].TabCount == menuTabs + 1 && PnSame(panes[2].CurrentPath, folders[0]),
                        "clicking pane options opens the source folder in the previously used pane");
                    PnActive(form, 2, "options menu success");
                    PnStatus(form, panes[0], "Opened this folder in pane {0}.", 3);

                    panes[2].AddBlankTab();
                    var (blankMenu, disabled) = PnOptions(form, panes[2]);
                    PnCheck(!disabled.Enabled, "open-other menu item is disabled for a blank source tab");
                    blankMenu.Close();
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnStatus(form, panes[2], "Open a folder first.");
                    PnCheck(panes[2].ActiveView is null, "blank source shortcut does not navigate or create a folder");
                    while (panes[2].TabCount < 32) panes[2].AddBlankTab();
                    panes[2].SelectTab(0);
                    await Go(3);
                    await Go(2);
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnCheck(panes[3].TabCount == beforeClose + 1 && PnSame(panes[3].CurrentPath, folders[2]),
                        "a populated target below capacity still accepts another source folder");
                    // Pane 2 has no folder-4 tab and a nonblank selected tab: the 32-tab boundary must reject it.
                    await Go(2);
                    await Go(3);
                    panes[3].SelectTab(0);
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnStatus(form, panes[3], "Pane {0} already has 32 tabs. Close a tab first.", 3);
                    PnCheck(panes[2].TabCount == 32 && panes[2].SelectedIndex == 0 && PnSame(panes[2].CurrentPath, folders[2]),
                        "full target keeps all 32 tabs and its selection unchanged");
                    PnActive(form, 3, "full target rejection retains source focus");
                    // Capacity must not block selecting an existing tab or filling the selected blank tab.
                    panes[3].SelectTab(beforeClose - 1);
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnCheck(panes[2].TabCount == 32 && PnSame(panes[2].CurrentPath, folders[0]),
                        "32-tab target can still select an already-open folder");
                    panes[2].SelectTab(31);
                    await Go(3);
                    panes[3].SelectTab(0);
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    await PnReady(panes[2]);
                    PnCheck(panes[2].TabCount == 32 && panes[2].SelectedIndex == 31 && PnSame(panes[2].CurrentPath, folders[3]),
                        "32-tab target can fill its selected blank tab without exceeding capacity");
                    PnActive(form, 2, "full target blank-fill success");

                    await Go(0);
                    ClickLayout(form, 1);
                    foreach (Keys key in new[] { Keys.F6, Keys.Shift | Keys.F6 })
                    {
                        await PnKey(form, key);
                        PnActive(form, 0, "single-pane cycle stays put");
                    }
                    await PnKey(form, Keys.Control | Keys.Alt | Keys.O);
                    PnStatus(form, panes[0], "There is no other pane. Choose 2, 3 or 4 panes first.");
                    PnCheck(panes[0].TabCount == 1 && form.CurrentLayout.GetPanes().SequenceEqual(new[] { 0 }),
                        "single-pane open-other failure does not open a pane or tab");
                    foreach (string language in new[] { "ko", "en" })
                    {
                        form.SetLanguage(language);
                        await PnHelp(form, language);
                    }
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL pane navigation: " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        Application.RemoveMessageFilter(pnDelivery);
        // Preserve the application's shared OtherPane rule: without history the
        // lowest-numbered other visible pane wins, not the next pane in the cycle.
        foreach (int source in new[] { 1, 0 })
        {
            pnDelivery = new PnDelivery();
            Application.AddMessageFilter(pnDelivery);
            SessionStore.Save(session, new SessionState
            {
                Language = "en", Layout = 4, ActivePane = source, ExplorerFavoritesImported = true,
                GlobalHotkey = false, KeepInTray = false, CheckUpdates = false,
                Panes = folders.Take(4).Select(path => new PaneState { Tabs = [path] }).ToList()
            });
            using var fallback = new MainForm(session, []);
            pnDelivery.Form = fallback;
            fallback.Shown += async (_, _) =>
            {
                try
                {
                    foreach (int i in Enumerable.Range(0, 4)) await PnReady(fallback.GetPane(i));
                    fallback.Activate();
                    fallback.GetPane(source).FocusFiles();
                    await PnKey(fallback, Keys.Control | Keys.Alt | Keys.O);
                    foreach (int i in Enumerable.Range(0, 4)) await PnReady(fallback.GetPane(i));
                    int target = source == 0 ? 1 : 0;
                    PnCheck(fallback.GetPane(target).TabCount == 2 && PnSame(fallback.GetPane(target).CurrentPath, folders[source])
                        && Enumerable.Range(0, 4).Where(i => i != target).All(i => fallback.GetPane(i).TabCount == 1),
                        $"without history pane {source + 1} opens in the lowest-numbered other visible pane {target + 1}");
                    PnActive(fallback, target, "first-visible fallback");
                    PnStatus(fallback, fallback.GetPane(source), "Opened this folder in pane {0}.", target + 1);
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL fallback: " + ex); }
                finally { fallback.ExitApplication(); }
            };
            Application.Run(fallback);
            Application.RemoveMessageFilter(pnDelivery);
        }
        pnDelivery = null;
    }

    private static void PnCheck(bool success, string name)
    {
        checks++;
        if (success) Console.WriteLine("PASS " + name);
        else { failures++; Console.Error.WriteLine("FAIL " + name); }
    }

    private static bool PnSame(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static string PnStatusText(ExplorerPane pane) => Descendants(pane).OfType<Label>()
        .Single(label => label.AccessibleName == $"Pane {pane.PaneNumber} status").Text;

    private static async Task PnReady(ExplorerPane pane)
    {
        if (pane.ActiveView is { HasNavigated: false } view)
            await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
        await NativeAppearanceDispatch(pane);
    }

    private static void PnActive(MainForm form, int index, string context)
    {
        var pane = form.GetPane(index);
        int active = (int)typeof(MainForm).GetField("activePane", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
        nint focus = PnGetFocus();
        bool fileFocus = pane.ActiveView is { } view
            ? focus == NativeAppearanceList(view) || PnIsChild(NativeAppearanceList(view), focus)
            : Descendants(pane).OfType<TextBox>().Any(box => box.Focused);
        using var image = new Bitmap(pane.Width, pane.Height);
        using (var graphics = Graphics.FromImage(image))
        {
            typeof(ExplorerPane).GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(pane, [new PaintEventArgs(graphics, pane.ClientRectangle)]);
        }
        bool outline = Enumerable.Range(0, pane.Padding.Left + 1).Any(x =>
        {
            Color pixel = image.GetPixel(x, pane.Height / 2);
            Color expected = pane.ActiveBorderColor;
            return Math.Abs(pixel.R - expected.R) + Math.Abs(pixel.G - expected.G) + Math.Abs(pixel.B - expected.B) <= 15;
        });
        PnCheck(active == index && pane.ContainsFocus && fileFocus && outline,
            context + $" activates pane {index + 1}, its outline and native file-list focus (active={active + 1}, focus={fileFocus}, outline={outline})");
    }

    private static void PnStatus(MainForm form, ExplorerPane pane, string key, params object?[] args)
    {
        string status = Descendants(pane).OfType<Label>().Single(label => label.AccessibleName == $"Pane {pane.PaneNumber} status").Text;
        string expected = new UiText(form.Language).T(key, args);
        PnCheck(status.Contains(expected, StringComparison.Ordinal) && !status.Contains("{0}", StringComparison.Ordinal),
            form.Language + " localized status: " + expected + " (actual: " + status + ")");
    }

    private static (ContextMenuStrip Menu, ToolStripMenuItem Item) PnOptions(MainForm form, ExplorerPane pane)
    {
        Descendants(pane).OfType<Button>().Single(button => button.AccessibleName == $"Pane {pane.PaneNumber} Pane options").PerformClick();
        var menu = (ContextMenuStrip)typeof(ExplorerPane).GetField("tabMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pane)!;
        var item = menu.Items.OfType<ToolStripMenuItem>().Single(each => each.Text == new UiText(form.Language).T("Open this folder in the other pane    Ctrl+Alt+O"));
        return (menu, item);
    }

    private static void PnCapture(Control control, string name)
    {
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is not { Length: > 0 } output) return;
        control.Refresh();
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        image.Save(Path.Combine(output, name + ".png"));
    }

    private static async Task PnHelp(MainForm form, string language)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Inspect(object? sender, EventArgs args)
        {
            var dialog = Application.OpenForms.OfType<ShortcutHelpDialog>().SingleOrDefault();
            if (dialog is null) return;
            Application.Idle -= Inspect;
            try
            {
                var content = Descendants(dialog).OfType<Panel>().Single(panel => panel.AccessibleName == "Help content");
                var navigation = Descendants(dialog).OfType<FlowLayoutPanel>().Single(panel => panel.AccessibleName == "Help sections");
                CheckHelpGeometry(dialog, content, navigation, "F1 " + language);
                var keys = Descendants(content).OfType<Label>().Where(label => label.AccessibleName == "Help shortcut").ToArray();
                PnCheck(new[] { "Shift+F6", "Alt+1 / Alt+2 / Alt+3 / Alt+4", "Ctrl+Alt+O" }.All(key => keys.Any(label => label.Text == key)),
                    "F1 " + language + " displays all new shortcut keycaps");
                navigation.Controls.OfType<Button>().First().PerformClick();
                PnCapture(dialog, "navigation-help-" + language);
                foreach (var key in keys.Where(label => label.Text.Contains("Ctrl+Alt+O", StringComparison.Ordinal)))
                    content.ScrollControlIntoView(key);
                PnCapture(dialog, "navigation-help-" + language + "-open-other");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL F1 " + language + ": " + ex); }
            finally { dialog.Close(); closed.TrySetResult(); }
        }
        Application.Idle += Inspect;
        try
        {
            await PnKey(form, Keys.F1);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { Application.Idle -= Inspect; }
    }

    // Await the real filter's return, not Application.Idle: ExplorerBrowser can pump
    // nested messages/Idle while its focus operation is still in progress.
    private static PnDelivery? pnDelivery;
    private sealed class PnDelivery : IMessageFilter
    {
        internal MainForm? Form { get; set; }
        internal nint Owner { get; set; }
        internal Keys Code { get; set; }
        internal TaskCompletionSource<bool>? Delivered { get; set; }
        internal bool? ExpectedRightAlt { get; set; }
        public bool PreFilterMessage(ref Message m)
        {
            if (Delivered is not { } completion || Form is null || m.HWnd != Owner
                || m.Msg is not (0x100 or 0x104) || m.WParam != (nint)Code) return false;
            Delivered = null;
            if (ExpectedRightAlt is { } expected && (PnGetKeyState(0xA5) < 0) != expected)
            {
                completion.TrySetException(new InvalidOperationException("Posted key's VK_RMENU state does not match the emulated AltGr/left-Alt state."));
                return true;
            }
            bool handled = Form.PreFilterMessage(ref m);
            completion.TrySetResult(handled);
            return handled;
        }
    }

    // Thread-local keyboard state plus posted HWND messages: never SendKeys/SendInput or global desktop input.
    private static async Task PnKey(MainForm form, Keys key, bool altGr = false)
    {
        nint owner = PnGetFocus();
        if (owner == 0 || !form.ContainsFocus) throw new InvalidOperationException("No focus owner for " + key);
        byte[] saved = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("GetKeyboardState failed");
        byte[] state = new byte[256];
        foreach ((Keys modifier, int code, int side) in new[] { (Keys.Control, 0x11, 0xA2), (Keys.Shift, 0x10, 0xA0),
            (Keys.Alt, 0x12, altGr ? 0xA5 : 0xA4) })
            if ((key & modifier) != 0) { state[code] = 0x80; state[side] = 0x80; }
        var delivery = pnDelivery ?? throw new InvalidOperationException("No keyboard delivery observer");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        delivery.Owner = owner;
        delivery.Code = key & Keys.KeyCode;
        delivery.Delivered = completion;
        delivery.ExpectedRightAlt = (key & (Keys.Control | Keys.Alt)) == (Keys.Control | Keys.Alt) ? altGr : null;
        try
        {
            if (!PnSetKeyboardState(state)) throw new InvalidOperationException("SetKeyboardState failed");
            if ((PnGetKeyState(0xA5) < 0) != altGr)
                throw new InvalidOperationException("GetKeyState(VK_RMENU) does not see the requested keyboard table.");
            bool alt = (key & Keys.Alt) != 0;
            if (!PostMessage(owner, alt ? 0x104u : 0x100u, (nint)(key & Keys.KeyCode), alt ? (1 << 29) | 1 : 1))
                throw new InvalidOperationException("Could not post " + key);
            bool handled = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (!handled && !altGr) throw new InvalidOperationException("The app did not consume " + key);
        }
        finally
        {
            delivery.Delivered = null;
            if (!PnSetKeyboardState(saved) || (PnGetKeyState(0xA5) < 0) != ((saved[0xA5] & 0x80) != 0))
                throw new InvalidOperationException("Could not restore the original right-Alt key state.");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetKeyState")]
    private static extern short PnGetKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetKeyboardState")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PnGetKeyboardState(byte[] state);
    [DllImport("user32.dll", EntryPoint = "SetKeyboardState")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PnSetKeyboardState(byte[] state);
    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern nint PnGetFocus();
    [DllImport("user32.dll", EntryPoint = "IsChild")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PnIsChild(nint parent, nint child);
}
