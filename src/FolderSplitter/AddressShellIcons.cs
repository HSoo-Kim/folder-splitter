using System.Runtime.InteropServices;
using QuadExplorer.Search;

namespace QuadExplorer;

// Runs only on the icon thread. The real item is queried on fixed local drives;
// remote/removable or missing items use the Shell's file-type association instead.
internal static class AddressShellIcons
{
    // The Shell reads icons through handlers that other programs register. Many of them (Internet shortcuts,
    // document security products that mark every file) work only in a single-threaded apartment, which
    // SHGetFileInfo also requires. Called from any other thread they fail, and every such result shows the blank
    // "unknown type" icon. The file list already runs on a single-threaded apartment, so this gives the popup the
    // icons the file list shows.
    internal static void StartThread(ThreadStart work)
    {
        var thread = new Thread(() =>
        {
            // The runtime has usually initialized the apartment already; S_FALSE still needs the matching call.
            int com = Native.CoInitializeEx(0, 2); // COINIT_APARTMENTTHREADED
            try { work(); }
            finally { if (com >= 0) Native.CoUninitialize(); }
        }) { IsBackground = true, Name = "Address icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // A single-threaded apartment receives its calls as window messages, and Shell handlers may post their own.
    // Handling them between icons keeps a handler that waits for one from stalling the remaining icons.
    internal static void PumpMessages()
    {
        while (Native.PeekMessage(out var message, 0, 0, 0, 1)) // PM_REMOVE
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }
    }

    internal static Bitmap? Load(SearchHit hit)
    {
        uint attributes = hit.IsDirectory ? 0x10u : 0x80u;
        const uint icon = 0x100, smallIcon = 0x1, useAttributes = 0x10, overlays = 0x20;
        string path = hit.FullPath;
        bool local = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/') && Native.GetDriveType(path[..3]) == 3;
        // Overlays, such as a sync state or a document-security lock, appear as they do in the file list.
        Bitmap? result = local ? Get(path, attributes, icon | smallIcon | overlays) : null;
        // Supplying only the extension prevents a fallback from touching the original
        // path, including UNC shares, mapped drives, and no-longer-present items.
        string association = hit.IsDirectory ? "folder" : Path.GetExtension(path);
        if (association.Length == 0) association = "file";
        return result ?? Get(association, attributes, icon | smallIcon | useAttributes);
    }

    private static Bitmap? Get(string path, uint attributes, uint flags)
    {
        var info = new ShellFileInfo();
        nint result = Native.SHGetFileInfo(path, attributes, ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        try
        {
            if (result == 0 || info.Icon == 0) return null;
            using var icon = Icon.FromHandle(info.Icon);
            return icon.ToBitmap();
        }
        finally
        {
            if (info.Icon != 0) Native.DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    private static class Native
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
        [DllImport("ole32.dll")]
        internal static extern int CoInitializeEx(nint reserved, uint concurrencyModel);
        [DllImport("ole32.dll")]
        internal static extern void CoUninitialize();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint GetDriveType(string root);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(nint icon);
        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessage(out WindowMessage message, nint window, uint first, uint last, uint remove);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref WindowMessage message);
        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        internal static extern nint DispatchMessage(ref WindowMessage message);
    }
}
