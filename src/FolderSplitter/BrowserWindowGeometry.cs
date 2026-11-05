using System.Drawing;

namespace QuadExplorer;

// Session bounds are physical pixels; only unsaved defaults and UI minimums are DPI-scaled.
internal static class BrowserWindowGeometry
{
    internal static Size MinimumSize(int dpi) => new(
        (int)Math.Ceiling(900 * dpi / 96d), (int)Math.Ceiling(600 * dpi / 96d));

    internal static Rectangle Restore(BrowserWindowState state, Rectangle work, int dpi, bool useDefaultSize)
    {
        double scale = useDefaultSize ? dpi / 96d : 1;
        int width = Math.Min((int)Math.Round(state.WindowWidth * scale), work.Width);
        int height = Math.Min((int)Math.Round(state.WindowHeight * scale), work.Height);
        int x = state.WindowX ?? work.Left + (work.Width - width) / 2;
        int y = state.WindowY ?? work.Top + (work.Height - height) / 2;
        return new Rectangle(Math.Clamp(x, work.Left, work.Right - width),
            Math.Clamp(y, work.Top, work.Bottom - height), width, height);
    }

    internal static Rectangle Cascade(Rectangle source, Rectangle work, int dpi)
    {
        int offset = (int)Math.Round(32 * dpi / 96d);
        int width = Math.Min(source.Width, work.Width), height = Math.Min(source.Height, work.Height);
        int x = source.Left + offset, y = source.Top + offset;
        if (x + width > work.Right) x = work.Left;
        if (y + height > work.Bottom) y = work.Top;
        return new Rectangle(Math.Max(work.Left, x), Math.Max(work.Top, y), width, height);
    }
}
