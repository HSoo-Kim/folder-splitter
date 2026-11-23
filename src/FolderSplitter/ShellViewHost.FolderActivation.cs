using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    // Double-click or Enter on a folder runs the folder's default verb. When that verb starts a program, as
    // "Open folders in Folder Splitter by default" does, the list would start a second Folder Splitter instead
    // of opening the folder, so the view browses there itself, as it does with Windows' own default.
    internal static Func<string, bool> FolderOpensElsewhere { get; set; } = folder => FolderVerbOpensElsewhere(Registry.ClassesRoot, folder);
    internal int InPlaceFolderActivations { get; private set; }

    private bool TryActivateFolderInPlace(nint notification)
    {
        if (notification == 0 || Marshal.ReadInt32(notification, 2 * IntPtr.Size) != -114) return false; // LVN_ITEMACTIVATE
        // Alt, Ctrl and Shift keep their own commands; several items open the way Windows opens them.
        if (Marshal.PtrToStructure<ItemActivation>(notification).KeyFlags != 0 || SelectionCount != 1) return false;
        var files = new List<nint>();
        IReadOnlyList<string> folders;
        try { folders = SelectedTabFolders(files); }
        finally { foreach (nint file in files) Marshal.FreeCoTaskMem(file); }
        if (folders.Count != 1 || files.Count != 0 || !FolderOpensElsewhere(folders[0])) return false;
        InPlaceFolderActivations++;
        string folder = folders[0];
        BeginInvoke(() => { if (!IsDisposed) Navigate(folder); }); // after the list has finished the click or key
        return true;
    }

    internal static bool FolderVerbOpensElsewhere(RegistryKey classes, string folder)
    {
        string full = Path.GetFullPath(folder);
        bool drive = string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);
        using var shell = classes.OpenSubKey((drive ? "Drive" : "Directory") + @"\shell");
        return shell?.GetValue("") is string verb && verb.Length > 0
            && !verb.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !verb.Equals("open", StringComparison.OrdinalIgnoreCase)
            && !verb.Equals("explore", StringComparison.OrdinalIgnoreCase);
    }

    // NMITEMACTIVATE: NMHDR is a nested struct, so the item fields start after its padded size.
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyHeader
    {
        public nint From;
        public nuint Id;
        public int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ItemActivation
    {
        public NotifyHeader Header;
        public int Item, SubItem;
        public uint NewState, OldState, Changed;
        public Point Action;
        public nint Param;
        public uint KeyFlags;
    }
}
