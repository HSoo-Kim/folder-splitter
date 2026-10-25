using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunNativeDetailsThemeScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "NativeTheme")).FullName;
        for (int i = 0; i < 8; i++)
            File.WriteAllText(Path.Combine(folder, $"sample-{i:D2}.txt"), "native theme");
        Theme.Set("dark");
        var previousFont = Theme.ListFont;
        Theme.ListFont = (SystemFonts.IconTitleFont!.Name, SystemFonts.IconTitleFont.SizeInPoints);
        using var form = new Form { Text = "Native theme regression", Size = new Size(1000, 650), ShowInTaskbar = false };
        using var host = new ShellViewHost(folder) { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        var populated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void CountChanged(object? sender, EventArgs e)
        {
            if (host.ItemCount == 8) populated.TrySetResult();
        }
        host.ItemCountChanged += CountChanged;
        NativeAppearanceRedrawWatcher? redraw = null;
        Exception? failure = null;
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
                Theme.ApplyNative(host.Handle);
                host.ApplyThemeColors();
                await NativeAppearanceDispatch(host);
                await NativeAppearanceRedrawn(redraw, list);
                string[] themes = ["dark", "everforest-dark", "light", "beige", "green", "sky", "pastel", "dim",
                    "solarized", "everforest", "gruvbox", "rosepine", "latte", "nord", "mocha"];
                foreach (string theme in themes)
                {
                    Theme.Set(theme);
                    Theme.ApplyNative(host.Handle);
                    host.ApplyThemeColors();
                    foreach (bool separators in new[] { false, true })
                    foreach (bool comfortable in new[] { false, true })
                    foreach (bool highlight in new[] { false, true })
                    {
                        string name = $"{theme}-s{separators}-c{comfortable}-h{highlight}";
                        host.SelectPaths([]);
                        host.SetDetailsAppearance(separators, comfortable, highlight);
                        await NativeAppearanceDispatch(host);
                        await NativeAppearanceRedrawn(redraw, list);
                        using var normal = NativeThemeCapture(list, name + "-normal");
                        NativeThemeHeaderPixels(list, normal, name);
                        var row = new NativeRect();
                        NativeAppearanceRectMessage(list, 0x100E, 0, ref row);
                        Color column = highlight ? Theme.SortColumn : Theme.ListBack;
                        Check(NativeThemeDifference(normal.GetPixel(200, row.Bottom - 4), column) < 12
                            && NativeThemeDifference(normal.GetPixel(200, normal.Height - 20), column) < 12,
                            name + " sorted-column preference controls row and empty-area pixels");
                        string[] paths = [Path.Combine(folder, "sample-00.txt"), Path.Combine(folder, "sample-02.txt")];
                        Check(host.SelectPaths(paths) == 2, name + " selects two real Shell items");
                        NativeThemeSetFocus(list);
                        await NativeAppearanceDispatch(host);
                        using var selected = NativeThemeCapture(list, name + "-selected");
                        NativeThemeSelectionPixels(list, selected, name);
                        NativeThemeSetFocus(form.Handle);
                        await NativeAppearanceDispatch(host);
                        using var inactive = NativeThemeCapture(list, name + "-inactive");
                        NativeThemeSelectionPixels(list, inactive, name + " inactive");
                        Check(SelectedPaths(host).Order().SequenceEqual(paths.Order()), name + " painting preserves Shell selection");
                    }
                }
                Theme.Set("dark");
                Theme.ApplyNative(host.Handle);
                host.ApplyThemeColors();
                host.SetDetailsAppearance(false, true, true);
                CheckNativeCachedRows(host, list);
                nint header = SendMessage(list, 0x101F, 0, 0);
                var dateColumn = new HeaderRect();
                HeaderItemRect(header, 0x1207, 1, ref dateColumn);
                nint point = ((dateColumn.Top + dateColumn.Bottom) / 2 << 16) | ((dateColumn.Left + dateColumn.Right) / 2 & 0xFFFF);
                SendMessage(header, 0x201, 1, point);
                SendMessage(header, 0x202, 0, point);
                await NativeAppearanceDispatch(host);
                await NativeAppearanceRedrawn(redraw, list);
                Check(host.FirstSortColumn?.Property.PropertyId == 14, "custom-colored header still sorts through its native click handler");
                host.SelectPaths([]);
                using var sorted = NativeThemeCapture(list, "dark-date-sorted");
                Check(NativeThemeDifference(sorted.GetPixel(430, sorted.Height - 20), Theme.SortColumn) < 12
                    && NativeThemeDifference(sorted.GetPixel(200, sorted.Height - 20), Theme.ListBack) < 12,
                    "highlight follows the new sort column without leaving the old stripe");
                host.SelectPaths([Path.Combine(folder, "sample-00.txt"), Path.Combine(folder, "sample-02.txt")]);
                NativeThemeSetFocus(list);
                using var dateSelected = NativeThemeCapture(list, "dark-date-selected");
                NativeThemeSelectionPixels(list, dateSelected, "date sort", 200);
                Check(HasNativeDropTarget(host), "theme rendering preserves the native Shell drop target");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                redraw?.ReleaseHandle();
                host.ItemCountChanged -= CountChanged;
                form.Close();
            }
        };
        try
        {
            Application.Run(form);
            if (failure is not null) throw new InvalidOperationException("Native theme regression failed.", failure);
        }
        finally { Theme.ListFont = previousFont; }
    }

    private static void NativeThemeHeaderPixels(nint list, Bitmap image, string context)
    {
        nint header = SendMessage(list, 0x101F, 0, 0);
        NativeAppearanceGetClientRect(header, out var bounds);
        int ink = NativeThemeCount(image, new Rectangle(6, 4, 100, bounds.Bottom - 8), Theme.Text, 70);
        Check(ink > 20, context + " actual header text uses a readable theme foreground");
        Check(NativeThemeDifference(image.GetPixel(130, bounds.Bottom / 2), Theme.ListBack) < 12
            && NativeThemeDifference(image.GetPixel(image.Width - 10, bounds.Bottom / 2), Theme.ListBack) < 12,
            context + " whole header follows the palette");
    }

    private static void NativeThemeSelectionPixels(nint list, Bitmap image, string context, int normalX = 430)
    {
        foreach (int index in new[] { 0, 2 })
        {
            var row = new NativeRect();
            NativeAppearanceRectMessage(list, 0x100E, index, ref row);
            int matching = 0, total = 0;
            for (int x = 40; x < Math.Min(row.Right, image.Width) - 8; x++)
            {
                if (NativeThemeDifference(image.GetPixel(x, row.Bottom - 4), Theme.Selected) < 12) matching++;
                total++;
            }
            Console.WriteLine($"NATIVE_THEME {context} row={index} selected={matching}/{total}");
            Check(matching > total * .99, context + " selection is continuous with no per-cell borders");
            nint header = SendMessage(list, 0x101F, 0, 0);
            bool edgesMatch = true;
            for (int column = 0; column < (int)SendMessage(header, 0x1200, 0, 0); column++)
            {
                var bounds = new HeaderRect();
                HeaderItemRect(header, 0x1207, column, ref bounds);
                for (int x = Math.Max(0, bounds.Right - 2); x < Math.Min(image.Width, bounds.Right + 2); x++)
                    edgesMatch &= NativeThemeDifference(image.GetPixel(x, row.Bottom - 4), Theme.Selected) < 12;
            }
            Check(edgesMatch, context + " every column boundary keeps the selection fill");
        }
        var untouched = new NativeRect();
        NativeAppearanceRectMessage(list, 0x100E, 1, ref untouched);
        Check(NativeThemeDifference(image.GetPixel(normalX, untouched.Bottom - 4), Theme.ListBack) < 12,
            context + " unselected rows keep their normal background");
    }

    private static Bitmap NativeThemeCapture(nint window, string name)
    {
        NativeAppearanceGetClientRect(window, out var client);
        var image = new Bitmap(client.Right, client.Bottom);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Magenta);
            nint dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(window, dc, 3)) throw new InvalidOperationException("Native theme capture failed.");
            }
            finally { graphics.ReleaseHdc(dc); }
        }
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
            image.Save(Path.Combine(output, $"native-theme-{name}.png"));
        return image;
    }

    private static int NativeThemeDifference(Color a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static void CheckNativeCachedRows(ShellViewHost host, nint list)
    {
        host.SelectPaths([]);
        using var image = NativeThemeCapture(list, "cached-rows-before-prepaint");
        var row = new NativeRect();
        NativeAppearanceRectMessage(list, 0x100E, 0, ref row);
        var label = new Rectangle(24, row.Top, 150, row.Bottom - row.Top);
        int before = NativeThemeCount(image, label, Theme.Text, 70);
        Check(before > 20, "cached first row contains real native text");
        // Shell can retain already painted rows during an incremental paint. The background
        // notification must not erase those rows before their own item notifications arrive.
        using (var graphics = Graphics.FromImage(image))
        {
            nint dc = graphics.GetHdc();
            nint data = Marshal.AllocHGlobal(Marshal.SizeOf<ThemeListDraw>());
            try
            {
                var draw = new ThemeListDraw
                {
                    Draw = new ThemeItemDraw
                    {
                        Header = new ThemeNotify { Window = list, Code = -12 },
                        Stage = 1, Dc = dc,
                        Bounds = new NativeRect { Right = image.Width, Bottom = image.Height }
                    }
                };
                Marshal.StructureToPtr(draw, data, false);
                SendMessage(WheelGetParent(list), 0x004E, 0, data);
            }
            finally { Marshal.FreeHGlobal(data); graphics.ReleaseHdc(dc); }
        }
        Check(NativeThemeCount(image, label, Theme.Text, 70) == before,
            "sorted-column background repaint preserves cached file names and icons");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThemeNotify { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThemeItemDraw
    {
        public ThemeNotify Header;
        public uint Stage;
        public nint Dc;
        public NativeRect Bounds;
        public nuint Item;
        public uint State;
        public nint Data;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThemeListDraw
    {
        public ThemeItemDraw Draw;
        public int Text, TextBack, SubItem;
        public uint ItemType;
        public int Face, IconEffect, IconPhase, Part, State;
        public NativeRect TextBounds;
        public uint Align;
    }

    private static int NativeThemeCount(Bitmap image, Rectangle region, Color expected, int tolerance)
    {
        int count = 0;
        for (int y = region.Top; y < Math.Min(region.Bottom, image.Height); y++)
        for (int x = region.Left; x < Math.Min(region.Right, image.Width); x++)
            if (NativeThemeDifference(image.GetPixel(x, y), expected) < tolerance) count++;
        return count;
    }

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    private static extern nint NativeThemeSetFocus(nint window);
}
