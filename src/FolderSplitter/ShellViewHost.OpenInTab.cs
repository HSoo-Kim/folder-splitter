using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    internal event Action<IReadOnlyList<string>, bool>? OpenFoldersInTabsRequested;

    private string? middleTabFolder;
    private nint middleTabList;
    private bool suppressTabLeftUp;
    private bool tabEnterHandled;

    private bool TryOpenInTabListMessage(ref Message message)
    {
        if (TryOpenInTabKey(ref message)) return true;
        switch (message.Msg)
        {
            case 0x0207: // WM_MBUTTONDOWN
            case 0x0209: // WM_MBUTTONDBLCLK is the second down of a rapid pair
            {
                ReleaseOpenInTabMouseGesture();
                if (OpenFoldersInTabsRequested is null || FolderAtListPoint(message.HWnd, message.LParam) is not { } folder)
                    return false;
                middleTabFolder = folder;
                middleTabList = message.HWnd;
                OpenInTabNative.SetCapture(message.HWnd);
                message.Result = 0;
                return true;
            }
            case 0x0208: // WM_MBUTTONUP
            {
                if (middleTabFolder is not { } pressedFolder || middleTabList != message.HWnd) return false;
                string? releasedFolder = FolderAtListPoint(message.HWnd, message.LParam);
                ReleaseOpenInTabMouseGesture();
                if (string.Equals(pressedFolder, releasedFolder, StringComparison.OrdinalIgnoreCase))
                    OpenFoldersInTabsRequested?.Invoke([pressedFolder], MouseTabForeground(message.WParam));
                message.Result = 0;
                return true;
            }
            case 0x0201: // WM_LBUTTONDOWN: leave Ctrl+single-click selection entirely native
                suppressTabLeftUp = false;
                break;
            case 0x0203: // WM_LBUTTONDBLCLK
                if (((long)message.WParam & 0x0008) == 0 || OpenFoldersInTabsRequested is null
                    || FolderAtListPoint(message.HWnd, message.LParam) is not { } doubleClickedFolder)
                    return false;
                suppressTabLeftUp = true;
                OpenFoldersInTabsRequested.Invoke([doubleClickedFolder], MouseTabForeground(message.WParam));
                message.Result = 0;
                return true;
            case 0x0202: // WM_LBUTTONUP paired with a handled double-click
                if (!suppressTabLeftUp) return false;
                suppressTabLeftUp = false;
                message.Result = 0;
                return true;
            case 0x001F: // WM_CANCELMODE
            case 0x0215: // WM_CAPTURECHANGED
                ReleaseOpenInTabMouseGesture();
                suppressTabLeftUp = false;
                break;
        }
        return false;
    }

    private static bool MouseTabForeground(nint modifiers) => ((long)modifiers & 0x000C) == 0x000C; // MK_CONTROL | MK_SHIFT

    private void ReleaseOpenInTabMouseGesture()
    {
        nint list = middleTabList;
        middleTabFolder = null;
        middleTabList = 0;
        if (list != 0 && OpenInTabNative.GetCapture() == list) OpenInTabNative.ReleaseCapture();
    }

    private bool TryOpenInTabKey(ref Message message)
    {
        // Do not turn Enter in the native rename edit control into a tab gesture.
        if (detailsList is not { Handle: var list } || list == 0 || message.HWnd != list) return false;
        if (message.Msg == 0x0101 && message.WParam == 0x0D) // WM_KEYUP / VK_RETURN
        {
            bool handled = tabEnterHandled;
            tabEnterHandled = false;
            if (handled) message.Result = 0;
            return handled;
        }
        if (message.Msg == 0x0102 && (message.WParam == 0x0D || message.WParam == 0x0A) && tabEnterHandled) // WM_CHAR
        {
            message.Result = 0;
            return true;
        }
        if (message.Msg != 0x0100 || message.WParam != 0x0D) return false; // WM_KEYDOWN / VK_RETURN
        if (((long)message.LParam & 0x40000000) != 0 && tabEnterHandled)
        {
            message.Result = 0;
            return true; // Holding Enter must not create a stream of duplicate tabs.
        }
        tabEnterHandled = false;
        if (OpenInTabNative.GetKeyState(0x11) >= 0 || OpenInTabNative.GetKeyState(0x12) < 0
            || OpenFoldersInTabsRequested is null) return false; // Ctrl, but not AltGr/Ctrl+Alt
        var files = new List<nint>();
        try
        {
            var folders = SelectedTabFolders(files);
            if (folders.Count == 0) return false; // Leave a file-only selection to the native accelerator.
            tabEnterHandled = true;
            bool foreground = OpenInTabNative.GetKeyState(0x10) < 0;
            // Enter is consumed for the folders, so preserve normal file activation in a mixed
            // selection using the actual shell identities (not display labels or reconstructed paths).
            foreach (nint file in files)
            {
                var execute = new OpenInTabNative.ShellExecuteInfo
                {
                    Size = Marshal.SizeOf<OpenInTabNative.ShellExecuteInfo>(),
                    Mask = 0x0000010C, Window = Handle, Show = 1, ItemIdList = file // INVOKEIDLIST | NOASYNC: keep the PIDL alive
                };
                OpenInTabNative.ShellExecuteExW(ref execute);
            }
            OpenFoldersInTabsRequested.Invoke(folders, foreground);
        }
        finally { foreach (nint file in files) Marshal.FreeCoTaskMem(file); }
        message.Result = 0;
        return true;
    }

    private string? FolderAtListPoint(nint list, nint coordinates)
    {
        var hit = new OpenInTabNative.ListHitTest
        {
            Point = new Point(unchecked((short)(long)coordinates), unchecked((short)((long)coordinates >> 16)))
        };
        // Native row indices belong to IFolderView, not to filesystem enumeration order or display labels.
        if (OpenInTabNative.HitTest(list, 0x1039, 0, ref hit) < 0 || hit.Item < 0 || (hit.Flags & 0x000E) == 0)
            return null; // LVM_SUBITEMHITTEST, LVHT_ONITEM; exclude empty space/group headers
        return TabFolderAtIndex(hit.Item);
    }

    private IReadOnlyList<string> SelectedTabFolders(List<nint>? files = null)
    {
        if (view is not IFolderView2 folderView) return [];
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int previous = -1;
            // The hosted Shell searches after the previous index; S_FALSE means exhausted.
            while (folderView.GetSelectedItem(previous, out int index) == 0 && index > previous)
            {
                IShellItem? item = null;
                try
                {
                    Guid itemId = typeof(IShellItem).GUID;
                    if (folderView.GetItem(index, ref itemId, out item) >= 0 && item is not null)
                    {
                        string? folder = FileSystemTabFolder(item, resolveLink: true, out bool file);
                        if (folder is not null && seen.Add(folder)) folders.Add(folder);
                        else if (file && files is not null && OpenInTabNative.SHGetIDListFromObject(item, out nint pidl) >= 0 && pidl != 0)
                            files.Add(pidl);
                    }
                }
                catch (COMException) { } // One unresolvable namespace item must not hide the remaining selection.
                finally { if (item is not null) Marshal.ReleaseComObject(item); }
                previous = index;
            }
        }
        catch (COMException) { } // A shell view can disappear while navigation replaces it.
        return folders;
    }

    private string? TabFolderAtIndex(int index)
    {
        if (view is not IFolderView2 folderView) return null;
        IShellItem? item = null;
        try
        {
            Guid itemId = typeof(IShellItem).GUID;
            if (folderView.GetItem(index, ref itemId, out item) < 0 || item is null) return null;
            return FileSystemTabFolder(item, resolveLink: true, out _);
        }
        catch (COMException) { return null; }
        finally { if (item is not null) Marshal.ReleaseComObject(item); }
    }

    private static string? FileSystemTabFolder(IShellItem item, bool resolveLink, out bool file)
    {
        file = false;
        const uint folderAttribute = 0x20000000, linkAttribute = 0x00010000;
        if (item.GetAttributes(folderAttribute | linkAttribute, out uint attributes) < 0) return null;
        if ((attributes & linkAttribute) != 0)
        {
            if (!resolveLink) return null;
            Guid linkTarget = new("3981E228-F559-11D3-8E3A-00C04F6837D5"); // BHID_LinkTargetItem
            Guid itemId = typeof(IShellItem).GUID;
            IShellItem? target = null;
            try
            {
                if (item.BindToHandler(0, ref linkTarget, ref itemId, out target) < 0 || target is null) return null;
                return FileSystemTabFolder(target, resolveLink: false, out file);
            }
            finally { if (target is not null) Marshal.ReleaseComObject(target); }
        }
        if (item.GetDisplayName(0x80058000, out nint name) < 0)
            return null; // SIGDN_FILESYSPATH also works for filesystem children of namespace folders.
        try
        {
            string? path = Marshal.PtrToStringUni(name);
            // Shell "folders" also include ZIP files and virtual namespace objects. Only a real,
            // navigable filesystem directory is a tab target; never fabricate a path from its label.
            if (path is null) return null;
            if ((attributes & folderAttribute) != 0 && Directory.Exists(path)) return path;
            file = File.Exists(path);
            return null;
        }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    private static class OpenInTabNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct ListHitTest
        {
            public Point Point;
            public uint Flags;
            public int Item, SubItem, Group;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ShellExecuteInfo
        {
            public int Size;
            public uint Mask;
            public nint Window, Verb, File, Parameters, Directory;
            public int Show;
            public nint Instance, ItemIdList, Class, ClassKey;
            public uint HotKey;
            public nint IconOrMonitor, Process;
        }

        [DllImport("shell32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
        [DllImport("shell32.dll")]
        internal static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, out nint pidl);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern nint HitTest(nint window, int message, nint wParam, ref ListHitTest hit);
        [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
        [DllImport("user32.dll")] internal static extern nint SetCapture(nint window);
        [DllImport("user32.dll")] internal static extern nint GetCapture();
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    }
}
