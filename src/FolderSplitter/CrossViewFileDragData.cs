using System.Runtime.InteropServices;

namespace QuadExplorer;

internal static class CrossViewFileDragData
{
    private const uint SfgaoFileSystem = 0x40000000;
    private const uint DropEffectCopyMoveLink = 1 | 2 | 4;
    private static readonly Guid BhidDataObject = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
    private static readonly Guid IidDataObject = new("0000010E-0000-0000-C000-000000000046");

    internal static bool TryCreate(IReadOnlyList<string> paths, out nint data)
    {
        data = 0;
        if (paths.Count == 0) return false;

        var pidls = new nint[paths.Count];
        nint itemArray = 0;
        nint result = 0;
        try
        {
            for (int i = 0; i < pidls.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return false;

                int hr = SHParseDisplayName(path, 0, out pidls[i], SfgaoFileSystem, out uint attributes);
                if (hr < 0 || pidls[i] == 0 || (attributes & SfgaoFileSystem) == 0) return false;
            }

            if (SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out itemArray) < 0 || itemArray == 0)
                return false;

            // BindToHandler is the first IShellItemArray method after IUnknown.
            nint vtable = Marshal.ReadIntPtr(itemArray);
            nint bindAddress = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
            var bind = Marshal.GetDelegateForFunctionPointer<BindToHandler>(bindAddress);
            Guid handler = BhidDataObject;
            Guid iid = IidDataObject;
            if (bind(itemArray, 0, ref handler, ref iid, out result) < 0 || result == 0) return false;

            data = result;
            result = 0;
            return true;
        }
        finally
        {
            if (result != 0) Marshal.Release(result);
            if (itemArray != 0) Marshal.Release(itemArray);
            foreach (nint pidl in pidls)
            {
                if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    internal static int DoDragDrop(nint sourceHandle, nint data, out uint effect) =>
        SHDoDragDrop(sourceHandle, data, 0, DropEffectCopyMoveLink, out effect);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, nint bindingContext,
        out nint pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] pidls, out nint itemArray);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BindToHandler(nint self, nint bindingContext, ref Guid handler,
        ref Guid interfaceId, out nint result);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHDoDragDrop(nint window, nint data, nint dropSource,
        uint allowedEffects, out uint effect);
}
