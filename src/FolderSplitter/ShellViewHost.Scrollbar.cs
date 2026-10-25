using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private ScrollbarVisual? horizontalScrollbar;
    private ScrollbarVisual? verticalScrollbar;
    private bool updatingScrollbars;
    private bool horizontalTracking, verticalTracking;

    // Native scrollbar tracking paints inside its modal loop, before postpaint can run.
    // A separate opaque HWND clips those paints; HTTRANSPARENT leaves input with the list.
    private void PaintScrollbars(ref Message message, ref bool horizontalHover, ref bool verticalHover)
    {
        if (message.Msg == 0x0082) // native list destroyed without an explicit view release
        {
            ReleaseScrollbars();
            return;
        }
        bool print = message.Msg == 0x0317 && (message.LParam & 2) != 0; // WM_PRINT, PRF_NONCLIENT
        if (!print && message.Msg is not (0x0085 or 0x000F or 0x0005 or 0x0114 or 0x0115
            or 0x00A0 or 0x00A1 or 0x00A2 or 0x0200 or 0x02A2 or 0x02A3
            or 0x007D or 0x0047 or 0x0018 or 0x020A or 0x020E or 0x0100 or 0x1013 or 0x1014)) return;
        if (message.Msg == 0x00A0) // WM_NCMOUSEMOVE: screen coordinates
        {
            int x = (short)(long)message.LParam, y = (short)((long)message.LParam >> 16);
            horizontalHover = PointerInScrollbar(message.HWnd, -6, x, y);
            verticalHover = PointerInScrollbar(message.HWnd, -5, x, y);
            var track = new ScrollbarNative.Track
            {
                Size = Marshal.SizeOf<ScrollbarNative.Track>(),
                Flags = 0x12, Window = message.HWnd // TME_LEAVE | TME_NONCLIENT
            };
            ScrollbarNative.TrackMouseEvent(ref track);
        }
        else if (message.Msg is 0x0200 or 0x02A2 or 0x02A3) horizontalHover = verticalHover = false;
        UpdateScrollbars(message.HWnd, horizontalHover, verticalHover);
        // A list-only print does not include its sibling visual. Keep print output faithful
        // without using a second paint over the native window DC during normal interaction.
        if (!print || !ScrollbarNative.GetWindowRect(message.HWnd, out var window)) return;
        for (int axis = 0; axis < 2; axis++)
        {
            bool vertical = axis == 1;
            if (!vertical && showVerticalSeparators) continue;
            var bar = new ScrollbarNative.Bar { Size = Marshal.SizeOf<ScrollbarNative.Bar>() };
            if (!ScrollbarNative.GetScrollBarInfo(message.HWnd, vertical ? -5 : -6, ref bar) || (bar.State & 0x18000) != 0) continue;
            var bounds = ScrollbarBounds(message.HWnd, bar, vertical);
            bounds.Offset(-window.Left, -window.Top);
            DrawScrollbar(message.WParam, bounds, bar, vertical,
                vertical ? verticalHover || verticalTracking : horizontalHover || horizontalTracking, message.HWnd);
        }
    }

    private static bool PointerInScrollbar(nint list, int objectId, int x, int y)
    {
        var bar = new ScrollbarNative.Bar { Size = Marshal.SizeOf<ScrollbarNative.Bar>() };
        return ScrollbarNative.GetScrollBarInfo(list, objectId, ref bar) && (bar.State & 0x18000) == 0
            && x >= bar.Bounds.Left && x < bar.Bounds.Right && y >= bar.Bounds.Top && y < bar.Bounds.Bottom;
    }

    private static Rectangle ScrollbarBounds(nint list, ScrollbarNative.Bar bar, bool vertical)
    {
        var bounds = Rectangle.FromLTRB(bar.Bounds.Left, bar.Bounds.Top, bar.Bounds.Right, bar.Bounds.Bottom);
        if (vertical)
        {
            var horizontal = new ScrollbarNative.Bar { Size = Marshal.SizeOf<ScrollbarNative.Bar>() };
            if (ScrollbarNative.GetScrollBarInfo(list, -6, ref horizontal) && (horizontal.State & 0x18000) == 0)
                bounds.Height = Math.Max(bounds.Bottom, horizontal.Bounds.Bottom) - bounds.Top;
        }
        return bounds;
    }

    private void ReleaseScrollbars()
    {
        horizontalScrollbar?.Destroy();
        horizontalScrollbar = null;
        verticalScrollbar?.Destroy();
        verticalScrollbar = null;
        horizontalTracking = verticalTracking = false;
    }

    private void UpdateScrollbars(nint list, bool horizontalHover = false, bool verticalHover = false)
    {
        if (updatingScrollbars || IsDisposed || Disposing) return;
        updatingScrollbars = true;
        try
        {
            for (int axis = 0; axis < 2; axis++)
            {
                bool vertical = axis == 1;
                ref ScrollbarVisual? visual = ref (vertical ? ref verticalScrollbar : ref horizontalScrollbar);
                var bar = new ScrollbarNative.Bar { Size = Marshal.SizeOf<ScrollbarNative.Bar>() };
                bool visible = (vertical || !showVerticalSeparators) && DetailsNative.IsWindowVisible(list)
                    && ScrollbarNative.GetScrollBarInfo(list, vertical ? -5 : -6, ref bar) && (bar.State & 0x18000) == 0;
                if (!visible)
                {
                    if (visual is { Handle: not 0 }) DetailsNative.ShowWindow(visual.Handle, 0);
                    continue;
                }
                if (visual is null)
                {
                    visual = new ScrollbarVisual(this, vertical);
                    // Clipping must exist at the browser-root/visual sibling boundary, not just
                    // on the visual itself. Preserve all native style bits.
                    for (nint window = list; window != 0 && window != Handle; window = DetailsNative.GetParent(window))
                        ScrollbarNative.AddClipStyle(window, 0x04000000); // WS_CLIPSIBLINGS
                    ScrollbarNative.AddClipStyle(Handle, 0x02000000); // WS_CLIPCHILDREN
                    visual.Create();
                }
                visual.Update(list, bar, vertical ? verticalHover || verticalTracking : horizontalHover || horizontalTracking);
            }
        }
        finally { updatingScrollbars = false; }
    }

    private static void DrawScrollbar(nint dc, Rectangle bounds, ScrollbarNative.Bar bar,
        bool vertical, bool expanded, nint list)
    {
        int saved = DetailsNative.SaveDC(dc);
        try
        {
            ScrollbarNative.IntersectClipRect(dc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            DetailsNative.FillColor(dc, new DetailsNative.Rect
            {
                Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom
            }, Theme.ListBack);
            float scale = DetailsNative.GetDpiForWindow(list) / 96f;
            int thickness = Math.Max(2, (int)Math.Round((expanded ? 6 : 2) * scale));
            int breadth = vertical ? bounds.Width : bounds.Height;
            int length = vertical ? bar.Bounds.Bottom - bar.Bounds.Top : bar.Bounds.Right - bar.Bounds.Left;
            DetailsNative.SelectObject(dc, ScrollbarNative.GetStockObject(18)); // DC_BRUSH
            DetailsNative.SelectObject(dc, ScrollbarNative.GetStockObject(8)); // NULL_PEN
            ScrollbarNative.SetDCBrushColor(dc, ColorTranslator.ToWin32(Theme.Muted));
            if ((bar.ThumbState & 0x8001) == 0 && bar.ThumbEnd > bar.ThumbStart)
            {
                int cross = (breadth - thickness) / 2;
                var thumbBounds = vertical
                    ? new Rectangle(bounds.Left + cross, bounds.Top + bar.ThumbStart, thickness, bar.ThumbEnd - bar.ThumbStart)
                    : new Rectangle(bounds.Left + bar.ThumbStart, bounds.Top + cross, bar.ThumbEnd - bar.ThumbStart, thickness);
                nint thumb = ScrollbarNative.CreateRoundRectRgn(thumbBounds.Left, thumbBounds.Top,
                    thumbBounds.Right + 1, thumbBounds.Bottom + 1, thickness, thickness);
                try { ScrollbarNative.FillRgn(dc, thumb, ScrollbarNative.GetStockObject(18)); }
                finally { ScrollbarNative.DeleteObject(thumb); }
            }
            if (expanded)
            {
                int cross = breadth / 2, d = Math.Max(2, (int)Math.Round(3 * scale));
                for (int side = 0; side < 2; side++)
                {
                    int along = side == 1 ? length - bar.Button / 2 : bar.Button / 2;
                    int direction = side == 1 ? 1 : -1;
                    Point Map(int a, int c) => vertical ? new(bounds.Left + c, bounds.Top + a) : new(bounds.Left + a, bounds.Top + c);
                    ScrollbarNative.Polygon(dc, new Point[]
                    {
                        Map(along + direction * d / 2, cross), Map(along - direction * d / 2, cross - d),
                        Map(along - direction * d / 2, cross + d)
                    }, 3);
                }
            }
        }
        finally
        {
            DetailsNative.RestoreDC(dc, saved);
        }
    }

    private sealed class ScrollbarVisual(ShellViewHost owner, bool vertical) : NativeWindow
    {
        private nint list;
        private ScrollbarNative.Bar bar;
        private bool expanded;
        private Rectangle bounds;

        internal void Create() => CreateHandle(new CreateParams
        {
            Caption = vertical ? "Vertical scrollbar visual" : "Horizontal scrollbar visual", Parent = owner.Handle,
            Style = 0x44000000, ExStyle = 0x08000000 // WS_CHILD | WS_CLIPSIBLINGS; WS_EX_NOACTIVATE
        });

        internal void Update(nint window, ScrollbarNative.Bar info, bool hover)
        {
            list = window;
            bar = info;
            expanded = hover;
            var screen = ScrollbarBounds(window, info, vertical);
            var origin = screen.Location;
            ScrollbarNative.ScreenToClient(owner.Handle, ref origin);
            var next = new Rectangle(origin, screen.Size);
            if (bounds != next || !DetailsNative.IsWindowVisible(Handle))
            {
                bounds = next;
                ScrollbarNative.SetWindowPos(Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x50); // NOACTIVATE | SHOWWINDOW
            }
            ScrollbarNative.InvalidateRect(Handle, 0, false);
            ScrollbarNative.UpdateWindow(Handle); // includes reentrant SB_THUMBTRACK, not queued until drag ends
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
                var paint = new ScrollbarNative.Paint();
                nint dc = print ? message.WParam : ScrollbarNative.BeginPaint(Handle, out paint);
                try { DrawScrollbar(dc, new Rectangle(Point.Empty, bounds.Size), bar, vertical, expanded, list); }
                finally { if (!print) ScrollbarNative.EndPaint(Handle, ref paint); }
                return;
            }
            base.WndProc(ref message);
        }
    }

    private static class ScrollbarNative
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
