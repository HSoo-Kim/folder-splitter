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
            }
            catch (Exception ex) { failure = ex; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Native shortcut-menu scenario failed.", failure);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShortcutPoint { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ShortcutWinEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    private static extern bool ShortcutToScreen(nint window, ref ShortcutPoint point);
    [DllImport("user32.dll", EntryPoint = "SetWinEventHook", SetLastError = true)]
    private static extern nint ShortcutSetWinEventHook(uint first, uint last, nint module, ShortcutWinEvent callback,
        uint process, uint thread, uint flags);
    [DllImport("user32.dll", EntryPoint = "UnhookWinEvent")]
    private static extern bool ShortcutUnhookWinEvent(nint hook);
}
