using QuadExplorer;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class Program
{
    private static void RunRenameScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "Rename")).FullName;
        string session = Path.Combine(root, "rename.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, ExplorerFavoritesImported = true, ShowCommandBar = true,
            ColorTheme = SessionStore.CustomTheme, CustomAccent = "#63A3D9", CustomBackground = "#20252A",
            CheckUpdates = false, GlobalHotkey = false, KeepInTray = false, AutoRestartMemory = 0,
            Panes = [new PaneState { Tabs = [fixture] }]
        });
        using var form = new MainForm(session, []);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                await OitReady(pane.ActiveView!);
                Console.WriteLine("RENAME_VERSION " + typeof(MainForm).Assembly.GetName().Version);
                await RenameKeyboardScenario(form, pane, fixture);
                foreach (bool comfortable in new[] { false, true })
                foreach (string kind in new[] { "file", "folder", "image", "pdf", "text" })
                foreach (string start in new[] { "f2", "button", "menu", "slow" })
                {
                    pane.SetDetailsAppearance(false, comfortable);
                    form.SetPreviewVisible(kind is "image" or "pdf" or "text");
                    form.SetDetailsPaneVisible(kind == "text");
                    string suffix = kind == "image" ? ".png" : kind == "pdf" ? ".pdf" : kind == "folder" ? "" : ".txt";
                    string path = Path.Combine(fixture, $"{kind}-{comfortable}-{start}{suffix}");
                    if (kind == "folder") Directory.CreateDirectory(path);
                    else if (kind == "image") { using var bitmap = new Bitmap(20, 20); bitmap.Save(path); }
                    else if (kind == "pdf") WritePdfFixture(path);
                    else File.WriteAllText(path, "Rename preview fixture.");
                    pane.ActiveView!.RefreshFolder();
                    await RenameWait(() => ListedPaths(pane.ActiveView!).Contains(path), "fixture enumeration");
                    try { await RenameOne(form, pane, path, start, clickCommit: start is "button" or "slow"); }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine($"FAIL RENAME_CASE {kind} comfortable={comfortable} start={start}: {ex.Message}");
                        nint edit = SendMessage(NativeAppearanceList(pane.ActiveView!), 0x1018, 0, 0);
                        if (edit != 0) await RenameKey(edit, Keys.Escape);
                    }
                }
                await RenameLocationScenario(form, pane, fixture, root);
                if (typeof(MainForm).Assembly.GetName().Version!.Build >= 21)
                    await RenameGroupedWindowScenario(form, fixture);
            }
            catch (Exception ex) { failure = ex; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Rename scenario failed.", failure);
    }

    private static async Task RenameOne(MainForm form, ExplorerPane pane, string path, string start, bool clickCommit)
    {
        var host = pane.ActiveView!;
        if (ShellViewHost.IsRecent(host.LocationPath))
        {
            int index = Array.FindIndex(ListedPaths(host), item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
            Marshal.ThrowExceptionForHR(FolderView(host).SelectItem(index, 0x1D));
            Check(host.SelectedPaths().Paths.Contains(path), "rename selects owned Recent target " + Path.GetFileName(path));
        }
        else Check(host.SelectPaths([path]) == 1, "rename selects owned item " + Path.GetFileName(path));
        form.Activate();
        host.FocusView();
        nint list = NativeAppearanceList(host);
        ShellNative.SetFocus(list);
        await Task.Delay(600);
        if (form.Preview.Visible)
        {
            Console.WriteLine($"RENAME_PREVIEW extension={Path.GetExtension(path)} handler={PreviewHandlers.Find(Path.GetExtension(path))} handler_loaded={form.Preview.HasHandler} image={form.Preview.HasImage} path={form.Preview.CurrentPath}");
            if (form.Preview.Pdf is { } pdf) await pdf.Rendering.WaitAsync(TimeSpan.FromSeconds(15));
        }
        if (start == "f2") await RenameKey(list, Keys.F2);
        else if (start == "button")
        {
            Descendants(pane).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                .Single(item => item.AccessibleName == $"Pane {pane.PaneNumber} Rename").PerformClick();
        }
        else if (start == "menu") await RenameMenu(host);
        else
        {
            var point = OitRowPoint(host, path);
            await Task.Delay(SystemInformation.DoubleClickTime + 150);
            await OitMouse(list, point, 0x201, 0x202, Keys.None);
            await Task.Delay(1000);
        }
        nint edit = SendMessage(list, 0x1018, 0, 0);
        Check(edit != 0, $"{start} opens native label edit for {Path.GetFileName(path)}");
        if (edit == 0) return;
        NativeAppearanceGetClientRect(edit, out var editBounds);
        Check(editBounds.Right > 0 && editBounds.Bottom > 0 && SendMessage(edit, 0x0031, 0, 0) != 0,
            "native label editor has a usable size and text font at the current DPI");
        string name = "renamed-" + Path.GetFileName(path);
        RenameSetText(edit, name);
        if (clickCommit)
        {
            NativeAppearanceGetClientRect(list, out var client);
            await OitMouse(list, new Point(client.Right - 8, client.Bottom - 8), 0x201, 0x202, Keys.None);
        }
        else await RenameKey(edit, Keys.Enter);
        string target = Path.Combine(Path.GetDirectoryName(path)!, name);
        await RenameWait(() => File.Exists(target) || Directory.Exists(target), "rename commit " + Path.GetFileName(path));
        Check((File.Exists(target) || Directory.Exists(target)) && !File.Exists(path) && !Directory.Exists(path),
            $"{start} commits {Path.GetFileName(path)} via {(clickCommit ? "click" : "Enter")}");
        edit = SendMessage(list, 0x1018, 0, 0);
        if (edit != 0) await RenameKey(edit, Keys.Escape);
    }

    private static async Task RenameKeyboardScenario(MainForm form, ExplorerPane pane, string fixture)
    {
        string path = Path.Combine(fixture, "keyboard.txt");
        File.WriteAllText(path, "Keyboard rename fixture.");
        pane.ActiveView!.RefreshFolder();
        await RenameWait(() => ListedPaths(pane.ActiveView!).Contains(path), "keyboard enumeration");
        pane.AddTab(fixture, select: false);
        var host = pane.ActiveView!;
        host.SelectPaths([path]);
        form.Activate();
        host.FocusView();
        nint list = NativeAppearanceList(host);
        ShellNative.SetFocus(list);
        await RenameKey(list, Keys.F2);
        nint edit = SendMessage(list, 0x1018, 0, 0);
        Check(edit != 0, "keyboard regression uses the real Shell label editor");
        RenameSetText(edit, "abcd");
        SendMessage(edit, 0x00B1, 4, 4); // EM_SETSEL
        PostMessage(edit, 0x0102, 'x', 1);
        await Task.Delay(120);
        Check(RenameText(edit) == "abcdx", "character input edits the filename instead of starting list search");
        await RenameKey(edit, Keys.Left);
        await RenameKey(edit, Keys.Delete);
        Check(RenameText(edit) == "abcd", "arrow and Delete keys edit only the filename");
        await RenameKey(edit, Keys.Back);
        Check(RenameText(edit) == "abc", $"Backspace edits only the filename (actual={RenameText(edit)}, location={host.LocationPath})");
        await RenameKey(edit, Keys.Control | Keys.A);
        long selection = (long)SendMessage(edit, 0x00B0, 0, 0); // EM_GETSEL
        Check((selection & 0xffff) == 0 && ((selection >> 16) & 0xffff) == 3,
            "Ctrl+A selects filename text, not Shell rows");
        PostMessage(edit, 0x0102, 'z', 1);
        await Task.Delay(120);
        await RenameKey(edit, Keys.Control | Keys.Z);
        Check(RenameText(edit) != "z" && File.Exists(path) && SendMessage(list, 0x1018, 0, 0) == edit,
            $"Ctrl+Z undoes filename text without a file operation (actual={RenameText(edit)})");
        RenameSetText(edit, Path.GetFileName(path));
        foreach (Keys key in new[] { Keys.Control | Keys.L, Keys.Control | Keys.Shift | Keys.PageDown })
        {
            await RenameKey(edit, key);
            Console.WriteLine($"RENAME_SHORTCUT key={key} focus={PnGetFocus():X} edit={edit:X} tabs={pane.TabCount}");
            try { Check(PnGetFocus() == edit && SendMessage(list, 0x1018, 0, 0) == edit,
                $"{key} does not steal the Shell label editor"); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL RENAME_KEYS " + ex.Message); }
            if (SendMessage(list, 0x1018, 0, 0) != edit)
            {
                host.FocusView();
                host.SelectPaths([path]);
                ShellNative.SetFocus(list);
                await RenameKey(list, Keys.F2);
                edit = SendMessage(list, 0x1018, 0, 0);
            }
        }
        if (edit != 0) { RenameSetText(edit, "cancelled.txt"); await RenameKey(edit, Keys.Escape); }
        Check(File.Exists(path) && !File.Exists(Path.Combine(fixture, "cancelled.txt")),
            "Esc cancels a real label edit without renaming the owned file");
    }

    private static async Task RenameLocationScenario(MainForm form, ExplorerPane pane, string fixture, string root)
    {
        form.SetPreviewVisible(false);
        string filtered = Path.Combine(fixture, "filtered-file.txt");
        File.WriteAllText(filtered, "Quick filter fixture.");
        var host = pane.ActiveView!;
        host.RefreshFolder();
        host.SetFilter("filtered");
        await RenameWait(() => ListedPaths(host).Contains(filtered), "filtered enumeration");
        await RenameOne(form, pane, filtered, "f2", clickCommit: false);
        host.SetFilter("");
        string recent = Directory.CreateDirectory(Path.Combine(root, "RenameRecent")).FullName;
        string previous = ShellNative.Recent;
        try
        {
            ShellNative.Recent = recent;
            foreach (bool folder in new[] { false, true })
            {
                string path = Path.Combine(fixture, folder ? "recent-folder" : "recent-file.txt");
                if (folder) Directory.CreateDirectory(path); else File.WriteAllText(path, "Recent target fixture.");
                Shortcut(recent, Path.GetFileName(path) + ".lnk", path, DateTime.Now);
                await Navigate(host, () => pane.Navigate(recent));
                await RecentSettled(host, 1);
                await RenameOne(form, pane, path, "f2", clickCommit: false);
                await Navigate(host, () => pane.Navigate(fixture));
            }
        }
        finally { ShellNative.Recent = previous; }
    }

    private static async Task RenameGroupedWindowScenario(MainForm form, string fixture)
    {
        var peer = form.ApplicationContext.CreateWindowFromActive(form);
        try
        {
            var pane = peer.GetPane(0);
            await OitReady(pane.ActiveView!);
            pane.CreateGroup(pane.ActiveTab!);
            string path = Path.Combine(fixture, "second-window-grouped.txt");
            File.WriteAllText(path, "Grouped peer rename.");
            pane.ActiveView!.RefreshFolder();
            await RenameWait(() => ListedPaths(pane.ActiveView!).Contains(path), "grouped peer enumeration");
            await RenameOne(peer, pane, path, "button", clickCommit: false);
            await RenameKeyboardScenario(peer, pane, fixture);
            await RenameGroupKeys(peer, pane, fixture);
            await RenameDeferredAppearance(peer, pane, fixture);
        }
        finally { peer.Close(); }
    }

    private static async Task RenameGroupKeys(MainForm form, ExplorerPane pane, string fixture)
    {
        string path = Directory.CreateDirectory(Path.Combine(fixture, "group-shortcut-folder")).FullName;
        var host = pane.ActiveView!;
        host.RefreshFolder();
        await RenameWait(() => ListedPaths(host).Contains(path), "group shortcut enumeration");
        host.SelectPaths([path]);
        form.Activate();
        host.FocusView();
        nint list = NativeAppearanceList(host);
        ShellNative.SetFocus(list);
        await RenameKey(list, Keys.F2);
        nint edit = SendMessage(list, 0x1018, 0, 0);
        Check(edit != 0, "group shortcut check starts the real folder label editor");
        var tabs = pane.Tabs.ToArray();
        var group = pane.ActiveTab!.Group!;
        foreach (Keys key in new[] { Keys.Alt | Keys.Shift | Keys.C, Keys.Alt | Keys.Shift | Keys.W,
            Keys.Control | Keys.Shift | Keys.PageUp, Keys.Control | Keys.Shift | Keys.PageDown })
        {
            await RenameKey(edit, key);
            Check(SendMessage(list, 0x1018, 0, 0) == edit && pane.Tabs.SequenceEqual(tabs) && !group.Collapsed,
                $"{key} does not close, collapse or reorder grouped tabs during rename in a second window");
        }
        RenameSetText(edit, "group-shortcut-renamed");
        await RenameKey(edit, Keys.Control | Keys.Enter);
        Check(pane.Tabs.SequenceEqual(tabs), "Ctrl+Enter in the folder label editor does not open a new tab");
        if (SendMessage(list, 0x1018, 0, 0) == edit) await RenameKey(edit, Keys.Enter);
        await RenameWait(() => Directory.Exists(Path.Combine(fixture, "group-shortcut-renamed")), "group shortcut commit");
    }

    private static async Task RenameDeferredAppearance(MainForm form, ExplorerPane pane, string fixture)
    {
        string path = Path.Combine(fixture, "deferred-appearance.txt");
        File.WriteAllText(path, "Deferred appearance fixture.");
        var host = pane.ActiveView!;
        host.RefreshFolder();
        await RenameWait(() => ListedPaths(host).Contains(path), "deferred appearance enumeration");
        nint list = NativeAppearanceList(host);
        pane.SetDetailsAppearance(false, false);
        await Task.Delay(150);
        int normalHeight = NativeAppearanceRowHeight(list);
        foreach (bool cancel in new[] { true, false })
        {
            host.SelectPaths([path]);
            form.Activate();
            host.FocusView();
            ShellNative.SetFocus(list);
            await RenameKey(list, Keys.F2);
            nint edit = SendMessage(list, 0x1018, 0, 0);
            Check(edit != 0, "deferred appearance starts a native rename");
            int before = NativeAppearanceRowHeight(list);
            pane.SetDetailsAppearance(false, cancel);
            await Task.Delay(150);
            Check(SendMessage(list, 0x1018, 0, 0) == edit && NativeAppearanceRowHeight(list) == before,
                "appearance changes preserve the active label editor and row height");
            RenameSetText(edit, cancel ? "discarded.txt" : "deferred-restored.txt");
            await RenameKey(edit, cancel ? Keys.Escape : Keys.Enter);
            await Task.Delay(250);
            int expected = cancel ? (int)Math.Round(28d * form.DeviceDpi / 96) : normalHeight;
            Check(SendMessage(list, 0x1018, 0, 0) == 0 && NativeAppearanceRowHeight(list) == expected
                && SendMessage(list, 0x0031, 0, 0) != 0,
                $"deferred row height and font are restored after {(cancel ? "Esc" : "commit")}");
            Check(cancel ? File.Exists(path) : File.Exists(Path.Combine(fixture, "deferred-restored.txt")),
                "deferred appearance does not change rename commit/cancel semantics");
        }
    }

    private static string RenameText(nint edit)
    {
        var text = new StringBuilder(1024);
        GetCloseWarningText(edit, text, text.Capacity);
        return text.ToString();
    }

    private static async Task RenameKey(nint window, Keys key)
    {
        byte[] saved = OitSetModifiers(key);
        try
        {
            bool alt = (key & Keys.Alt) != 0;
            if (alt)
            {
                byte[] state = new byte[256];
                PnGetKeyboardState(state);
                state[0x12] = state[0xA4] = 0x80;
                PnSetKeyboardState(state);
            }
            PostMessage(window, alt ? 0x104u : 0x100u, (nint)(key & Keys.KeyCode), 1);
            PostMessage(window, alt ? 0x105u : 0x101u, (nint)(key & Keys.KeyCode), unchecked((nint)0xC0000001));
            await Task.Delay(120);
        }
        finally { PnSetKeyboardState(saved); }
    }

    private static async Task RenameWait(Func<bool> ready, string description)
    {
        for (int i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException(description);
    }

    private static async Task RenameMenu(ShellViewHost host)
    {
        nint list = NativeAppearanceList(host), popup = 0;
        int position = -1;
        Exception? error = null;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ShortcutWinEvent callback = (_, eventId, window, _, _, _, _) =>
        {
            if (eventId == 7 && window == popup) { closed.TrySetResult(); return; }
            if (eventId != 6 || popup != 0) return;
            popup = window;
            nint menu = SendMessage(window, 0x01E1, 0, 0);
            for (int i = 0; i < NftGetMenuItemCount(menu); i++)
            {
                var caption = new StringBuilder(512);
                NftGetMenuString(menu, (uint)i, caption, caption.Capacity, 0x0400);
                string label = caption.ToString().Replace("&", "");
                if (label.Contains("이름 바꾸기") || label.StartsWith("Rename", StringComparison.OrdinalIgnoreCase))
                    position = i;
            }
            if (position < 0) { error = new InvalidOperationException("Native menu has no Rename command."); NftPostMenuKey(window, 0x1B); }
            else
            {
                PostMessage(popup, 0x01E5, position, 0);
                PostMessage(popup, 0x01F1, position, 0);
            }
        };
        nint hook = ShortcutSetWinEventHook(6, 7, 0, callback, (uint)Environment.ProcessId, 0, 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        using var dismissal = new System.Windows.Forms.Timer { Interval = 3000 };
        dismissal.Tick += (_, _) => { dismissal.Stop(); if (popup != 0) NftPostMenuKey(popup, 0x1B); };
        dismissal.Start();
        try
        {
            PostMessage(list, 0x007B, list, -1);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(100);
            if (error is not null) throw error;
        }
        finally { ShortcutUnhookWinEvent(hook); GC.KeepAlive(callback); }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern bool RenameSetText(nint window, string text);
}
