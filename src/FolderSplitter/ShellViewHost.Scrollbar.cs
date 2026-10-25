using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private HorizontalScrollVisual? horizontalScrollbar;
    private bool updatingHorizontalScrollbar;
    private bool horizontalTracking;

    // Native scrollbar tracking paints inside its modal loop, before postpaint can run.
    // A separate opaque HWND clips those paints; HTTRANSPARENT leaves input with the list.
    private void PaintHorizontalScrollbar(ref Message message, ref bool hover)
    {
        if (message.Msg == 0x0082) // native list destroyed without an explicit view release
        {
            horizontalScrollbar?.Destroy();
            horizontalScrollbar = null;
            return;
        }
        bool print = message.Msg == 0x0317 && (message.LParam & 2) != 0; // WM_PRINT, PRF_NONCLIENT
        if (!print && message.Msg is not (0x0085 or 0x000F or 0x0005 or 0x0114
            or 0x00A0 or 0x00A1 or 0x00A2 or 0x0200 or 0x02A2 or 0x02A3
            or 0x007D or 0x0047 or 0x0018)) return;
        var bar = new HorizontalScrollNative.Bar { Size = Marshal.SizeOf<HorizontalScrollNative.Bar>() };
        if (!HorizontalScrollNative.GetScrollBarInfo(message.HWnd, -6, ref bar)) return;
        if (message.Msg == 0x00A0) // WM_NCMOUSEMOVE: screen coordinates
        {
            int x = (short)(long)message.LParam, y = (short)((long)message.LParam >> 16);
            hover = x >= bar.Bounds.Left && x < bar.Bounds.Right && y >= bar.Bounds.Top && y < bar.Bounds.Bottom;
            var track = new HorizontalScrollNative.Track
            {
                Size = Marshal.SizeOf<HorizontalScrollNative.Track>(),
                Flags = 0x12, Window = message.HWnd // TME_LEAVE | TME_NONCLIENT
            };
            HorizontalScrollNative.TrackMouseEvent(ref track);
        }
        else if (message.Msg is 0x0200 or 0x02A2 or 0x02A3) hover = false;
        UpdateHorizontalScrollbar(message.HWnd, hover);
        // A list-only print does not include its sibling visual. Keep print output faithful
        // without using a second paint over the native window DC during normal interaction.
        if (!print || showVerticalSeparators || (bar.State & 0x18000) != 0) return;
        if (!HorizontalScrollNative.GetWindowRect(message.HWnd, out var window)) return;
        var bounds = Rectangle.FromLTRB(bar.Bounds.Left - window.Left, bar.Bounds.Top - window.Top,
            bar.Bounds.Right - window.Left, bar.Bounds.Bottom - window.Top);
        DrawHorizontalScrollbar(message.WParam, bounds, bar, hover || horizontalTracking, message.HWnd);
    }

    private void UpdateHorizontalScrollbar(nint list, bool hover = false)
    {
        if (updatingHorizontalScrollbar || IsDisposed || Disposing) return;
        updatingHorizontalScrollbar = true;
        try
        {
            var bar = new HorizontalScrollNative.Bar { Size = Marshal.SizeOf<HorizontalScrollNative.Bar>() };
            bool visible = !showVerticalSeparators && DetailsNative.IsWindowVisible(list)
                && HorizontalScrollNative.GetScrollBarInfo(list, -6, ref bar) && (bar.State & 0x18000) == 0;
            if (!visible)
            {
                if (horizontalScrollbar is { Handle: not 0 } old) DetailsNative.ShowWindow(old.Handle, 0);
                return;
            }
            if (horizontalScrollbar is null)
            {
                horizontalScrollbar = new HorizontalScrollVisual(this);
                // Clipping must exist at the browser-root/visual sibling boundary, not just
                // on the visual itself. Preserve all native style bits.
                for (nint window = list; window != 0 && window != Handle; window = DetailsNative.GetParent(window))
                    HorizontalScrollNative.AddClipStyle(window, 0x04000000); // WS_CLIPSIBLINGS
                HorizontalScrollNative.AddClipStyle(Handle, 0x02000000); // WS_CLIPCHILDREN
                horizontalScrollbar.Create();
            }
            horizontalScrollbar.Update(list, bar, hover || horizontalTracking);
        }
        finally { updatingHorizontalScrollbar = false; }
    }

    private static void DrawHorizontalScrollbar(nint dc, Rectangle bounds, HorizontalScrollNative.Bar bar,
        bool expanded, nint list)
    {
        int saved = DetailsNative.SaveDC(dc);
        try
        {
            HorizontalScrollNative.IntersectClipRect(dc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            DetailsNative.FillColor(dc, new DetailsNative.Rect
            {
                Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom
            }, Theme.ListBack);
            float scale = DetailsNative.GetDpiForWindow(list) / 96f;
            int thickness = Math.Max(2, (int)Math.Round((expanded ? 6 : 2) * scale));
            int left = bounds.Left + bar.ThumbStart;
            int right = bounds.Left + bar.ThumbEnd;
            DetailsNative.SelectObject(dc, HorizontalScrollNative.GetStockObject(18)); // DC_BRUSH
            DetailsNative.SelectObject(dc, HorizontalScrollNative.GetStockObject(8)); // NULL_PEN
            HorizontalScrollNative.SetDCBrushColor(dc, ColorTranslator.ToWin32(Theme.Muted));
            if ((bar.ThumbState & 0x8001) == 0 && right > left)
            {
                int top = bounds.Top + (bounds.Height - thickness) / 2;
                nint thumb = HorizontalScrollNative.CreateRoundRectRgn(left, top, right + 1,
                    top + thickness + 1, thickness, thickness);
                try { HorizontalScrollNative.FillRgn(dc, thumb, HorizontalScrollNative.GetStockObject(18)); }
                finally { HorizontalScrollNative.DeleteObject(thumb); }
            }
            if (expanded)
            {
                int y = bounds.Top + bounds.Height / 2, d = Math.Max(2, (int)Math.Round(3 * scale));
                for (int side = 0; side < 2; side++)
                {
                    int x = side == 1 ? bounds.Right - bar.Button / 2 : bounds.Left + bar.Button / 2;
                    int direction = side == 1 ? 1 : -1;
                    HorizontalScrollNative.Polygon(dc, new Point[]
                    {
                        new(x + direction * d / 2, y), new(x - direction * d / 2, y - d),
                        new(x - direction * d / 2, y + d)
                    }, 3);
                }
            }
        }
        finally
        {
            DetailsNative.RestoreDC(dc, saved);
        }
    }

    private sealed class HorizontalScrollVisual(ShellViewHost owner) : NativeWindow
    {
        private nint list;
        private HorizontalScrollNative.Bar bar;
        private bool expanded;
        private Rectangle bounds;

        internal void Create() => CreateHandle(new CreateParams
        {
            Caption = "Horizontal scrollbar visual", Parent = owner.Handle,
            Style = 0x44000000, ExStyle = 0x08000000 // WS_CHILD | WS_CLIPSIBLINGS; WS_EX_NOACTIVATE
        });

        internal void Update(nint window, HorizontalScrollNative.Bar info, bool hover)
        {
            list = window;
            bar = info;
            expanded = hover;
            var origin = new Point(info.Bounds.Left, info.Bounds.Top);
            HorizontalScrollNative.ScreenToClient(owner.Handle, ref origin);
            var next = new Rectangle(origin, new Size(info.Bounds.Right - info.Bounds.Left, info.Bounds.Bottom - info.Bounds.Top));
            if (bounds != next || !DetailsNative.IsWindowVisible(Handle))
            {
                bounds = next;
                HorizontalScrollNative.SetWindowPos(Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x50); // NOACTIVATE | SHOWWINDOW
            }
            HorizontalScrollNative.InvalidateRect(Handle, 0, false);
            HorizontalScrollNative.UpdateWindow(Handle); // includes reentrant SB_THUMBTRACK, not queued until drag ends
        }

        internal void Destroy() => DestroyHandle();

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) // WM_NCHITTEST: underlying native bar handles click/capture/drag
            {
                message.Result = -1; // HTTRANSPARENT, same UI thread
                return;
            }
            if (message.Msg == 0x0014) { message.Result = 1; return; }
            if (message.Msg is 0x000F or 0x0318)
            {
                bool print = message.Msg == 0x0318;
                var paint = new HorizontalScrollNative.Paint();
                nint dc = print ? message.WParam : HorizontalScrollNative.BeginPaint(Handle, out paint);
                try { DrawHorizontalScrollbar(dc, new Rectangle(Point.Empty, bounds.Size), bar, expanded, list); }
                finally { if (!print) HorizontalScrollNative.EndPaint(Handle, ref paint); }
                return;
            }
            base.WndProc(ref message);
        }
    }

    private static class HorizontalScrollNative
    {
        internal static void AddClipStyle(nint window, int flag)
        {
            nint style = GetWindowLongPtr(window, -16);
            if ((style & flag) != 0) return;
            SetWindowLongPtr(window, -16, style | flag);
            SetWindowPos(window, 0, 0, 0, 0, 0, 0x37); // FRAMECHANGED | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Paint
        {
            public nint Dc;
            public int Erase;
            public DetailsNative.Rect Bounds;
            public int Restore, Incremental;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Bar
        {
            public int Size;
            public DetailsNative.Rect Bounds;
            public int Button, ThumbStart, ThumbEnd, Reserved;
            public uint State, First, Before, ThumbState, After, Last;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Track
        {
            public int Size, Flags;
            public nint Window;
            public int HoverTime;
        }

        [DllImport("user32.dll")]
        internal static extern bool GetScrollBarInfo(nint window, int objectId, ref Bar info);
        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(nint window, out DetailsNative.Rect bounds);
        [DllImport("user32.dll")]
        internal static extern bool TrackMouseEvent(ref Track track);
        [DllImport("user32.dll")]
        internal static extern bool ScreenToClient(nint window, ref Point point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        internal static extern bool InvalidateRect(nint window, nint rect, bool erase);
        [DllImport("user32.dll")]
        internal static extern bool UpdateWindow(nint window);
        [DllImport("user32.dll")]
        internal static extern nint BeginPaint(nint window, out Paint paint);
        [DllImport("user32.dll")]
        internal static extern bool EndPaint(nint window, ref Paint paint);
        [DllImport("gdi32.dll")]
        internal static extern int IntersectClipRect(nint dc, int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")]
        internal static extern nint GetStockObject(int index);
        [DllImport("gdi32.dll")]
        internal static extern int SetDCBrushColor(nint dc, int color);
        [DllImport("gdi32.dll")]
        internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")]
        internal static extern bool FillRgn(nint dc, nint region, nint brush);
        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(nint gdiObject);
        [DllImport("gdi32.dll")]
        internal static extern bool Polygon(nint dc, Point[] points, int count);
    }
}
