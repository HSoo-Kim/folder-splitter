using System.Runtime.InteropServices;
using QuadExplorer.Search;

namespace QuadExplorer;

// Runs only on the icon worker. The real item is queried on fixed local drives;
// remote/removable or missing items use the Shell's file-type association instead.
internal static class AddressShellIcons
{
    // SHGetFileInfo requires COM on the calling thread. Balance successful
    // initialization even when that worker processes several query generations.
    internal static int InitializeWorker() => Native.CoInitializeEx(0, 0);
    internal static void UninitializeWorker() => Native.CoUninitialize();

    internal static Bitmap? Load(SearchHit hit)
    {
        uint attributes = hit.IsDirectory ? 0x10u : 0x80u;
        const uint icon = 0x100, smallIcon = 0x1, useAttributes = 0x10;
        string path = hit.FullPath;
        bool local = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/') && Native.GetDriveType(path[..3]) == 3;
        Bitmap? result = local ? Get(path, attributes, icon | smallIcon) : null;
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
    }
}
