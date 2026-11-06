using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace QuadExplorer;

internal static class ShellDropDescription
{
    private static readonly short Format = unchecked((short)DataFormats.GetFormat("DropDescription").Id);
    private static readonly nuint DescriptionSize = (nuint)Marshal.SizeOf<DropDescription>();

    internal static void Set(ComDataObject data, string? description)
    {
        // CFSTR_DROPDESCRIPTION requires a movable HGLOBAL, not a managed DataObject copy.
        nint memory = GlobalAlloc(0x0042, DescriptionSize); // GMEM_MOVEABLE | GMEM_ZEROINIT
        if (memory == 0) return;
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        try
        {
            nint buffer = GlobalLock(memory);
            if (buffer == 0) return;
            try
            {
                var value = new DropDescription
                {
                    Image = description is null ? -1 : 4, // DROPIMAGE_INVALID / DROPIMAGE_LINK
                    Message = description ?? "",
                    Insert = ""
                };
                Marshal.StructureToPtr(value, buffer, false);
            }
            finally { GlobalUnlock(memory); }
            var format = new FORMATETC
            {
                cfFormat = Format, dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1, tymed = TYMED.TYMED_HGLOBAL
            };
            data.SetData(ref format, ref medium, true);
            // A successful fRelease transfers the HGLOBAL to the original drag source.
            medium = default;
        }
        catch (Exception error) when (error is COMException or NotImplementedException or NotSupportedException or ArgumentException)
        {
            // Some drag sources are read-only or do not accept Shell drop descriptions.
        }
        finally
        {
            if (medium.tymed != TYMED.TYMED_NULL) ShellNative.ReleaseStgMedium(ref medium);
        }
    }

    // shlobj_core.h: DROPIMAGETYPE followed by two WCHAR[MAX_PATH] buffers (1044 bytes).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DropDescription
    {
        public int Image;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Message;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Insert;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);
}
