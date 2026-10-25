using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunNativeScrollbarScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "Scrollbars")).FullName;
        for (int i = 0; i < 100; i++)
            File.WriteAllText(Path.Combine(folder, $"sample-{i:D3}.txt"), "scroll");
        using var form = new Form { Size = new Size(620, 400), ShowInTaskbar = false };
        using var host = new ShellViewHost(folder) { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        var populated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ItemCountChanged += (_, _) => { if (host.ItemCount == 100) populated.TrySetResult(); };
        Exception? failure = null;
        NativeAppearanceRedrawWatcher? redraw = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                nint list = NativeAppearanceList(host);
                redraw = new NativeAppearanceRedrawWatcher();
                redraw.AssignHandle(list);
                await populated.Task.WaitAsync(TimeSpan.FromSeconds(20));
                host.SetViewModeAndSize(4, 16);
                await NativeAppearanceRedrawn(redraw, list);
                SendMessage(list, 0x101E, 0, 850); // LVM_SETCOLUMNWIDTH: force horizontal overflow
                foreach (string theme in new[] { "dim", "light", "dark", "beige", "green", "sky", "pastel",
                    "solarized", "everforest", "everforest-dark", "gruvbox", "rosepine", "latte", "nord", "mocha" })
                foreach (bool separators in new[] { false, true })
                {
                    await NativeAppearanceIdle(() =>
                    {
                        Theme.Set(theme);
                        Theme.ApplyNative(host.Handle);
                        host.ApplyThemeColors();
                        host.SetDetailsAppearance(separators, false);
                    });
                    Check((ScrollInfo(list, -5).State & 0x18000) == 0 && (ScrollInfo(list, -6).State & 0x18000) == 0,
                        theme + " overflow fixture has both native scrollbars");
                    ScrollCapture(list, $"{theme}-{separators}");
                    var vertical = ScrollInfo(list, -5);
                    int vx = (vertical.Bounds.Left + vertical.Bounds.Right) / 2;
                    int vy = vertical.Bounds.Top + (vertical.ThumbTop + vertical.ThumbBottom) / 2;
                    SendMessage(list, 0x00A0, 7, (nint)((vy << 16) | (vx & 0xFFFF)));
                    ScrollCapture(list, $"vertical-hover-{theme}-{separators}", verticalExpanded: true);
                    SendMessage(list, 0x02A2, 0, 0);
                    ScrollCapture(list, $"vertical-leave-{theme}-{separators}");
                    int verticalBefore = ScrollInfo(list, -5).ThumbTop;
                    SendMessage(list, 0x0115, 1, 0); // SB_LINEDOWN
                    Check(ScrollInfo(list, -5).ThumbTop > verticalBefore, theme + " native vertical line scroll advances");
                    SendMessage(list, 0x0115, 6, 0);
                    NativeThemeSetFocus(list);
                    var wheelPoint = new ShortcutPoint { X = 100, Y = 100 };
                    ShortcutToScreen(list, ref wheelPoint);
                    SendMessage(list, 0x020A, (nint)(-120 << 16),
                        (nint)((wheelPoint.Y << 16) | (wheelPoint.X & 0xFFFF))); // no global input
                    await NativeAppearanceDispatch(host);
                    if (SystemInformation.MouseWheelScrollLines != 0)
                        Check((int)SendMessage(list, 0x1027, 0, 0) > 0, theme + " native wheel moves actual rows");
                    ScrollCapture(list, $"vertical-wheel-{theme}-{separators}");
                    SendMessage(list, 0x0115, 7, 0); // SB_BOTTOM
                    ScrollCapture(list, $"vertical-end-{theme}-{separators}");
                    SendMessage(list, 0x0115, 6, 0);
                    if (separators) continue;
                    var bar = ScrollInfo(list, -6);
                    int x = bar.Bounds.Left + (bar.ThumbTop + bar.ThumbBottom) / 2;
                    int y = (bar.Bounds.Top + bar.Bounds.Bottom) / 2;
                    SendMessage(list, 0x00A0, 6, (nint)((y << 16) | (x & 0xFFFF)));
                    ScrollCapture(list, $"{theme}-hover", true);
                    SendMessage(list, 0x02A2, 0, 0);
                    ScrollCapture(list, $"{theme}-leave");
                    int before = ScrollInfo(list, -6).ThumbTop;
                    SendMessage(list, 0x0114, 1, 0); // SB_LINERIGHT
                    Check(ScrollInfo(list, -6).ThumbTop > before, theme + " native horizontal line scroll advances");
                    SendMessage(list, 0x0114, 7, 0); // SB_RIGHT
                    ScrollCapture(list, $"{theme}-end");
                    SendMessage(list, 0x0114, 6, 0); // SB_LEFT
                    SendMessage(list, 0x0115, 1, 0); // SB_LINEDOWN
                    Check((int)SendMessage(list, 0x1027, 0, 0) > 0, theme + " native vertical scrolling remains functional");
                    SendMessage(list, 0x0115, 6, 0); // SB_TOP
                }
                Check(HasNativeDropTarget(host), "horizontal skin preserves Shell drop registration");
                Theme.Set("dim");
                Theme.ApplyNative(host.Handle);
                host.ApplyThemeColors();
                host.SetDetailsAppearance(false, false);
                foreach (int width in new[] { 420, 900 })
                {
                    await NativeAppearanceIdle(() => form.Width = width);
                    ScrollCapture(list, $"resize-{width}");
                }
                await NativeAppearanceIdle(() => form.Width = 2000);
                Check((ScrollInfo(list, -6).State & 0x18000) != 0, "horizontal bar disappears without overflow");
                await NativeAppearanceIdle(() => form.Width = 620);
                ScrollCapture(list, "overflow-restored");
                await NativeAppearanceIdle(() => host.SetViewModeAndSize(1, 48));
                await NativeAppearanceIdle(() => host.SetViewModeAndSize(4, 16));
                list = NativeAppearanceList(host);
                await NativeAppearanceIdle(() => SendMessage(list, 0x101E, 0, 850));
                ScrollCapture(list, "view-restored");
                host.SetDetailsAppearance(false, true);
                await NativeAppearanceDispatch(host);
                ScrollCapture(list, "comfortable");
                await NativeAppearanceIdle(() => form.Width = 2000);
                ScrollCapture(list, "vertical-only");
                string few = Directory.CreateDirectory(Path.Combine(root, "FewScrollbars")).FullName;
                File.WriteAllText(Path.Combine(few, "one.txt"), "scroll");
                File.WriteAllText(Path.Combine(few, "two.txt"), "scroll");
                await NativeAppearancePopulated(host, () => host.Navigate(few), 2);
                list = NativeAppearanceList(host);
                await NativeAppearanceIdle(() => SendMessage(list, 0x101E, 0, 850));
                Check((ScrollInfo(list, -5).State & 0x18000) != 0 && (ScrollInfo(list, -6).State & 0x18000) != 0,
                    "few items in wide window need neither scrollbar");
                ScrollCapture(list, "no-overflow");
                await NativeAppearanceIdle(() => form.Width = 620);
                Check((ScrollInfo(list, -5).State & 0x18000) != 0 && (ScrollInfo(list, -6).State & 0x18000) == 0,
                    "few items in narrow window need only horizontal scrollbar");
                ScrollCapture(list, "horizontal-only");
                await NativeAppearancePopulated(host, () => host.Navigate(folder), 100);
                list = NativeAppearanceList(host);
                await NativeAppearanceIdle(() => SendMessage(list, 0x101E, 0, 850));
                ScrollCapture(list, "both-restored");
                string empty = Directory.CreateDirectory(Path.Combine(root, "EmptyScrollbars")).FullName;
                await NativeAppearancePopulated(host, () => host.Navigate(empty), 0);
                ScrollCapture(NativeAppearanceList(host), "empty-folder");
                await NativeAppearancePopulated(host, () => host.Navigate(folder), 100);
                list = NativeAppearanceList(host);
                await NativeAppearanceIdle(() => SendMessage(list, 0x101E, 0, 850));
                ScrollCapture(list, "navigation-restored");
                ScrollCheckNativeClip(list);
            }
            catch (Exception ex) { failure = ex; }
            finally { redraw?.ReleaseHandle(); form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Scrollbar regression failed.", failure);
    }

    private static void ScrollCheckNativeClip(nint list, bool covered = true)
    {
        var bar = ScrollInfo(list, -6);
        ScrollWindowRect(list, out var window);
        nint dc = ScrollGetWindowDC(list);
        try
        {
            bool client = ScrollPointVisible(dc, 100, 100);
            var vertical = ScrollInfo(list, -5);
            Check(client, "native clipping probe has a drawable client region");
            bool hasHorizontal = (bar.State & 0x18000) == 0, hasVertical = (vertical.State & 0x18000) == 0;
            if (hasHorizontal)
            {
                bool nativeBar = ScrollPointVisible(dc, bar.Bounds.Left - window.Left + bar.LineButton + 4,
                    (bar.Bounds.Top + bar.Bounds.Bottom) / 2 - window.Top);
                Check(nativeBar != covered, "horizontal visual controls native scrollbar paint visibility");
            }
            if (hasVertical)
            {
                bool nativeBar = ScrollPointVisible(dc, (vertical.Bounds.Left + vertical.Bounds.Right) / 2 - window.Left,
                    vertical.Bounds.Top - window.Top + vertical.LineButton + 4);
                Check(!nativeBar, "vertical visual blocks classic native scrollbar paints");
            }
            if (hasHorizontal && hasVertical)
                Check(!ScrollPointVisible(dc, (vertical.Bounds.Left + vertical.Bounds.Right) / 2 - window.Left,
                    (bar.Bounds.Top + bar.Bounds.Bottom) / 2 - window.Top), "shared scrollbar corner blocks classic native paints");
            Console.WriteLine($"SCROLL_CLIP client={client} horizontal={hasHorizontal} vertical={hasVertical}");
        }
        finally { ScrollReleaseDC(list, dc); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowDC")]
    private static extern nint ScrollGetWindowDC(nint window);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    private static extern int ScrollReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", EntryPoint = "PtVisible")]
    private static extern bool ScrollPointVisible(nint dc, int x, int y);

    private static ScrollBarInfo ScrollInfo(nint list, int axis)
    {
        var info = new ScrollBarInfo { Size = Marshal.SizeOf<ScrollBarInfo>() };
        if (!ScrollGetInfo(list, axis, ref info)) throw new InvalidOperationException("Missing native scrollbar.");
        return info;
    }

    private static void ScrollCapture(nint list, string name, bool expanded = false, bool verticalExpanded = false)
    {
        var host = (ShellViewHost)Control.FromChildHandle(list)!;
        // Capture each visual before printing the list: list WM_PRINT refreshes its visuals
        // and must not conceal a stale thumb after wheel/keyboard/native scroll input.
        using var horizontalImage = ScrollVisualImage(host, false, name);
        using var verticalImage = ScrollVisualImage(host, true, name);
        var horizontalBeforePrint = ScrollInfo(list, -6);
        var verticalBeforePrint = ScrollInfo(list, -5);
        ScrollWindowRect(list, out var window);
        using var image = new Bitmap(window.Right - window.Left, window.Bottom - window.Top);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(Color.Magenta);
            nint dc = g.GetHdc();
            try { Check(PrintWindow(list, dc, 2), name + " full native list capture"); }
            finally { g.ReleaseHdc(dc); }
        }
        // PrintWindow alone does not always run the non-client skin; the explicit WM_PRINT does.
        using (var g = Graphics.FromImage(image))
        {
            nint dc = g.GetHdc();
            try { SendMessage(list, 0x0317, dc, 0x1E); }
            finally { g.ReleaseHdc(dc); }
        }
        bool covered = !name.EndsWith("True", StringComparison.Ordinal);
        ScrollCheckNativeClip(list, covered);
        foreach (int axis in new[] { -6, -5 })
        {
            bool vertical = axis == -5;
            var visual = (NativeWindow?)typeof(ShellViewHost)
                .GetField(vertical ? "verticalScrollbar" : "horizontalScrollbar",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(host);
            var bar = ScrollInfo(list, axis);
            bool visible = (bar.State & 0x18000) == 0;
            string context = $"{name} axis={axis}";
            if (!visible || !vertical && !covered)
            {
                Check(visual is null || !DemoIsVisible(visual.Handle), context + " unused visual is hidden");
                continue;
            }
            Check(visual is { Handle: not 0 } && DemoIsVisible(visual.Handle), context + " visual is visible");
            ScrollWindowRect(visual!.Handle, out var visualBounds);
            var horizontal = ScrollInfo(list, -6);
            int bottom = vertical && (horizontal.State & 0x18000) == 0
                ? Math.Max(bar.Bounds.Bottom, horizontal.Bounds.Bottom) : bar.Bounds.Bottom;
            Check(visualBounds.Left == bar.Bounds.Left && visualBounds.Right == bar.Bounds.Right &&
                visualBounds.Top == bar.Bounds.Top && visualBounds.Bottom == bottom,
                context + " visual covers its native gutter and shared corner only");
            var actual = (vertical ? verticalImage : horizontalImage)
                ?? throw new InvalidOperationException(context + " visual is missing before list print can refresh it");
            Check(actual.Width == visualBounds.Right - visualBounds.Left && actual.Height == visualBounds.Bottom - visualBounds.Top,
                context + " live visual geometry is current before printing");
            bool same = true;
            for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
                same &= actual.GetPixel(x, y) == image.GetPixel(bar.Bounds.Left - window.Left + x, bar.Bounds.Top - window.Top + y);
            if (!same && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } failureOutput)
            {
                var before = vertical ? verticalBeforePrint : horizontalBeforePrint;
                Console.WriteLine($"SCROLL_MISMATCH {context} view={SendMessage(list, 0x108F, 0, 0)} before={before.ThumbTop}..{before.ThumbBottom} state={before.Thumb:X} after={bar.ThumbTop}..{bar.ThumbBottom} state={bar.Thumb:X}");
                actual.Save(Path.Combine(failureOutput, $"mismatch-{name}-{axis}-actual.png"));
                using var expected = image.Clone(new Rectangle(bar.Bounds.Left - window.Left, bar.Bounds.Top - window.Top,
                    actual.Width, actual.Height), image.PixelFormat);
                expected.Save(Path.Combine(failureOutput, $"mismatch-{name}-{axis}-print.png"));
            }
            Check(same, context + " real visual and list print both show the current theme and thumb");
            Check(SendMessage(visual.Handle, 0x0084, 0, 0) == -1, context + " visual leaves native pointer hit testing intact");
            Check(ScrollWindowThread(visual.Handle, out _) == ScrollWindowThread(list, out _),
                context + " pointer-transparent visual and native list share a UI thread");
            int along = (bar.ThumbTop + bar.ThumbBottom) / 2;
            int breadth = vertical ? actual.Width : actual.Height;
            double ink = 0;
            for (int cross = 0; cross < breadth; cross++)
            {
                var pixel = vertical ? actual.GetPixel(cross, along) : actual.GetPixel(along, cross);
                ink += NativeThemeDifference(pixel, Theme.ListBack) / (double)NativeThemeDifference(Theme.Muted, Theme.ListBack);
            }
            double scale = NativeAppearanceDpi(list) / 96d;
            bool hover = vertical ? verticalExpanded : expanded;
            Check(hover ? ink >= 5 * scale && ink <= 7 * scale : ink >= 1.5 && ink <= Math.Ceiling(3 * scale),
                context + " thumb has expected theme-colored thickness");
            Color track = vertical ? actual.GetPixel(1, along) : actual.GetPixel(along, 1);
            Check(NativeThemeDifference(track, Theme.ListBack) < 12, context + " track blends with list background");
            if (vertical && bottom > bar.Bounds.Bottom)
                Check(NativeThemeDifference(actual.GetPixel(actual.Width / 2, actual.Height - 3), Theme.ListBack) < 12,
                    context + " shared corner has no classic gray block");
            if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } captureOutput)
                actual.Save(Path.Combine(captureOutput, $"visual-{name}-{axis}.png"));
        }
    }

    private static Bitmap? ScrollVisualImage(ShellViewHost host, bool vertical, string name)
    {
        var visual = (NativeWindow?)typeof(ShellViewHost)
            .GetField(vertical ? "verticalScrollbar" : "horizontalScrollbar",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host);
        if (visual is null || !DemoIsVisible(visual.Handle)) return null;
        ScrollWindowRect(visual.Handle, out var bounds);
        var image = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = Graphics.FromImage(image);
        nint dc = graphics.GetHdc();
        try { Check(PrintWindow(visual.Handle, dc, 1), $"{name} vertical={vertical} live visual capture"); }
        finally { graphics.ReleaseHdc(dc); }
        return image;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollBarInfo
    {
        public int Size;
        public NativeRect Bounds;
        public int LineButton, ThumbTop, ThumbBottom, Reserved;
        public uint State, First, PageFirst, Thumb, PageLast, Last;
    }

    [DllImport("user32.dll", EntryPoint = "GetScrollBarInfo")]
    private static extern bool ScrollGetInfo(nint window, int axis, ref ScrollBarInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool ScrollWindowRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint ScrollWindowThread(nint window, out uint process);
}
