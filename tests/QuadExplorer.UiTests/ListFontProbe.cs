using System.Runtime.InteropServices;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProbeLogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, FontCharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", CharSet = CharSet.Unicode)]
    private static extern int GetObject(nint font, int size, ref ProbeLogFont logFont);

    // The font face the Shell list window inside a folder view actually draws with (WM_GETFONT).
    private static string ListWindowFace(nint viewHost)
    {
        nint list = 0;
        EnumChildWindows(viewHost, (window, _) =>
        {
            var name = new System.Text.StringBuilder(32);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "SysListView32") list = window;
            return list == 0;
        }, 0);
        var logFont = new ProbeLogFont();
        return list != 0 && GetObject(SendMessage(list, 0x0031, 0, 0), Marshal.SizeOf<ProbeLogFont>(), ref logFont) > 0 ? logFont.FaceName : "";
    }
}
