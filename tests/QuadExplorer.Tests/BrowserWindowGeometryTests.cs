using System.Drawing;
using QuadExplorer;

internal static class BrowserWindowGeometryTests
{
    private static readonly Rectangle LargeWorkArea = new(-4000, -1000, 8000, 4000);

    public static void SavedBounds()
    {
        foreach (var dpi in new[] { 120, 144 })
        {
            var saved = new BrowserWindowState
            {
                WindowX = -2500, WindowY = -600, WindowWidth = 1943, WindowHeight = 1129,
                Maximized = true, Hidden = true, Minimized = true
            };
            var expected = new Rectangle(-2500, -600, 1943, 1129);
            for (var restore = 0; restore < 2; restore++)
            {
                var bounds = BrowserWindowGeometry.Restore(saved, LargeWorkArea, dpi, useDefaultSize: false);
                Equal(expected, bounds);
                saved = saved with
                {
                    WindowX = bounds.X, WindowY = bounds.Y,
                    WindowWidth = bounds.Width, WindowHeight = bounds.Height
                };
            }

            var centered = BrowserWindowGeometry.Restore(saved with { WindowX = null, WindowY = null },
                LargeWorkArea, dpi, useDefaultSize: false);
            Equal(new Rectangle(
                LargeWorkArea.Left + (LargeWorkArea.Width - 1943) / 2,
                LargeWorkArea.Top + (LargeWorkArea.Height - 1129) / 2, 1943, 1129), centered);

            var smallWorkArea = new Rectangle(1600, -300, 1280, 720);
            Equal(smallWorkArea, BrowserWindowGeometry.Restore(saved, smallWorkArea, dpi, useDefaultSize: false));
        }
    }

    public static void FreshDefaults()
    {
        foreach (var (dpi, expectedSize, expectedMinimum) in new[]
        {
            (120, new Size(1750, 1125), new Size(1125, 750)),
            (144, new Size(2100, 1350), new Size(1350, 900))
        })
        {
            Equal(expectedMinimum, BrowserWindowGeometry.MinimumSize(dpi));
            var bounds = BrowserWindowGeometry.Restore(new BrowserWindowState(),
                LargeWorkArea, dpi, useDefaultSize: true);
            Equal(expectedSize, bounds.Size);
            Equal(new Point(
                LargeWorkArea.Left + (LargeWorkArea.Width - expectedSize.Width) / 2,
                LargeWorkArea.Top + (LargeWorkArea.Height - expectedSize.Height) / 2), bounds.Location);
        }
    }

    public static void CascadedBounds()
    {
        foreach (var (dpi, offset) in new[] { (120, 40), (144, 48) })
        {
            var source = new Rectangle(-2500, -600, 1943, 1129);
            var cascade = BrowserWindowGeometry.Cascade(source, LargeWorkArea, dpi);
            Equal(source.Size, cascade.Size);
            Equal(new Point(source.X + offset, source.Y + offset), cascade.Location);

            var atBottomRight = new Rectangle(
                LargeWorkArea.Right - source.Width, LargeWorkArea.Bottom - source.Height,
                source.Width, source.Height);
            var wrapped = BrowserWindowGeometry.Cascade(atBottomRight, LargeWorkArea, dpi);
            Equal(source.Size, wrapped.Size);
            Equal(LargeWorkArea.Location, wrapped.Location);

            var smallWorkArea = new Rectangle(1600, -300, 1280, 720);
            Equal(smallWorkArea, BrowserWindowGeometry.Cascade(source, smallWorkArea, dpi));
        }
    }

    public static void TornOffBounds()
    {
        var work = new Rectangle(-3200, -400, 2400, 1600);
        var source = new Rectangle(100, 100, 1200, 800);
        foreach (var (sourceDpi, targetDpi, expectedSize) in new[]
        {
            (96, 96, new Size(1200, 800)), (96, 144, new Size(1800, 1200)),
            (144, 96, new Size(900, 600)), (120, 144, new Size(1440, 960))
        })
        {
            var bottomRight = BrowserWindowGeometry.TearOff(source, new Point(work.Right - 1, work.Bottom - 1),
                work, sourceDpi, targetDpi);
            Equal(expectedSize, bottomRight.Size);
            Equal(work.Right, bottomRight.Right);
            Equal(work.Bottom, bottomRight.Bottom);
            var topLeft = BrowserWindowGeometry.TearOff(source, work.Location, work, sourceDpi, targetDpi);
            Equal(work.Location, topLeft.Location);
            Equal(expectedSize, topLeft.Size);
            var tinyMonitor = new Rectangle(1920, 0, 640, 480);
            Equal(tinyMonitor, BrowserWindowGeometry.TearOff(source, tinyMonitor.Location, tinyMonitor, sourceDpi, targetDpi));
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
}
