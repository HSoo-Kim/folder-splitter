using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private bool showVerticalSeparators = true;
    private bool comfortableRows;
    private bool highlightSortedColumn;
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
    private int sortedColumn = -1;

    public void SetDetailsAppearance(bool showVerticalSeparators, bool comfortableRows, bool highlightSortedColumn = false)
    {
        this.showVerticalSeparators = showVerticalSeparators;
        this.comfortableRows = comfortableRows;
        this.highlightSortedColumn = highlightSortedColumn;
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
            // Switching from Details to the icon views leaves the Shell's column header visible over the
            // items; File Explorer shows no header outside Details.
            if (header != 0 && SendMessage(list, 0x108F, 0, 0) != 1 && DetailsNative.IsWindowVisible(header))
                DetailsNative.ShowWindow(header, 0); // SW_HIDE
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
                visual.SetColor(3, ColorTranslator.ToWin32(highlightSortedColumn ? Theme.SortColumn : Theme.ListBack));
                visual.SetColor(4, ColorTranslator.ToWin32(Theme.Muted));
                visual.SetColor(5, ColorTranslator.ToWin32(Theme.ListBack));
            }
            // Native sorted-column painting overwrites the custom selection on that column.
            // Keep sorting native; apply its optional color through the same draw path as selection.
            SendMessage(list, 0x108C, -1, 0); // LVM_SETSELECTEDCOLUMN
            DetailsNative.RedrawWindow(list, 0, 0, 0x0001 | 0x0004 | 0x0080);
            UpdateScrollbars(list);
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
        ReleaseScrollbars();
    }

    private enum DetailsWindowKind { Container, List, Header }

    private sealed class DetailsWindow(ShellViewHost owner, DetailsWindowKind kind) : NativeWindow
    {
        private bool horizontalHover, verticalHover;

        protected override void WndProc(ref Message message)
        {
            if (kind == DetailsWindowKind.Header && message.Msg == 0x14 && message.WParam != 0) // WM_ERASEBKGND
            {
                DetailsNative.GetClientRect(Handle, out var background);
                DetailsNative.FillColor(message.WParam, background, Theme.ListBack);
                message.Result = 1;
                return;
            }
            bool external = !owner.applyingDetailsAppearance;
            if (kind == DetailsWindowKind.List && message.Msg == 0x108C)
                message.WParam = -1; // sorting still updates header flags and row order
            bool nativeDrag = kind == DetailsWindowKind.List && message.Msg is 0x00A1 or 0x00A3;
            bool horizontalDrag = nativeDrag && message.WParam == 6;
            bool verticalDrag = nativeDrag && message.WParam == 7;
            if (horizontalDrag || verticalDrag)
            {
                owner.horizontalTracking |= horizontalDrag;
                owner.verticalTracking |= verticalDrag;
                owner.UpdateScrollbars(Handle, horizontalHover || horizontalDrag, verticalHover || verticalDrag);
            }
            base.WndProc(ref message);
            if (horizontalDrag) owner.horizontalTracking = false;
            if (verticalDrag) owner.verticalTracking = false;
            if (kind == DetailsWindowKind.List)
                owner.PaintScrollbars(ref message, ref horizontalHover, ref verticalHover);
            if (message.Msg == 0x004E && message.LParam != 0 && kind == DetailsWindowKind.List
                && Marshal.ReadIntPtr(message.LParam) == owner.detailsHeader?.Handle
                && Marshal.ReadInt32(message.LParam, 2 * IntPtr.Size) == -12)
                DrawHeader(ref message);
            if (kind == DetailsWindowKind.Container && message.Msg == 0x004E && message.LParam != 0)
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
            if (SendMessage(list, 0x108F, 0, 0) != 1) return;
            int stage = Marshal.ReadInt32(message.LParam, 3 * IntPtr.Size);
            nint dc = Marshal.ReadIntPtr(message.LParam, 4 * IntPtr.Size);
            if (stage == 0x1)
            {
                owner.sortedColumn = -1;
                nint header = owner.detailsHeader?.Handle ?? 0;
                int count = (int)SendMessage(header, 0x1200, 0, 0);
                for (int i = 0; owner.highlightSortedColumn && i < count; i++)
                {
                    var item = new DetailsNative.HeaderItem { Mask = 0x4 }; // HDI_FORMAT
                    DetailsNative.HeaderMessage(header, 0x120B, i, ref item);
                    if ((item.Format & 0x600) == 0) continue;
                    owner.sortedColumn = i;
                    var column = new DetailsNative.Rect();
                    DetailsNative.SendMessage(header, 0x1207, i, ref column);
                    DetailsNative.GetClientRect(list, out var client);
                    column.Top = column.Bottom;
                    column.Bottom = client.Bottom;
                    // Shell may reuse cached rows on an incremental repaint. Tinting the whole
                    // column here erases their names/icons without item notifications to repaint them.
                    // Item backgrounds are set below; only the space after the last row is ours.
                    int items = (int)SendMessage(list, 0x1004, 0, 0); // LVM_GETITEMCOUNT
                    if (items > 0)
                    {
                        var last = new DetailsNative.Rect();
                        if (DetailsNative.SendMessage(list, 0x100E, items - 1, ref last) != 0)
                            column.Top = Math.Max(column.Top, last.Bottom);
                    }
                    if (column.Top < column.Bottom) DetailsNative.FillColor(dc, column, Theme.SortColumn);
                    break;
                }
                message.Result |= 0x20; // CDRF_NOTIFYITEMDRAW
            }
            else if (stage is 0x10001 or 0x30001) // CDDS_ITEMPREPAINT, with CDDS_SUBITEM
            {
                if (owner.rowFont != 0) DetailsNative.SelectObject(dc, owner.textFont);
                var draw = Marshal.PtrToStructure<DetailsNative.ListCustomDraw>(message.LParam);
                if (draw.ItemType == 0)
                {
                    bool selected = (SendMessage(list, 0x102C, (nint)draw.Draw.Item, 2) & 2) != 0; // LVIS_SELECTED
                    int column = stage == 0x10001 ? 0 : draw.SubItem;
                    Color back = selected ? Theme.Selected : column == owner.sortedColumn ? Theme.SortColumn : Theme.ListBack;
                    draw.Text = ColorTranslator.ToWin32(Theme.Text);
                    draw.TextBack = draw.Face = ColorTranslator.ToWin32(back);
                    if (selected)
                    {
                        // Keep native icons, overlays and text, but not the system's per-cell selection boxes.
                        draw.Draw.State &= ~(1u | 0x10u);
                        if (stage == 0x10001)
                        {
                            DetailsNative.GetClientRect(list, out var client);
                            var row = draw.Draw.Bounds;
                            row.Left = 0;
                            row.Right = client.Right;
                            DetailsNative.FillColor(dc, row, Theme.Selected);
                        }
                    }
                    Marshal.StructureToPtr(draw, message.LParam, false);
                }
                message.Result |= stage == 0x10001 ? 0x32 : 0x2; // CDRF_NEWFONT, subitem and item-postpaint notifications
            }
            else if (stage == 0x10002 && DetailsNative.GetFocus() == list && (SendMessage(list, 0x0129, 0, 0) & 1) == 0)
            {
                var draw = Marshal.PtrToStructure<DetailsNative.CustomDraw>(message.LParam);
                if ((SendMessage(list, 0x102C, (nint)draw.Item, 1) & 1) == 0) return;
                DetailsNative.GetClientRect(list, out var bounds);
                bounds.Top = draw.Bounds.Top;
                bounds.Bottom = draw.Bounds.Bottom;
                int saved = DetailsNative.SaveDC(dc);
                DetailsNative.SetTextColor(dc, ColorTranslator.ToWin32(Theme.Text));
                DetailsNative.DrawFocusRect(dc, ref bounds);
                DetailsNative.RestoreDC(dc, saved);
            }
        }

        private void DrawHeader(ref Message message)
        {
            var draw = Marshal.PtrToStructure<DetailsNative.CustomDraw>(message.LParam);
            nint header = owner.detailsHeader!.Handle;
            if (draw.Stage == 1)
            {
                DetailsNative.GetClientRect(header, out var client);
                DetailsNative.FillColor(draw.Dc, client, Theme.ListBack);
                message.Result = 0x30; // CDRF_NOTIFYITEMDRAW | CDRF_NOTIFYPOSTPAINT
                return;
            }
            if (draw.Stage == 2) // CDDS_POSTPAINT: the native theme paints the trailing empty header too.
            {
                DetailsNative.GetClientRect(header, out var tail);
                int count = (int)SendMessage(header, 0x1200, 0, 0), right = 0;
                for (int i = 0; i < count; i++)
                {
                    var itemRect = new DetailsNative.Rect();
                    DetailsNative.SendMessage(header, 0x1207, i, ref itemRect); // HDM_GETITEMRECT
                    right = Math.Max(right, itemRect.Right);
                }
                tail.Left = right;
                if (tail.Left < tail.Right) DetailsNative.FillColor(draw.Dc, tail, Theme.ListBack);
                return;
            }
            if (draw.Stage != 0x10001) return;
            int saved = DetailsNative.SaveDC(draw.Dc);
            nint buffer = Marshal.AllocHGlobal(1024);
            try
            {
                var item = new DetailsNative.HeaderItem { Mask = 0x2 | 0x4, Text = buffer, TextMax = 512 };
                if (DetailsNative.HeaderMessage(header, 0x120B, (nint)draw.Item, ref item) == 0) return; // HDM_GETITEMW
                var bounds = draw.Bounds;
                float dpi = DetailsNative.GetDpiForWindow(header) / 96f;
                DetailsNative.FillColor(draw.Dc, bounds, (draw.State & 0x40) != 0 ? Theme.Hover : Theme.ListBack);
                DetailsNative.Line(draw.Dc, Theme.Border,
                    new Point(bounds.Right - 1, bounds.Top), new Point(bounds.Right - 1, bounds.Bottom - 1));
                var textBounds = bounds;
                int padding = (int)Math.Round(6 * dpi);
                textBounds.Left += padding;
                textBounds.Right -= padding;
                if ((item.Format & 0x40) != 0) // HDF_CHECKBOX (Explorer's select-all checkbox)
                {
                    int size = (int)Math.Round(13 * dpi);
                    var check = new Rectangle(textBounds.Left, bounds.Top + (bounds.Bottom - bounds.Top - size) / 2, size, size);
                    DetailsNative.Line(draw.Dc, Theme.Muted, new Point(check.Left, check.Top),
                        new Point(check.Right, check.Top), new Point(check.Right, check.Bottom),
                        new Point(check.Left, check.Bottom), new Point(check.Left, check.Top));
                    if ((item.Format & 0x80) != 0)
                    {
                        DetailsNative.Line(draw.Dc, Theme.Text, new Point(check.Left + 3, check.Top + size / 2),
                            new Point(check.Left + size / 2, check.Bottom - 3), new Point(check.Right - 2, check.Top + 3));
                    }
                    textBounds.Left += size + padding;
                }
                DetailsNative.SelectObject(draw.Dc, owner.textFont);
                DetailsNative.SetTextColor(draw.Dc, ColorTranslator.ToWin32(Theme.Text));
                DetailsNative.SetBkMode(draw.Dc, 1); // TRANSPARENT
                uint flags = 0x4 | 0x20 | 0x8000 | 0x800; // DT_VCENTER | SINGLELINE | END_ELLIPSIS | NOPREFIX
                flags |= (item.Format & 3) switch { 1 => 2u, 2 => 1u, _ => 0u };
                DetailsNative.DrawTextW(draw.Dc, buffer, -1, ref textBounds, flags);
                if ((item.Format & 0x600) != 0)
                {
                    int x = (bounds.Left + bounds.Right) / 2, y = bounds.Top + (int)Math.Round(3 * dpi);
                    int radius = Math.Max(2, (int)Math.Round(3 * dpi));
                    bool up = (item.Format & 0x400) != 0;
                    DetailsNative.Line(draw.Dc, Theme.Muted, new Point(x - radius, y + (up ? radius : 0)),
                        new Point(x, y + (up ? 0 : radius)), new Point(x + radius, y + (up ? radius : 0)));
                }
                message.Result = 4; // CDRF_SKIPDEFAULT: Windows theme text/background do not follow our palette.
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                DetailsNative.RestoreDC(draw.Dc, saved);
            }
        }
    }

    private static class DetailsNative
    {
        internal static void FillColor(nint dc, Rect rect, Color color)
        {
            int previous = SetDCBrushColor(dc, ColorTranslator.ToWin32(color));
            FillRect(dc, ref rect, GetStockObject(18)); // DC_BRUSH
            SetDCBrushColor(dc, previous);
        }

        internal static void Line(nint dc, Color color, params Point[] points)
        {
            nint previous = SelectObject(dc, GetStockObject(19)); // DC_PEN
            int oldColor = SetDCPenColor(dc, ColorTranslator.ToWin32(color));
            Polyline(dc, points, points.Length);
            SetDCPenColor(dc, oldColor);
            SelectObject(dc, previous);
        }

        [DllImport("gdi32.dll")] internal static extern int SaveDC(nint dc);
        [DllImport("gdi32.dll")] internal static extern bool RestoreDC(nint dc, int saved);
        [DllImport("gdi32.dll")] internal static extern int SetTextColor(nint dc, int color);
        [DllImport("gdi32.dll")] internal static extern int SetBkMode(nint dc, int mode);
        [DllImport("gdi32.dll")] private static extern int SetDCBrushColor(nint dc, int color);
        [DllImport("gdi32.dll")] private static extern int SetDCPenColor(nint dc, int color);
        [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
        [DllImport("gdi32.dll")] private static extern bool Polyline(nint dc, Point[] points, int count);
        [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int DrawTextW(nint dc, nint text, int length, ref Rect rect, uint format);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct NotifyHeader
        {
            public nint Window;
            public nuint Id;
            public int Code;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct CustomDraw
        {
            public NotifyHeader Header;
            public uint Stage;
            public nint Dc;
            public Rect Bounds;
            public nuint Item;
            public uint State;
            public nint ItemData;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct ListCustomDraw
        {
            public CustomDraw Draw;
            public int Text, TextBack, SubItem;
            public uint ItemType;
            public int Face, IconEffect, IconPhase, Part, State;
            public Rect TextBounds;
            public uint Align;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct HeaderItem
        {
            public uint Mask;
            public int Width;
            public nint Text, Bitmap;
            public int TextMax, Format;
            public nint Data;
            public int Image, Order;
            public uint Type;
            public nint Filter;
            public uint State;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TextMetric
        {
            public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang,
                DigitizedAspectX, DigitizedAspectY;
            public char FirstChar, LastChar, DefaultChar, BreakChar;
            public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
        }
        [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll")] internal static extern nint GetFocus();
        [DllImport("user32.dll")] internal static extern bool DrawFocusRect(nint dc, ref Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern nint HeaderMessage(nint window, int message, nint wParam, ref HeaderItem item);
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
