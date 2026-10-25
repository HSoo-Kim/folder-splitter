using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    internal static void RunNativeDetailsAppearanceScenario(string root, string[] folders)
    {
        string? desktop = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_DESKTOP");
        var actual = new System.Text.StringBuilder(256);
        if (desktop is null || !desktop.StartsWith("FolderSplitter-QA-", StringComparison.Ordinal) ||
            !GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, actual, 512, out _) ||
            actual.ToString() != desktop)
            throw new InvalidOperationException("Native appearance checks require tools/PrivateDesktopQa.");

        string folder = Directory.CreateDirectory(Path.Combine(root, "NativeDetails")).FullName;
        for (int i = 0; i < 8; i++)
            File.WriteAllText(Path.Combine(folder, $"item-{i:D2}.txt"), "native appearance");
        var previousFont = Theme.ListFont;
        Theme.Set("dark");
        Theme.ListFont = (SystemFonts.IconTitleFont!.Name, SystemFonts.IconTitleFont.SizeInPoints);
        using var form = new Form { Text = "Native details appearance", Size = new Size(1000, 650), ShowInTaskbar = false };
        using var host = new ShellViewHost(folder);
        host.SetDetailsAppearance(true, false);
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        var enumerated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void CountChanged(object? sender, EventArgs e)
        {
            if (host.ItemCount == 8) enumerated.TrySetResult();
        }
        host.ItemCountChanged += CountChanged;
        Exception? failure = null;
        NativeAppearanceRedrawWatcher? redrawWatcher = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                redrawWatcher = new NativeAppearanceRedrawWatcher();
                redrawWatcher.AssignHandle(NativeAppearanceList(host));
                await enumerated.Task.WaitAsync(TimeSpan.FromSeconds(20));
                host.SetViewModeAndSize(4, 16);
                Theme.ApplyNative(host.Handle);
                host.ApplyThemeColors();
                await NativeAppearanceDispatch(host);
                nint list = NativeAppearanceList(host);
                int compact = NativeAppearanceRowHeight(list);
                nint smallImages = SendMessage(list, 0x1002, 1, 0);
                Check(NativeAppearanceImageSize(smallImages, out int iconWidth, out int iconHeight), "native file icon dimensions are measurable");
                var originalView = host.ViewModeAndSize;
                nint stateImages = SendMessage(list, 0x1002, 2, 0);
                uint dpi = NativeAppearanceDpi(list);
                Console.WriteLine($"NATIVE_DETAILS compact={compact} dpi={dpi} small=0x{smallImages:X}");
                Check(compact > 0, "native details reports actual item bounds");
                await NativeAppearanceRedrawn(redrawWatcher, list);
                CheckNativeAppearanceHeader(list, "initial dark header");
                CheckNativeAppearanceBody(host, list, "initial-with-separators", true);
                // Compare against compact rows drawn the same way: no separators or grid lines in either capture.
                host.SetDetailsAppearance(false, false);
                await NativeAppearanceRedrawn(redrawWatcher, list);
                var compactIcon = NativeAppearanceInk(list, 1, "compact-icon"); // LVIR_ICON
                var compactText = NativeAppearanceInk(list, 2, "compact-label"); // LVIR_LABEL

                host.SetDetailsAppearance(false, true);
                int comfortable = NativeAppearanceRowHeight(list);
                int target = (int)Math.Round(28d * dpi / 96);
                Console.WriteLine($"NATIVE_DETAILS comfortable={comfortable} target={target}");
                await NativeAppearanceRedrawn(redrawWatcher, list);
                var spacedIcon = NativeAppearanceInk(list, 1, "comfortable-icon");
                var spacedText = NativeAppearanceInk(list, 2, "comfortable-label");
                Check(!compactIcon.IsEmpty && Math.Abs(spacedIcon.Width - compactIcon.Width) <= 1
                    && Math.Abs(spacedIcon.Height - compactIcon.Height) <= 1,
                    "comfortable rows draw the file icon at its compact pixel size, not stretched");
                Check(!compactText.IsEmpty && Math.Abs(spacedText.Height - compactText.Height) <= 1,
                    "comfortable rows draw labels with the normal list font");
                Check(Math.Abs(comfortable - target) <= 1 && comfortable > compact,
                    "comfortable rows measure 28 DIP independently of the list font");
                Check(SendMessage(list, 0x1002, 1, 0) == smallImages,
                    "row spacing leaves the native small/system image list unchanged");
                Check(NativeAppearanceImageSize(smallImages, out int spacedIconWidth, out int spacedIconHeight)
                    && spacedIconWidth == iconWidth && spacedIconHeight == iconHeight && host.ViewModeAndSize == originalView,
                    "comfortable row spacing preserves actual icon dimensions and Shell icon size");
                CheckNativeAppearanceHeader(list, "header stays dark without body separators");
                CheckNativeAppearanceBody(host, list, "without-separators", false);

                // A theme association reset, not merely a resize: Shell does this to the header
                // during maximization and native view resets. The subclass must restore it itself.
                nint header = SendMessage(list, 0x101F, 0, 0);
                Marshal.ThrowExceptionForHR(NativeAppearanceSetTheme(header, "ItemsView", null));
                CheckNativeAppearanceHeader(list, "dark header survives an actual light-theme reset");
                Marshal.ThrowExceptionForHR(NativeAppearanceSetTheme(list, "ItemsView", null));
                CheckNativeAppearanceHeader(list, "dark header survives a body-theme reset");
                Check(NativeAppearanceRowHeight(list) == comfortable, "theme reset retains comfortable row spacing");
                CheckNativeAppearanceBody(host, list, "after-reset", false);

                await NativeAppearanceIdle(() => form.WindowState = FormWindowState.Maximized);
                CheckNativeAppearanceHeader(NativeAppearanceList(host), "dark header survives maximize");
                await NativeAppearanceIdle(() => form.WindowState = FormWindowState.Normal);
                CheckNativeAppearanceHeader(NativeAppearanceList(host), "dark header survives restore");

                host.SetDetailsAppearance(true, false);
                int restoredHeight = NativeAppearanceRowHeight(list);
                Console.WriteLine($"NATIVE_COMPACT restored={restoredHeight} original={compact} state=0x{SendMessage(list, 0x1002, 2, 0):X} originalState=0x{stateImages:X}");
                Check(restoredHeight == compact &&
                    SendMessage(list, 0x1002, 2, 0) == stateImages,
                    "compact rows restore original height and native state image list");
                CheckNativeAppearanceBody(host, list, "with-separators", true);
                host.SetDetailsAppearance(false, true);
                Theme.ListFont = ("Segoe UI", 11f);
                host.ApplyListFont();
                await NativeAppearanceDispatch(host);
                Check(Math.Abs(NativeAppearanceRowHeight(list) - target) <= 1,
                    "comfortable row spacing remains 28 DIP after changing the list font");
                Theme.ListFont = null;
                host.ApplyListFont();
                await NativeAppearanceDispatch(host);
                await NativeAppearanceIdle(() =>
                {
                    host.SetViewModeAndSize(1, 48);
                    host.SetViewModeAndSize(4, 16);
                });
                list = NativeAppearanceList(host);
                await NativeAppearanceRedrawn(redrawWatcher, list);
                Check(Math.Abs(NativeAppearanceRowHeight(list) - target) <= 1,
                    "comfortable rows survive a native view-mode round trip");
                CheckNativeAppearanceHeader(list, "dark header survives a native view-mode round trip");
                await NativeAppearanceIdle(() => host.SetViewModeAndSize(1, 48));
                list = NativeAppearanceList(host);
                Check(!DemoIsVisible(SendMessage(list, 0x101F, 0, 0)), "the column header is hidden in icon view");
                await NativeAppearanceIdle(() => host.SetViewModeAndSize(4, 16));
                list = NativeAppearanceList(host);
                Check(DemoIsVisible(SendMessage(list, 0x101F, 0, 0)), "the column header is shown again in Details view");

                await Navigate(host, () => host.Navigate(folders[1]));
                await NativeAppearancePopulated(host, () => host.Navigate(folder), 8);
                await NativeAppearanceDispatch(host);
                list = NativeAppearanceList(host);
                Check(Math.Abs(NativeAppearanceRowHeight(list) - target) <= 1,
                    "new native list after navigation inherits comfortable rows");
                CheckNativeAppearanceHeader(list, "new native header after navigation stays dark");
                Check(HasNativeDropTarget(host), "native appearance preserves Shell drag and drop registration");
                string item = Path.Combine(folder, "item-00.txt");
                Check(host.SelectPaths([item]) == 1 && SelectedPaths(host).SequenceEqual([item]),
                    "native appearance preserves real file selection");
                await NativeAppearancePopulated(host, () => typeof(Control)
                    .GetMethod("RecreateHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(host, null), 8);
                host.SetViewModeAndSize(4, 16);
                await NativeAppearanceDispatch(host);
                list = NativeAppearanceList(host);
                Check(Math.Abs(NativeAppearanceRowHeight(list) - target) <= 1,
                    "comfortable rows survive actual managed/native handle recreation");
                CheckNativeAppearanceHeader(list, "dark header survives actual native handle recreation");
                SendMessage(host.Handle, 0x02E3, 0, 0); // WM_DPICHANGED_AFTERPARENT
                await NativeAppearanceDispatch(host);
                Check(Math.Abs(NativeAppearanceRowHeight(list) - target) <= 1,
                    "native DPI notification retains comfortable row spacing");
                CheckNativeAppearanceHeader(list, "dark header survives native DPI notification");
                await NativeAppearanceIdle(() =>
                {
                    Theme.Set("light");
                    Theme.ApplyNative(host.Handle);
                    host.ApplyThemeColors();
                    Theme.Set("dark");
                    Theme.ApplyNative(host.Handle);
                    host.ApplyThemeColors();
                });
                CheckNativeAppearanceHeader(list, "dark header survives light/dark theme round trip");
                CheckNativeAppearanceBody(host, list, "after-theme-roundtrip", false);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                redrawWatcher?.ReleaseHandle();
                host.ItemCountChanged -= CountChanged;
                form.Close();
            }
        };
        try
        {
            Application.Run(form);
            if (failure is not null) throw new InvalidOperationException("Native details appearance scenario failed.", failure);
        }
        finally { Theme.ListFont = previousFont; }
    }

    private static async Task NativeAppearancePopulated(ShellViewHost host, Action trigger, int count)
    {
        var populated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e)
        {
            if (host.ItemCount == count) populated.TrySetResult();
        }
        host.ItemCountChanged += Changed;
        try
        {
            await Navigate(host, trigger);
            var redraw = new NativeAppearanceRedrawWatcher();
            redraw.AssignHandle(NativeAppearanceList(host));
            try
            {
                await populated.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await NativeAppearanceDispatch(host);
                await NativeAppearanceRedrawn(redraw, redraw.Handle);
            }
            finally { redraw.ReleaseHandle(); }
        }
        finally { host.ItemCountChanged -= Changed; }
    }

    private static async Task NativeAppearanceIdle(Action trigger)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Idle(object? sender, EventArgs e) => done.TrySetResult();
        Application.Idle += Idle;
        try
        {
            trigger();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { Application.Idle -= Idle; }
    }

    // This is an exact posted UI dispatch barrier: no elapsed-time settling or polling.
    private static Task NativeAppearanceDispatch(Control control)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.BeginInvoke(() => done.TrySetResult());
        return done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static nint NativeAppearanceList(ShellViewHost host)
    {
        nint list = 0;
        EnumChildWindows(host.Handle, (window, _) =>
        {
            var name = new System.Text.StringBuilder(32);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "SysListView32") list = window;
            return list == 0;
        }, 0);
        if (list == 0) throw new InvalidOperationException("No native details list.");
        return list;
    }

    private static int NativeAppearanceRowHeight(nint list)
    {
        var bounds = new NativeRect(); // LVIR_BOUNDS
        if (NativeAppearanceRectMessage(list, 0x100E, 0, ref bounds) == 0)
            throw new InvalidOperationException("Native details has no measurable item.");
        return bounds.Bottom - bounds.Top;
    }

    // DefView disables list redraw while it populates (WM_SETREDRAW FALSE, handled by the list itself);
    // the list then paints nothing, not even into WM_PRINT. Wait until the Shell re-enables drawing.
    private static async Task NativeAppearanceRedrawn(NativeAppearanceRedrawWatcher? watcher, nint list)
    {
        if (watcher is null || watcher.Handle != list) throw new InvalidOperationException("Redraw watcher is not on the current list.");
        Console.WriteLine($"NATIVE_REDRAW seen={watcher.Seen} enabled={watcher.Enabled.Task.IsCompleted}");
        await watcher.Enabled.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class NativeAppearanceRedrawWatcher : NativeWindow
    {
        public TaskCompletionSource Enabled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Seen { get; private set; } = "";

        public NativeAppearanceRedrawWatcher() => Enabled.TrySetResult();

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x000B) // WM_SETREDRAW
            {
                Seen += message.WParam != 0 ? "1" : "0";
                if (message.WParam != 0) Enabled.TrySetResult();
                else if (Enabled.Task.IsCompleted) Enabled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            base.WndProc(ref message);
        }
    }

    // Bounding box of rendered pixels within item 0's row, horizontally limited to one item part.
    private static Rectangle NativeAppearanceInk(nint list, int part, string name)
    {
        var row = new NativeRect(); // LVIR_BOUNDS
        var area = new NativeRect { Left = part };
        if (NativeAppearanceRectMessage(list, 0x100E, 0, ref row) == 0 ||
            NativeAppearanceRectMessage(list, 0x100E, 0, ref area) == 0)
            throw new InvalidOperationException("Native details has no measurable item.");
        NativeAppearanceGetClientRect(list, out var client);
        using var image = new Bitmap(client.Right, client.Bottom);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Magenta);
            nint dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(list, dc, 3)) // PW_CLIENTONLY | PW_RENDERFULLCONTENT
                    throw new InvalidOperationException("Could not capture native details list.");
            }
            finally { graphics.ReleaseHdc(dc); }
        }
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
            image.Save(Path.Combine(output, $"native-ink-{name}.png"));
        var ink = NativeAppearanceInkBounds(image, row, area);
        Console.WriteLine($"NATIVE_INK {name} ink={ink} row={row.Top}..{row.Bottom}");
        return ink;
    }

    private static Rectangle NativeAppearanceInkBounds(Bitmap image, NativeRect row, NativeRect area)
    {
        // The whole row's dominant color is its background; an icon can fill most of its own small cell.
        var counts = new Dictionary<int, int>();
        for (int y = Math.Max(0, row.Top); y < Math.Min(image.Height, row.Bottom); y++)
        for (int x = 0; x < image.Width; x++)
            counts[image.GetPixel(x, y).ToArgb()] = counts.GetValueOrDefault(image.GetPixel(x, y).ToArgb()) + 1;
        Color back = counts.Count == 0 ? Color.Empty : Color.FromArgb(counts.MaxBy(pair => pair.Value).Key);
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = Math.Max(0, row.Top); y < Math.Min(image.Height, row.Bottom); y++)
        for (int x = Math.Max(0, area.Left); x < Math.Min(image.Width, area.Right); x++)
        {
            Color pixel = image.GetPixel(x, y);
            if (Math.Abs(pixel.R - back.R) + Math.Abs(pixel.G - back.G) + Math.Abs(pixel.B - back.B) < 60) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static Bitmap NativeAppearancePaintTheme(nint window, string className, int part, Color background)
    {
        NativeAppearanceGetClientRect(window, out var bounds);
        var image = new Bitmap(bounds.Right, bounds.Bottom);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(background);
        nint dc = graphics.GetHdc();
        nint theme = NativeAppearanceOpenTheme(window, className);
        try
        {
            if (theme == 0) throw new InvalidOperationException("No native theme for " + className);
            Marshal.ThrowExceptionForHR(NativeAppearanceDrawTheme(theme, dc, part, part == 1 ? 1 : 0, ref bounds, 0));
        }
        finally
        {
            if (theme != 0) NativeAppearanceCloseTheme(theme);
            graphics.ReleaseHdc(dc);
        }
        return image;
    }

    private static void CheckNativeAppearanceHeader(nint list, string description)
    {
        nint header = SendMessage(list, 0x101F, 0, 0);
        using var image = NativeThemeCapture(header, "header-" + description.Replace(' ', '-').Replace('/', '-'));
        NativeThemeHeaderPixels(list, image, description);
    }

    private static void CheckNativeAppearanceBody(ShellViewHost host, nint list, string name, bool separators)
    {
        var view = (IVisualProperties)typeof(ShellViewHost)
            .GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(host)!;
        Marshal.ThrowExceptionForHR(view.GetColor(3, out int sortColor));
        Marshal.ThrowExceptionForHR(view.GetColor(2, out int background));
        Color sorted = ColorTranslator.FromWin32(sortColor), normal = ColorTranslator.FromWin32(background);
        Console.WriteLine($"NATIVE_BODY {name} sorted={sorted} normal={normal}");
        int Difference(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        Check(Difference(sorted, Theme.ListBack) <= 6 && Difference(normal, Theme.ListBack) <= 6,
            $"native sorted column and body share the background when highlighting is off {name}");
        if (!separators)
        {
            Check(NativeAppearanceGetWindowTheme(list) == 0 && (SendMessage(list, 0x1037, 0, 0) & 1) == 0,
                $"native body has neither themed column dividers nor grid lines {name}");
            return;
        }
        // ColumnDetail is the native body divider part, not ListDetail (a framed background).
        // Rasterizing this live theme part does not require a composed input desktop.
        using var image = NativeAppearancePaintTheme(list, "ListView", 10, normal); // LVP_COLUMNDETAIL
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
            image.Save(Path.Combine(output, $"native-details-{name}.png"));
        int y = image.Height / 2;
        int edgeContrast = 0;
        for (int x = 0; x < image.Width; x++)
        {
            Color edge = image.GetPixel(x, y);
            edgeContrast = Math.Max(edgeContrast, Difference(edge, normal));
        }
        Console.WriteLine($"NATIVE_SEPARATORS {name} contrast={edgeContrast} requested={separators}");
        Check(separators ? edgeContrast >= 12 : edgeContrast <= 6,
            $"native body vertical separators are {(separators ? "visible" : "absent")} {name}");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint NativeAppearanceRectMessage(nint window, uint message, nint wParam, ref NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    private static extern bool NativeAppearanceGetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int NativeAppearanceStyle(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint NativeAppearanceDpi(nint window);
    [DllImport("uxtheme.dll", EntryPoint = "SetWindowTheme", CharSet = CharSet.Unicode)]
    private static extern int NativeAppearanceSetTheme(nint window, string appName, string? idList);
    [DllImport("uxtheme.dll", EntryPoint = "GetWindowTheme")]
    private static extern nint NativeAppearanceGetWindowTheme(nint window);
    [DllImport("comctl32.dll", EntryPoint = "ImageList_GetIconSize")]
    private static extern bool NativeAppearanceImageSize(nint images, out int width, out int height);
    [DllImport("uxtheme.dll", EntryPoint = "OpenThemeData", CharSet = CharSet.Unicode)]
    private static extern nint NativeAppearanceOpenTheme(nint window, string className);
    [DllImport("uxtheme.dll", EntryPoint = "CloseThemeData")]
    private static extern int NativeAppearanceCloseTheme(nint theme);
    [DllImport("uxtheme.dll", EntryPoint = "DrawThemeBackground")]
    private static extern int NativeAppearanceDrawTheme(nint theme, nint dc, int part, int state, ref NativeRect rect, nint clip);
}

