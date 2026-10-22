using System.Runtime.InteropServices;

namespace QuadExplorer;

// Images are borrowed by callers and remain owned here until Dispose. Classifying
// paths, rather than caching each favorite, bounds this cache to eight Shell icons.
internal sealed class ShellIconCache : IDisposable
{
    private enum Kind { Folder, Home, Desktop, Downloads, Documents, Recent, ThisPc, RecycleBin }
    private readonly Dictionary<Kind, Image> images = [];
    private readonly string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);
    private readonly string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory,
        Environment.SpecialFolderOption.DoNotVerify);
    private readonly string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolderOption.DoNotVerify);
    private bool disposed;

    public Image Get(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(path);
        Kind kind = Classify(path);
        if (images.TryGetValue(kind, out Image? image)) return image;
        image = Load(kind);
        images.Add(kind, image);
        return image;
    }

    private Kind Classify(string path)
    {
        string value = path.TrimEnd('\\', '/');
        if (EqualsPath(value, home) || value.Equals("Home", StringComparison.OrdinalIgnoreCase))
            return Kind.Home;
        if (EqualsPath(value, desktop) || value.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
            return Kind.Desktop;
        if (EqualsPath(value, Path.Combine(home, "Downloads"))
            || value.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
            return Kind.Downloads;
        if (EqualsPath(value, documents) || value.Equals("Documents", StringComparison.OrdinalIgnoreCase))
            return Kind.Documents;
        if (ShellViewHost.IsRecent(value)) return Kind.Recent;
        if (value.Equals("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ThisPc", StringComparison.OrdinalIgnoreCase))
            return Kind.ThisPc;
        if (value.Equals("::{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase)
            || value.Equals("RecycleBin", StringComparison.OrdinalIgnoreCase))
            return Kind.RecycleBin;
        return Kind.Folder;
    }

    private static bool EqualsPath(string value, string folder) =>
        folder.Length != 0 && value.Equals(folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static Image Load(Kind kind)
    {
        // Windows' known-folder default icons are resources, not zero-based icon
        // indices. Extracting from the local system module never asks a network or
        // cloud folder for its customized icon, thumbnail, overlay or existence.
        int resource = kind switch
        {
            Kind.Desktop => 183,
            Kind.Downloads => 184,
            Kind.Documents => 112,
            Kind.Recent => 117,
            _ => 0
        };
        nint handle = 0;
        try
        {
            if (resource != 0)
                Native.ExtractIconEx(Path.Combine(Environment.SystemDirectory, "imageres.dll"),
                    -resource, out handle, 0, 1);
            if (handle == 0)
            {
                int stock = kind switch
                {
                    Kind.Home => 96, // SIID_USERS
                    Kind.ThisPc or Kind.Desktop => 94, // SIID_DESKTOPPC
                    Kind.RecycleBin => 31, // SIID_RECYCLER (no enumeration to determine fullness)
                    _ => 3 // SIID_FOLDER
                };
                var info = new StockIconInfo { Size = (uint)Marshal.SizeOf<StockIconInfo>(), Path = "" };
                int result = Native.SHGetStockIconInfo(stock, 0x100, ref info); // SHGSI_ICON | LARGEICON
                handle = info.Icon;
                Marshal.ThrowExceptionForHR(result);
            }
            if (handle == 0) throw new InvalidOperationException("The Shell returned no stock icon.");
            using var icon = Icon.FromHandle(handle);
            return icon.ToBitmap();
        }
        finally
        {
            if (handle != 0) Native.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (Image image in images.Values) image.Dispose();
        images.Clear();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIconInfo
    {
        public uint Size;
        public nint Icon;
        public int SystemImageIndex;
        public int IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Path;
    }

    private static class Native
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconEx(string file, int index, out nint large, nint small, uint count);
        [DllImport("shell32.dll")]
        public static extern int SHGetStockIconInfo(int stock, uint flags, ref StockIconInfo info);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(nint icon);
    }
}
