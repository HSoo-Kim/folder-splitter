using System.Runtime.InteropServices;

namespace QuadExplorer;

// File Explorer's View > Show options. They are Windows-wide settings, read and written through the same
// SHGetSetSettings call Explorer uses, so open Explorer windows follow the change too.
internal static class ShellSettings
{
    private const uint ShowAllObjects = 0x1, ShowExtensions = 0x2; // SSF_SHOWALLOBJECTS, SSF_SHOWEXTENSIONS

    internal static bool HiddenItemsShown
    {
        get => (Read(ShowAllObjects) & 0x1) != 0;
        set => Write(ShowAllObjects, value ? 0x1 : 0);
    }

    internal static bool ExtensionsShown
    {
        get => (Read(ShowExtensions) & 0x2) != 0;
        set => Write(ShowExtensions, value ? 0x2 : 0);
    }

    // SHELLSTATE starts with a 32-bit field whose low bits are fShowAllObjects and fShowExtensions.
    private static int Read(uint mask)
    {
        nint state = Marshal.AllocHGlobal(64);
        try
        {
            for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(state, i, 0);
            SHGetSetSettings(state, mask, false);
            return Marshal.ReadInt32(state);
        }
        finally { Marshal.FreeHGlobal(state); }
    }

    private static void Write(uint mask, int bits)
    {
        nint state = Marshal.AllocHGlobal(64);
        try
        {
            for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(state, i, 0);
            Marshal.WriteInt32(state, bits);
            SHGetSetSettings(state, mask, true);
        }
        finally { Marshal.FreeHGlobal(state); }
    }

    [DllImport("shell32.dll")]
    private static extern void SHGetSetSettings(nint state, uint mask, [MarshalAs(UnmanagedType.Bool)] bool set);
}
