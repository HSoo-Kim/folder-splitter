using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private void DrawGroupHeader(nint list, DetailsNative.CustomDraw draw)
    {
        var bounds = new DetailsNative.Rect { Top = 1 }; // LVGGR_HEADER
        if (DetailsNative.SendMessage(list, 0x1062, (nint)draw.Item, ref bounds) == 0) return;
        nint buffer = Marshal.AllocHGlobal(4096);
        int saved = DetailsNative.SaveDC(draw.Dc);
        try
        {
            var group = new GroupInfo
            {
                Size = (uint)Marshal.SizeOf<GroupInfo>(), Mask = 1 | 4,
                Header = buffer, HeaderMax = 2048, StateMask = uint.MaxValue
            };
            if (GroupMessage(list, 0x1095, (nint)draw.Item, ref group) == -1) return; // LVM_GETGROUPINFO
            // The Shell's header includes its localized title and item count. Keep that text and
            // the native bounds/hit targets; repaint only the header, never the group's file items.
            var background = bounds;
            // Classic collapsed groups leave a system-colored footer below HEADER. GROUP and the
            // draw bounds still include the former rows: stop at the next header to preserve it.
            if ((group.State & 1) != 0 && !showVerticalSeparators)
            {
                background.Bottom = draw.Bounds.Bottom;
                int groups = (int)SendMessage(list, 0x1098, 0, 0);
                for (int i = 0; i < groups; i++)
                {
                    var next = new GroupInfo { Size = (uint)Marshal.SizeOf<GroupInfo>(), Mask = 0x10 }; // LVGF_GROUPID
                    if (GroupMessage(list, 0x1099, i, ref next) == -1 || next.Id == (int)draw.Item) continue;
                    var nextHeader = new DetailsNative.Rect { Top = 1 };
                    if (DetailsNative.SendMessage(list, 0x1062, next.Id, ref nextHeader) != 0 && nextHeader.Top >= bounds.Bottom)
                        background.Bottom = Math.Min(background.Bottom, nextHeader.Top);
                }
            }
            DetailsNative.FillColor(draw.Dc, background, Theme.ListBack);
            DetailsNative.SelectObject(draw.Dc, textFont);
            DetailsNative.SetTextColor(draw.Dc, ColorTranslator.ToWin32(Theme.Text));
            DetailsNative.SetBkMode(draw.Dc, 1);
            float dpi = DetailsNative.GetDpiForWindow(list) / 96f;
            int padding = (int)Math.Round(8 * dpi);
            bool collapsible = (group.State & 8) != 0;
            var text = new DetailsNative.Rect { Top = 2 }; // LVGGR_LABEL
            if (DetailsNative.SendMessage(list, 0x1062, (nint)draw.Item, ref text) == 0) return;
            text.Right = bounds.Right - (collapsible ? (int)Math.Round(30 * dpi) : padding);
            const uint flags = 0x4 | 0x20 | 0x8000 | 0x800; // VCENTER | SINGLELINE | END_ELLIPSIS | NOPREFIX
            DetailsNative.DrawTextW(draw.Dc, buffer, -1, ref text, flags);
            var measured = text;
            DetailsNative.DrawTextW(draw.Dc, buffer, -1, ref measured, 0x400 | 0x20 | 0x800); // CALCRECT
            int middle = (text.Top + text.Bottom) / 2;
            int start = measured.Right + padding;
            if (start < text.Right)
                DetailsNative.Line(draw.Dc, Theme.Border, new Point(start, middle), new Point(text.Right, middle));
            if (collapsible)
            {
                // Classic and themed lists put this button at different heights and widths.
                // Query its native hit region so the visible glyph is also the clickable target.
                int top = -1, bottom = -1, left = -1, right = -1;
                var hit = new GroupHit { Point = new Point(bounds.Right - (int)Math.Round(7 * dpi), bounds.Top) };
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    hit.Point.Y = y;
                    GroupHitMessage(list, 0x1012, -1, ref hit);
                    if ((hit.Flags & 0x40000000) == 0) continue; // LVHT_EX_GROUP_COLLAPSE
                    if (top < 0) top = y;
                    bottom = y + 1;
                }
                if (top < 0) return;
                middle = (top + bottom) / 2;
                hit.Point.Y = middle;
                for (int xProbe = Math.Max(bounds.Left, bounds.Right - (int)Math.Round(40 * dpi)); xProbe < bounds.Right; xProbe++)
                {
                    hit.Point.X = xProbe;
                    GroupHitMessage(list, 0x1012, -1, ref hit);
                    if ((hit.Flags & 0x40000000) == 0) continue;
                    if (left < 0) left = xProbe;
                    right = xProbe + 1;
                }
                int x = (left + right) / 2;
                int radius = Math.Max(2, Math.Min((int)Math.Round(4 * dpi), (bottom - top - 2) / 2));
                bool collapsed = (group.State & 1) != 0;
                if (collapsed)
                    DetailsNative.Line(draw.Dc, Theme.Text, new Point(x - radius / 2, middle - radius),
                        new Point(x + radius / 2, middle), new Point(x - radius / 2, middle + radius));
                else
                    DetailsNative.Line(draw.Dc, Theme.Text, new Point(x - radius, middle - radius / 2),
                        new Point(x, middle + radius / 2), new Point(x + radius, middle - radius / 2));
            }
            if ((group.State & 0x10) != 0 && DetailsNative.GetFocus() == list && (SendMessage(list, 0x0129, 0, 0) & 1) == 0)
                DetailsNative.DrawFocusRect(draw.Dc, ref bounds);
        }
        finally
        {
            DetailsNative.RestoreDC(draw.Dc, saved);
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GroupInfo
    {
        public uint Size, Mask;
        public nint Header;
        public int HeaderMax;
        public nint Footer;
        public int FooterMax, Id;
        public uint StateMask, State, Align;
        public nint Subtitle;
        public uint SubtitleMax;
        public nint Task;
        public uint TaskMax;
        public nint DescriptionTop;
        public uint DescriptionTopMax;
        public nint DescriptionBottom;
        public uint DescriptionBottomMax;
        public int TitleImage, ExtendedImage, FirstItem;
        public uint Items;
        public nint SubsetTitle;
        public uint SubsetTitleMax;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint GroupMessage(nint window, int message, nint wParam, ref GroupInfo group);
    [StructLayout(LayoutKind.Sequential)]
    private struct GroupHit { public Point Point; public uint Flags; public int Item, SubItem, Group; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint GroupHitMessage(nint window, int message, nint wParam, ref GroupHit hit);
}
