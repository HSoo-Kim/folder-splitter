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
                    Theme.Set(theme);
                    Theme.ApplyNative(host.Handle);
                    host.ApplyThemeColors();
                    host.SetDetailsAppearance(separators, false);
                    await NativeAppearanceDispatch(host);
                    ScrollCapture(list, $"{theme}-{separators}");
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
            bool nativeBar = ScrollPointVisible(dc, bar.Bounds.Left - window.Left + bar.LineButton + 4,
                (bar.Bounds.Top + bar.Bounds.Bottom) / 2 - window.Top);
            var vertical = ScrollInfo(list, -5);
            bool verticalBar = ScrollPointVisible(dc, (vertical.Bounds.Left + vertical.Bounds.Right) / 2 - window.Left,
                vertical.Bounds.Top - window.Top + vertical.LineButton + 4);
            Console.WriteLine($"SCROLL_CLIP client={client} nativeBar={nativeBar}");
            Check(client, "native clipping probe has a drawable client region");
            Check(nativeBar != covered, "horizontal visual controls native scrollbar paint visibility");
            Check(verticalBar, "horizontal visual does not clip native vertical scrollbar");
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

    private static void ScrollCapture(nint list, string name, bool expanded = false)
    {
        ScrollWindowRect(list, out var window);
        using var image = new Bitmap(window.Right - window.Left, window.Bottom - window.Top);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(Color.Magenta);
            nint dc = g.GetHdc();
            try { Check(PrintWindow(list, dc, 2), name + " full native list capture"); }
            finally { g.ReleaseHdc(dc); }
        }
        bool covered = !name.EndsWith("True", StringComparison.Ordinal);
        ScrollCheckNativeClip(list, covered);
        if (covered)
        {
            var host = (ShellViewHost)Control.FromChildHandle(list)!;
            var visual = (NativeWindow?)typeof(ShellViewHost)
                .GetField("horizontalScrollbar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(host);
            Check(visual is { Handle: not 0 } && DemoIsVisible(visual.Handle), name + " horizontal visual is visible");
            var bar = ScrollInfo(list, -6);
            ScrollWindowRect(visual!.Handle, out var visualBounds);
            Check(visualBounds.Left == bar.Bounds.Left && visualBounds.Right == bar.Bounds.Right &&
                visualBounds.Top == bar.Bounds.Top && visualBounds.Bottom == bar.Bounds.Bottom,
                name + " visual covers only the horizontal native gutter");
            using var actual = new Bitmap(bar.Bounds.Right - bar.Bounds.Left, bar.Bounds.Bottom - bar.Bounds.Top);
            using (var g = Graphics.FromImage(actual))
            {
                nint dc = g.GetHdc();
                try { Check(PrintWindow(visual.Handle, dc, 1), name + " separate visual capture"); }
                finally { g.ReleaseHdc(dc); }
            }
            bool same = true;
            for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
                same &= actual.GetPixel(x, y) == image.GetPixel(bar.Bounds.Left - window.Left + x, bar.Bounds.Top - window.Top + y);
            Check(same, name + " real visual and list print both show the current theme and thumb");
            Check(SendMessage(visual.Handle, 0x0084, 0, 0) == -1, name + " visual leaves native pointer hit testing intact");
            Check(ScrollWindowThread(visual.Handle, out _) == ScrollWindowThread(list, out _),
                name + " pointer-transparent visual and native list share a UI thread");
            if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } captureOutput)
                actual.Save(Path.Combine(captureOutput, "visual-" + name + ".png"));
        }
        using (var g = Graphics.FromImage(image))
        {
            nint dc = g.GetHdc();
            try { SendMessage(list, 0x0317, dc, 0x1E); }
            finally { g.ReleaseHdc(dc); }
        }
        foreach (int axis in new[] { -6, -5 })
        {
            var info = new ScrollBarInfo { Size = Marshal.SizeOf<ScrollBarInfo>() };
            Check(ScrollGetInfo(list, axis, ref info) && (info.State & 0x8000) == 0,
                name + " scrollbar visible " + axis);
            Console.WriteLine($"SCROLL {name} axis={axis} bounds={info.Bounds.Left-window.Left},{info.Bounds.Top-window.Top},{info.Bounds.Right-window.Left},{info.Bounds.Bottom-window.Top} thumb={info.ThumbTop}..{info.ThumbBottom}");
            if (axis == -6 && !name.EndsWith("True", StringComparison.Ordinal))
            {
                int x = info.Bounds.Left - window.Left + (info.ThumbTop + info.ThumbBottom) / 2;
                int top = info.Bounds.Top - window.Top, bottom = info.Bounds.Bottom - window.Top;
                double ink = 0;
                for (int y = top; y < bottom; y++)
                    ink += NativeThemeDifference(image.GetPixel(x, y), Theme.ListBack) /
                        (double)NativeThemeDifference(Theme.Muted, Theme.ListBack);
                Console.WriteLine($"SCROLL_INK {name} thickness={ink:F2}");
                double scale = NativeAppearanceDpi(list) / 96d;
                Check(expanded ? ink >= 5 * scale && ink <= 7 * scale : ink >= 1.5 && ink <= Math.Ceiling(3 * scale),
                    name + " horizontal thumb has expected theme-colored thickness");
                Check(NativeThemeDifference(image.GetPixel(x, top + 1), Theme.ListBack) < 12,
                    name + " horizontal track matches file background");
            }
        }
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
        {
            var bar = ScrollInfo(list, -6);
            var region = new Rectangle(0, bar.Bounds.Top - window.Top, image.Width, image.Height - (bar.Bounds.Top - window.Top));
            using var strip = image.Clone(region, image.PixelFormat);
            strip.Save(Path.Combine(output, "scroll-" + name + ".png"));
        }
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
