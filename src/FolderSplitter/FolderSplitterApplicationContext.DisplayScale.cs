using System.Runtime.InteropServices;

namespace QuadExplorer;

// Windows fixes a process's system DPI when the process starts. If the app restarts during a Remote Desktop session
// from a display with other scaling (a Mac at 200%), it keeps that DPI after the session returns to the local monitors:
// the hosted file lists' status icons, system metrics and the text sized from them stay at the remote size. Only a new
// process gets the current scaling, so when a display or session change leaves the two apart, the app restarts in
// place (windows, tabs and hidden or minimized state come back) as soon as nothing is in progress.
public sealed partial class FolderSplitterApplicationContext
{
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 3000 };
    private int unreachableDisplayDpi;

    internal int SystemDpi { get; } = (int)GetDpiForSystem();
    internal Func<int> DisplayDpi { get; set; } = PrimaryDisplayDpi;

    private void InitializeDisplayScale()
    {
        int display = PrimaryDisplayDpi();
        // Started with the two already apart: Windows gives new processes another DPI, so a restart cannot help.
        unreachableDisplayDpi = display != SystemDpi ? display : 0;
        displayTimer.Tick += (_, _) => CheckDisplayScale();
    }

    // Several display changes arrive while a Remote Desktop session connects; act once they settle.
    internal void DisplayChanged()
    {
        if (disposed || shuttingDown) return;
        displayTimer.Stop();
        displayTimer.Start();
    }

    internal static bool ShouldRestartForDisplayScale(int systemDpi, int displayDpi, int unreachableDpi) =>
        displayDpi > 0 && displayDpi != systemDpi && displayDpi != unreachableDpi;

    private void CheckDisplayScale()
    {
        if (disposed || restarting || !ShouldRestartForDisplayScale(SystemDpi, DisplayDpi(), unreachableDisplayDpi))
        {
            displayTimer.Stop();
            return;
        }
        // The timer keeps running until nothing would be cut short.
        if (Updating || FileOperations.InProgress || LastActiveWindow is not { } owner || InputInProgress()) return;
        displayTimer.Stop();
        RestartApplication(owner);
    }

    private bool InputInProgress()
    {
        if (windows.Any(window => window.IsInteracting)) return true;
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        // GUI_INMOVESIZE | GUI_INMENUMODE | GUI_SYSTEMMENUMODE | GUI_POPUPMENUMODE; a mouse capture is a drag in progress.
        return GetGUIThreadInfo(GetCurrentThreadId(), ref info) && ((info.Flags & 0x1E) != 0 || info.Capture != 0);
    }

    // The primary monitor holds the origin; MONITOR_DEFAULTTOPRIMARY, MDT_EFFECTIVE_DPI.
    private static int PrimaryDisplayDpi() =>
        GetDpiForMonitor(MonitorFromPoint(Point.Empty, 1), 0, out uint dpi, out _) == 0 ? (int)dpi : 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int CaretLeft, CaretTop, CaretRight, CaretBottom;
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
