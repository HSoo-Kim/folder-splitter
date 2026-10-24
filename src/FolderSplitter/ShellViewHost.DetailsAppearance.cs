using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private bool showVerticalSeparators = true;
    private bool comfortableRows;
    private bool applyingDetailsAppearance;
    private bool detailsAppearanceQueued;
    private readonly List<DetailsWindow> detailsWindows = [];
    private DetailsWindow? detailsList;
    private DetailsWindow? detailsHeader;
    // Comfortable rows: the list measures its rows with a taller font, and custom draw paints every
    // label with the normal font. A taller state image list would grow rows too, but the Shell
    // then stretches each file icon to the row height.
    private nint rowFont;
    private nint textFont;
    private bool rowCalibrated;

    public void SetDetailsAppearance(bool showVerticalSeparators, bool comfortableRows)
    {
        this.showVerticalSeparators = showVerticalSeparators;
        this.comfortableRows = comfortableRows;
        ApplyDetailsAppearance();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyListFont();
        ApplyDetailsAppearance();
    }

    private void ApplyDetailsAppearance()
    {
        if (applyingDetailsAppearance || !IsHandleCreated || Disposing || IsDisposed || view is null) return;
        applyingDetailsAppearance = true;
        try
        {
            nint list = 0;
            EnumChildWindows(Handle, (window, _) =>
            {
                var name = new System.Text.StringBuilder(32);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "SysListView32") return true;
                list = window;
                return false;
            }, 0);
            if (list == 0) return;
            if (detailsList?.Handle != list)
            {
                ReleaseDetailsAppearance();
                detailsList = WatchDetailsWindow(list, DetailsWindowKind.List);
                // Native child creation is reported to its immediate parent, not the managed host.
                // Watch the native ancestors as well so a replacement list is discovered.
                for (nint parent = DetailsNative.GetParent(list); parent != 0 && parent != Handle;
                    parent = DetailsNative.GetParent(parent))
                    WatchDetailsWindow(parent, DetailsWindowKind.Container);
            }
            nint header = SendMessage(list, 0x101F, 0, 0); // LVM_GETHEADER
            if (header != 0 && detailsHeader?.Handle != header)
            {
                if (detailsHeader is { } oldHeader)
                {
                    oldHeader.ReleaseHandle();
                    detailsWindows.Remove(oldHeader);
                }
                detailsHeader = WatchDetailsWindow(header, DetailsWindowKind.Header);
            }
            ApplyRowSpacing(list, header);
            // Both Explorer and ItemsView draw body separators. Disable body visual styles only
            // when requested; native list colors and the independently themed header remain intact.
            DetailsNative.SetWindowTheme(list, showVerticalSeparators
                ? Theme.IsDark ? "DarkMode_ItemsView" : "ItemsView" : "", showVerticalSeparators ? null : "");
            if (!showVerticalSeparators) SendMessage(list, 0x1036, 1, 0); // clear LVS_EX_GRIDLINES only
            if (header != 0)
                DetailsNative.SetWindowTheme(header, Theme.IsDark ? "DarkMode_ItemsView" : "ItemsView", null);
            // SetWindowTheme may reset the Shell's sorted-column brush. Restore its persistent colors
            // after applying the body class, rather than relying on the class's default sort highlight.
            if (view is IVisualProperties visual)
            {
                visual.SetColor(1, ColorTranslator.ToWin32(Theme.Text));
                visual.SetColor(2, ColorTranslator.ToWin32(Theme.ListBack));
                visual.SetColor(3, ColorTranslator.ToWin32(Theme.SortColumn));
                visual.SetColor(4, ColorTranslator.ToWin32(Theme.Muted));
                visual.SetColor(5, ColorTranslator.ToWin32(Theme.ListBack));
            }
            DetailsNative.RedrawWindow(list, 0, 0, 0x0001 | 0x0004 | 0x0080);
        }
        finally { applyingDetailsAppearance = false; }
    }

    private DetailsWindow WatchDetailsWindow(nint window, DetailsWindowKind kind)
    {
        var watcher = new DetailsWindow(this, kind);
        watcher.AssignHandle(window);
        detailsWindows.Add(watcher);
        return watcher;
    }

    private void QueueDetailsAppearance()
    {
        if (detailsAppearanceQueued || applyingDetailsAppearance || !IsHandleCreated || Disposing || IsDisposed) return;
        detailsAppearanceQueued = true;
        BeginInvoke(() =>
        {
            detailsAppearanceQueued = false;
            ApplyDetailsAppearance();
        });
    }

    private void ApplyRowSpacing(nint list, nint header)
    {
        bool details = SendMessage(list, 0x108F, 0, 0) == 1; // LVM_GETVIEW: LV_VIEW_DETAILS
        var (face, points) = Theme.ListFont ?? (SystemFonts.IconTitleFont!.Name, SystemFonts.IconTitleFont.SizeInPoints);
        textFont = ListFonts.Get(face, -(int)Math.Round(points * DeviceDpi / 72d));
        if (!comfortableRows || !details)
        {
            if (rowFont == 0) return;
            rowFont = 0;
            SetFonts(list, header, textFont);
            return;
        }
        int dpi = (int)DetailsNative.GetDpiForWindow(list);
        int target = (int)Math.Round(28d * dpi / 96);
        SendMessage(list, 0x0030, textFont, 0); // WM_SETFONT
        int textHeight = TextHeight(list, textFont);
        int row = RowHeight(list);
        rowCalibrated = row > 0;
        if (!rowCalibrated) row = textHeight + (int)Math.Round(4d * dpi / 96);
        if (row >= target)
        {
            rowFont = 0;
            SetFonts(list, header, textFont);
            return;
        }
        int cell = textHeight + target - row;
        for (int pass = 0; pass < 3; pass++)
        {
            rowFont = ListFonts.Get(face, cell);
            SendMessage(list, 0x0030, rowFont, 0);
            int measured = RowHeight(list);
            if (measured <= 0 || measured == target) break;
            cell += target - measured;
        }
        if (header != 0) SendMessage(header, 0x0030, textFont, 1);
        nint tips = SendMessage(list, 0x104E, 0, 0); // LVM_GETTOOLTIPS: name tips for truncated labels
        if (tips != 0) SendMessage(tips, 0x0030, textFont, 0);
    }

    private static void SetFonts(nint list, nint header, nint font)
    {
        SendMessage(list, 0x0030, font, 1);
        if (header != 0) SendMessage(header, 0x0030, font, 1);
        nint tips = SendMessage(list, 0x104E, 0, 0);
        if (tips != 0) SendMessage(tips, 0x0030, font, 0);
    }

    private static int RowHeight(nint list)
    {
        var bounds = new DetailsNative.Rect(); // LVIR_BOUNDS
        return DetailsNative.SendMessage(list, 0x100E, 0, ref bounds) == 0 ? 0 : bounds.Bottom - bounds.Top;
    }

    private static int TextHeight(nint window, nint font)
    {
        nint dc = DetailsNative.GetDC(window);
        try
        {
            nint old = DetailsNative.SelectObject(dc, font);
            DetailsNative.GetTextMetricsW(dc, out var metrics);
            DetailsNative.SelectObject(dc, old);
            return metrics.Height;
        }
        finally { DetailsNative.ReleaseDC(window, dc); }
    }

    private void ReleaseDetailsAppearance()
    {
        foreach (var watcher in detailsWindows)
            if (watcher.Handle != 0) watcher.ReleaseHandle();
        detailsWindows.Clear();
        detailsList = detailsHeader = null;
        rowFont = 0;
    }

    private enum DetailsWindowKind { Container, List, Header }

    private sealed class DetailsWindow(ShellViewHost owner, DetailsWindowKind kind) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            bool external = !owner.applyingDetailsAppearance;
            base.WndProc(ref message);
            if (kind == DetailsWindowKind.Container && message.Msg == 0x004E && owner.rowFont != 0) // WM_NOTIFY
                DrawWithTextFont(ref message);
            if (!external) return;
            if (message.Msg == 0x031A && kind != DetailsWindowKind.Container) // WM_THEMECHANGED
                owner.ApplyDetailsAppearance();
            else if (message.Msg is 0x0210 or 0x02E3 || // WM_PARENTNOTIFY, WM_DPICHANGED_AFTERPARENT
                kind == DetailsWindowKind.List && (message.Msg is 0x108E or 0x1003 or 0x0030
                    || message.Msg == 0x104D && !owner.rowCalibrated)) // LVM_SETVIEW / SETIMAGELIST / WM_SETFONT / INSERTITEMW
                owner.QueueDetailsAppearance();
        }

        private void DrawWithTextFont(ref Message message)
        {
            nint list = owner.detailsList?.Handle ?? 0;
            if (list == 0 || Marshal.ReadIntPtr(message.LParam) != list) return;
            int code = Marshal.ReadInt32(message.LParam, 2 * IntPtr.Size);
            if (code == -175 && message.Result == 0) // LVN_BEGINLABELEDITW: rename box uses the normal font
            {
                nint edit = SendMessage(list, 0x1018, 0, 0); // LVM_GETEDITCONTROL
                if (edit != 0) SendMessage(edit, 0x0030, owner.textFont, 0);
                return;
            }
            if (code != -12) return; // NM_CUSTOMDRAW
            int stage = Marshal.ReadInt32(message.LParam, 3 * IntPtr.Size);
            nint dc = Marshal.ReadIntPtr(message.LParam, 4 * IntPtr.Size);
            if (stage == 0x1) message.Result |= 0x20; // CDDS_PREPAINT: CDRF_NOTIFYITEMDRAW
            else if (stage is 0x10001 or 0x30001) // CDDS_ITEMPREPAINT, with CDDS_SUBITEM
            {
                DetailsNative.SelectObject(dc, owner.textFont);
                message.Result |= stage == 0x10001 ? 0x22 : 0x2; // CDRF_NEWFONT, items also CDRF_NOTIFYSUBITEMDRAW
            }
        }
    }

    private static class DetailsNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TextMetric
        {
            public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang,
                DigitizedAspectX, DigitizedAspectY;
            public char FirstChar, LastChar, DefaultChar, BreakChar;
            public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
        }
        [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll")] internal static extern bool RedrawWindow(nint window, nint rect, nint region, uint flags);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern nint SendMessage(nint window, int message, nint wParam, ref Rect rect);
        [DllImport("user32.dll")] internal static extern nint GetDC(nint window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
        [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint gdiObject);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetTextMetricsW(nint dc, out TextMetric metrics);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(nint window, string appName, string? idList);
    }
}
