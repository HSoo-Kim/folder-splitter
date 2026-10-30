using System.Runtime.InteropServices;

namespace QuadExplorer;

// Windows records every opened file and folder as a shortcut in the Recent folder. The pane lists the
// shortcut targets instead, so dragging, copying and "Copy path" hand over the item itself, not a .lnk file.
internal static class RecentItems
{
    // Absolute ID lists of the files and folders whose names contain every filter fragment, most recently
    // opened first. The shortcut files are read as data (MS-SHLLINK) without resolving them, which takes a
    // fraction of the time IShellLink does. Entries Windows records for app activations such as ms-photos:
    // have no file path and are left out. Only targets on local fixed drives are checked for deletion, so an
    // offline share or a removed USB drive cannot stall the list. The caller frees each ID list with ILFree.
    public static List<nint> Load(string folder, IReadOnlyCollection<string> filter)
    {
        var items = new List<nint>();
        FileInfo[] links;
        try
        {
            links = new DirectoryInfo(folder).GetFiles("*.lnk", new EnumerationOptions { IgnoreInaccessible = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException) { return items; }
        // A shortcut's modified time is when Windows last recorded opening its target.
        Array.Sort(links, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fixedRoots = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var path = new char[32768];
        foreach (var link in links)
        {
            byte[] data;
            try { data = File.ReadAllBytes(link.FullName); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            nint pidl;
            string target;
            // A damaged shortcut is skipped; the rest of the list still loads.
            try { if (!Read(data, path, out pidl, out target)) continue; }
            catch (ArgumentException) { continue; }
            string name = Path.GetFileName(target.TrimEnd('\\'));
            if (target.Length == 0 || !seen.Add(target) || Deleted(target, fixedRoots)
                || filter.Count > 0 && !NameFilter.Matches(name.Length > 0 ? name : target, filter))
            {
                ShellNative.ILFree(pidl);
                continue;
            }
            items.Add(pidl);
        }
        return items;
    }

    private static bool Deleted(string path, Dictionary<string, bool> fixedRoots)
    {
        // Only drive-letter paths: a share or a rooted path without a drive is never checked.
        if (Path.GetPathRoot(path) is not { Length: 3 } root || root[1] != ':') return false;
        if (!fixedRoots.TryGetValue(root, out bool isFixed))
            fixedRoots[root] = isFixed = new DriveInfo(root).DriveType == DriveType.Fixed;
        return isFixed && !File.Exists(path) && !Directory.Exists(path);
    }

    // [MS-SHLLINK] https://learn.microsoft.com/openspecs/windows_protocols/ms-shllink/16cb4ca1-9339-4d0c-a68d-bf1d6cc0f943
    // The header, the target's ID list (what IShellLink.GetIDList returns) and LinkInfo, which holds its path.
    private static bool Read(byte[] data, char[] path, out nint pidl, out string target)
    {
        pidl = 0;
        target = "";
        if (data.Length < 0x4E || BitConverter.ToUInt32(data, 0) != 0x4C) return false; // HeaderSize
        uint flags = BitConverter.ToUInt32(data, 0x14);
        if ((flags & 0x1) == 0) return false; // HasLinkTargetIDList
        int size = BitConverter.ToUInt16(data, 0x4C);
        const int IdList = 0x4E;
        if (size < 2 || IdList + size > data.Length || !WellFormed(data, IdList, size)) return false;
        int linkInfo = IdList + size;
        bool hasLinkInfo = (flags & 0x2) != 0 && linkInfo + 0x1C <= data.Length; // HasLinkInfo
        if (hasLinkInfo) target = LinkInfoPath(data, linkInfo);
        pidl = Marshal.AllocCoTaskMem(size);
        Marshal.Copy(data, IdList, pidl, size);
        if (!hasLinkInfo) target = FileSystemPath(pidl, path);
        return true;
    }

    // Each item starts with its 16-bit size; a zero size ends the list exactly at its end.
    private static bool WellFormed(byte[] data, int start, int size)
    {
        for (int offset = 0; offset + 2 <= size;)
        {
            int length = BitConverter.ToUInt16(data, start + offset);
            if (length == 0) return offset + 2 == size;
            if (length < 2 || offset + length > size) return false;
            offset += length;
        }
        return false;
    }

    private static string LinkInfoPath(byte[] data, int start)
    {
        int length = (int)BitConverter.ToUInt32(data, start);
        int header = (int)BitConverter.ToUInt32(data, start + 4);
        uint flags = BitConverter.ToUInt32(data, start + 8);
        if (header < 0x1C || length < header || length > data.Length - start) return "";
        // Headers of 0x24 bytes or more also carry Unicode copies of the ANSI strings.
        bool unicode = header >= 0x24;
        string suffix = unicode ? Unicode(data, start, length, BitConverter.ToUInt32(data, start + 0x20))
            : Ansi(data, start, length, BitConverter.ToUInt32(data, start + 0x18));
        if ((flags & 0x1) != 0) // VolumeIDAndLocalBasePath
        {
            string basePath = unicode ? Unicode(data, start, length, BitConverter.ToUInt32(data, start + 0x1C))
                : Ansi(data, start, length, BitConverter.ToUInt32(data, start + 0x10));
            return basePath.Length == 0 ? "" : basePath + suffix;
        }
        if ((flags & 0x2) == 0) return ""; // CommonNetworkRelativeLinkAndPathSuffix
        int network = start + (int)BitConverter.ToUInt32(data, start + 0x14);
        int end = start + length;
        if (network < start || network + 0x14 > end) return "";
        uint netName = BitConverter.ToUInt32(data, network + 8);
        int networkLength = Math.Min((int)BitConverter.ToUInt32(data, network), end - network);
        string share = netName > 0x14 && network + 0x1C <= end
            ? Unicode(data, network, networkLength, BitConverter.ToUInt32(data, network + 0x14))
            : Ansi(data, network, networkLength, netName);
        return share.Length == 0 ? "" : suffix.Length == 0 ? share : share.TrimEnd('\\') + "\\" + suffix;
    }

    // ANSI strings use the system code page, as Windows wrote them.
    private static string Ansi(byte[] data, int start, int length, uint offset)
    {
        if (offset == 0 || offset >= length) return "";
        int from = start + (int)offset, end = from;
        while (end < start + length && data[end] != 0) end++;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { return Marshal.PtrToStringAnsi(handle.AddrOfPinnedObject() + from, end - from) ?? ""; }
        finally { handle.Free(); }
    }

    private static string Unicode(byte[] data, int start, int length, uint offset)
    {
        if (offset == 0 || offset >= length) return "";
        int from = start + (int)offset, end = from;
        while (end + 1 < start + length && (data[end] != 0 || data[end + 1] != 0)) end += 2;
        return System.Text.Encoding.Unicode.GetString(data, from, end - from);
    }

    // Shortcuts saved without LinkInfo: the path comes from the ID list, as IShellLink.GetPath does.
    private static string FileSystemPath(nint pidl, char[] path) =>
        SHGetPathFromIDListEx(pidl, path, path.Length, 0) ? new string(path, 0, Array.IndexOf(path, '\0')) : "";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListEx(nint pidl, [Out] char[] path, int length, int options);
}
