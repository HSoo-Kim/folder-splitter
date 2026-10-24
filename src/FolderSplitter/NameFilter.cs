using System.Runtime.InteropServices;

namespace QuadExplorer;

// Shows an item only when its displayed name contains every fragment, ignoring case.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class NameFilter(string[] fragments) : IFolderFilter
{
    public int ShouldShow(object folder, nint folderIdList, nint itemIdList)
    {
        nint full = ShellNative.ILCombine(folderIdList, itemIdList);
        try { return Matches(ShellNative.Name(full, 0), fragments) ? 0 : 1; } // S_OK shows, S_FALSE hides
        finally { ShellNative.ILFree(full); }
    }

    public int GetEnumFlags(object folder, nint folderIdList, ref nint window, ref uint flags) => 0;

    internal static bool Matches(string? name, IEnumerable<string> fragments) =>
        name is not null && fragments.All(fragment => name.Contains(fragment, StringComparison.CurrentCultureIgnoreCase));
}
