using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunNativeShortcutMenuScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "ShortcutMenus")).FullName;
        string shortcut = Path.Combine(folder, "sample.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object link = ((dynamic)shell).CreateShortcut(shortcut);
        try
        {
            ((dynamic)link).TargetPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            ((dynamic)link).Save();
        }
        finally { Marshal.ReleaseComObject(link); Marshal.ReleaseComObject(shell); }
        var shortcuts = new List<string> { shortcut };
        var originals = new List<string>();
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_SHORTCUTS") is { Length: > 0 } sources)
        {
            foreach (string source in sources.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Path.IsPathFullyQualified(source) || !File.Exists(source) ||
                    !Path.GetExtension(source).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Shortcut fixture must be an existing absolute .lnk path: " + source);
                string copy = Path.Combine(folder, $"installed-{shortcuts.Count}.lnk");
                File.Copy(source, copy);
                shortcuts.Add(copy);
                originals.Add(source);
                Console.WriteLine($"SHORTCUT_FIXTURE source={source} copy={Path.GetFileName(copy)}");
            }
        }
        Theme.Set("dark");
        using var form = new Form { Text = "Native shortcut menu", Size = new Size(800, 600), ShowInTaskbar = false };
        using var host = new ShellViewHost(folder) { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                await ItemCountKnown(host);
                host.SetViewModeAndSize(4, 16);
                host.SetDetailsAppearance(false, true, true);
                Check(host.SelectPaths([shortcut]) == 1, "shortcut context-menu case selects the actual Shell item");
                nint list = NativeAppearanceList(host);
                int index = (int)SendMessage(list, 0x100C, -1, 2); // LVM_GETNEXTITEM, LVNI_SELECTED
                var row = new NativeRect();
                Check(NativeAppearanceRectMessage(list, 0x100E, index, ref row) != 0,
                    "shortcut context-menu case has actual item bounds");
                var point = new ShortcutPoint { X = row.Left + 60, Y = (row.Top + row.Bottom) / 2 };
                ShortcutToScreen(list, ref point);
                var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ShortcutWinEvent callback = (_, eventId, window, _, _, _, _) =>
                {
                    if (eventId == 6) // EVENT_SYSTEM_MENUPOPUPSTART, on the private desktop only
                    {
                        opened.TrySetResult();
                        PostMessage(window, 0x100, 0x1B, 0); // Escape dismisses, never invokes a command.
                    }
                    else if (eventId == 7) closed.TrySetResult(); // EVENT_SYSTEM_MENUPOPUPEND
                };
                nint hook = ShortcutSetWinEventHook(6, 7, 0, callback, (uint)Environment.ProcessId, 0, 0);
                if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    Check(PostMessage(list, 0x007B, list, (nint)((point.Y << 16) | (point.X & 0xFFFF))),
                        "shortcut context-menu request is queued to the private Shell view");
                    await Task.WhenAll(opened.Task, closed.Task).WaitAsync(TimeSpan.FromSeconds(15));
                    await NativeAppearanceDispatch(host);
                    Check(SelectedPaths(host).SequenceEqual([shortcut]),
                        "real shortcut context menu opens and closes without losing the native selection");
                }
                finally { ShortcutUnhookWinEvent(hook); GC.KeepAlive(callback); }
                foreach (string item in shortcuts)
                    await ShortcutMouseMenu(host, item, "single-dark");
                host.SetDetailsAppearance(true, true, true);
                foreach (var group in originals.GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase))
                {
                    await Navigate(host, () => host.Navigate(group.Key));
                    await ItemCountKnown(host);
                    host.SetViewModeAndSize(4, 16);
                    await NativeAppearanceDispatch(host);
                    Console.WriteLine($"SHORTCUT_ORIGINAL_FOLDER path={group.Key} separators=true");
                    foreach (string item in group)
                        await ShortcutMouseMenu(host, item, "single-dark-original-separators");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Native shortcut-menu scenario failed.", failure);
        foreach (bool separators in new[] { false, true })
        {
            RunThreePaneShortcutMenus(root, folder, shortcuts, separators);
            foreach (var group in originals.GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase))
                RunThreePaneShortcutMenus(root, group.Key, group.ToList(), separators);
        }
    }

    private static void RunThreePaneShortcutMenus(string root, string folder, List<string> shortcuts, bool separators)
    {
        string session = Path.Combine(root, "shortcut-three-pane.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ColorTheme = "dim", Layout = 3,
            ShowFolderTree = false, GlobalHotkey = false, KeepInTray = false,
            CheckUpdates = false, AutoRestartMemory = 0, ExplorerFavoritesImported = true,
            ShowVerticalSeparators = separators, ComfortableRows = true, HighlightSortedColumn = true,
            Panes = [new PaneState { Tabs = [folder] }, new PaneState { Tabs = [folder] },
                new PaneState { Tabs = [folder] }, new PaneState()]
        });
        using var form = new MainForm(session, []);
        var panes = Enumerable.Range(0, 3).Select(form.GetPane).ToArray();
        // Subscribe before Show; only visible panes can complete native navigation.
        var navigation = panes.Select(p => p.ActiveView!.HasNavigated
            ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                Console.WriteLine($"SHORTCUT_THREE_PANE waiting-navigation folder={folder} separators={separators}");
                await Task.WhenAll(navigation).WaitAsync(TimeSpan.FromSeconds(20));
                Check(form.CurrentLayout.GetPanes().Length == 3 && panes.All(p => p.Visible),
                    "shortcut reproduction uses three visible MainForm panes");
                foreach (var pane in panes)
                {
                    var host = pane.ActiveView!;
                    await ItemCountKnown(host);
                    host.SetViewModeAndSize(4, 16);
                    await NativeAppearanceDispatch(host);
                    Console.WriteLine($"SHORTCUT_PANE pane={pane.PaneNumber} bounds={pane.Bounds} items={host.ItemCount}");
                    foreach (string item in shortcuts)
                        await ShortcutMouseMenu(host, item, $"three-dim-pane-{pane.PaneNumber}");
                }
                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                {
                    form.Refresh();
                    using var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(output, $"shortcut-three-pane-{Path.GetFileName(folder)}-{separators}.png"));
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Three-pane shortcut-menu scenario failed.", failure);
    }

    private static async Task ShortcutMouseMenu(ShellViewHost host, string shortcut, string scenario)
    {
        nint list = NativeAppearanceList(host);
        // Find the displayed item without selecting it, with or without hidden .lnk extension.
        var find = new ShortcutFindInfo { Flags = 2, Text = Path.GetFileName(shortcut) }; // LVFI_STRING
        int index = (int)ShortcutFindItem(list, 0x1053, -1, ref find); // LVM_FINDITEMW
        if (index < 0)
        {
            find.Text = Path.GetFileNameWithoutExtension(shortcut);
            index = (int)ShortcutFindItem(list, 0x1053, -1, ref find);
        }
        Check(index >= 0, $"{scenario}: shortcut has an unselected native row");
        host.SelectPaths([]);
        await NativeAppearanceDispatch(host);
        Check(host.SelectionCount == 0, $"{scenario}: no item is selected before right-click");
        SendMessage(list, 0x1013, index, 0); // LVM_ENSUREVISIBLE
        var label = new NativeRect { Left = 2 }; // LVIR_LABEL
        Check(NativeAppearanceRectMessage(list, 0x100E, index, ref label) != 0 && label.Right > label.Left,
            $"{scenario}: shortcut label has actual bounds");
        int x = label.Left + Math.Min(30, (label.Right - label.Left) / 2);
        int y = (label.Top + label.Bottom) / 2;
        nint position = (nint)((y << 16) | (x & 0xFFFF));
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ShortcutWinEvent callback = (_, eventId, window, _, _, _, _) =>
        {
            if (eventId == 6)
            {
                opened.TrySetResult();
                PostMessage(window, 0x100, 0x1B, 0); // Escape, never invoke a command.
            }
            else if (eventId == 7) closed.TrySetResult();
        };
        nint hook = ShortcutSetWinEventHook(6, 7, 0, callback, (uint)Environment.ProcessId, 0, 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Console.WriteLine($"SHORTCUT_MOUSE scenario={scenario} file={Path.GetFileName(shortcut)} index={index} point={x},{y}");
            // Address this private native list only. No SendInput, cursor movement, or input-desktop switch.
            Check(PostMessage(list, 0x0200, 0, position) &&
                PostMessage(list, 0x0204, 2, position) && PostMessage(list, 0x0205, 0, position),
                $"{scenario}: native right-button down/up transaction queued");
            await Task.WhenAll(opened.Task, closed.Task).WaitAsync(TimeSpan.FromSeconds(15));
            await NativeAppearanceDispatch(host);
            Check(SelectedPaths(host).SequenceEqual([shortcut], StringComparer.OrdinalIgnoreCase),
                $"{scenario}: right-click selects shortcut and native popup opens/closes");
        }
        finally { ShortcutUnhookWinEvent(hook); GC.KeepAlive(callback); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShortcutPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShortcutFindInfo
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string Text;
        public nint Parameter;
        public ShortcutPoint Point;
        public uint Direction;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ShortcutWinEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    private static extern bool ShortcutToScreen(nint window, ref ShortcutPoint point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern nint ShortcutFindItem(nint window, uint message, nint start, ref ShortcutFindInfo find);
    [DllImport("user32.dll", EntryPoint = "SetWinEventHook", SetLastError = true)]
    private static extern nint ShortcutSetWinEventHook(uint first, uint last, nint module, ShortcutWinEvent callback,
        uint process, uint thread, uint flags);
    [DllImport("user32.dll", EntryPoint = "UnhookWinEvent")]
    private static extern bool ShortcutUnhookWinEvent(nint hook);
}
