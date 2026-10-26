using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunNativeGroupThemeScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "NativeGroups")).FullName;
        for (int i = 0; i < 4; i++)
        {
            string path = Path.Combine(folder, $"group-{i}.txt");
            File.WriteAllText(path, "native group theme");
            File.SetLastWriteTime(path, DateTime.Today.AddDays(i < 2 ? 0 : -1).AddHours(1));
        }
        RunNativeGroupFolder(folder, "dates", true);
        RunNativeGroupFolder("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "this-pc", false);
    }

    private static void RunNativeGroupFolder(string folder, string context, bool dates)
    {
        Theme.Set("everforest-dark");
        using var form = new Form { Text = "Native group theme", Size = new Size(1000, 650), ShowInTaskbar = false };
        using var host = new ShellViewHost(folder) { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            NativeAppearanceRedrawWatcher? redraw = null;
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                nint list = NativeAppearanceList(host);
                using var groupsReady = new NativeGroupsReady(list);
                redraw = new NativeAppearanceRedrawWatcher();
                redraw.AssignHandle(list);
                host.SetViewModeAndSize(4, 16);
                if (dates)
                    host.GroupBy(new PropertyKey { FormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = 14 });
                await NativeAppearanceIdle(() => host.ApplyThemeColors());
                await NativeAppearanceRedrawn(redraw, list);
                await groupsReady.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
                int count = (int)SendMessage(list, 0x1098, 0, 0); // LVM_GETGROUPCOUNT
                Check(count >= (dates ? 2 : 1), context + " exposes real native groups");
                if (count == 0) throw new InvalidOperationException("No native groups in " + context);
                var first = NativeGroupInfoAt(list, 0);
                uint originalItems = first.Items;
                Console.WriteLine($"NATIVE_GROUP {context} groups={count} id={first.Id} items={first.Items} state={first.State:X}");
                foreach (string theme in new[] { "everforest-dark", "light", "dark", "everforest" })
                foreach (bool separators in new[] { true, false })
                foreach (bool collapsed in new[] { false, true })
                {
                    Theme.Set(theme);
                    Theme.ApplyNative(host.Handle);
                    host.ApplyThemeColors();
                    host.SetDetailsAppearance(separators, false);
                    var state = new NativeGroupInfo
                    {
                        Size = (uint)Marshal.SizeOf<NativeGroupInfo>(), Mask = 4,
                        StateMask = 1, State = collapsed ? 1u : 0
                    };
                    NativeGroupMessage(list, 0x1093, first.Id, ref state); // LVM_SETGROUPINFO, native collapse
                    await NativeAppearanceDispatch(host);
                    await NativeAppearanceRedrawn(redraw, list);
                    string name = $"{context}-{theme}-s{separators}-{(collapsed ? "collapsed" : "expanded")}";
                    using var image = NativeThemeCapture(list, "group-" + name);
                    var current = NativeGroupInfoAt(list, 0);
                    Check((current.State & 1) == (collapsed ? 1u : 0), name + " preserves native collapsed state");
                    Check(current.Items == originalItems && (int)SendMessage(list, 0x1098, 0, 0) == count,
                        name + " preserves Shell group count and membership");
                    var header = new NativeRect { Top = 1 }; // LVGGR_HEADER
                    Check(NativeAppearanceRectMessage(list, 0x1062, first.Id, ref header) != 0,
                        name + " has native header bounds");
                    var title = header;
                    title.Right = Math.Min(header.Right - 40, header.Left + 160);
                    int textPixels = NativeGroupColorPixels(image, title, Theme.Text);
                    var divider = header;
                    divider.Left += 180;
                    divider.Right -= 40;
                    var label = new NativeRect { Top = 2 }; // LVGGR_LABEL
                    NativeAppearanceRectMessage(list, 0x1062, first.Id, ref label);
                    divider.Top = (label.Top + label.Bottom) / 2;
                    divider.Bottom = divider.Top + 1;
                    int borderPixels = NativeGroupColorPixels(image, divider, Theme.Border);
                    Console.WriteLine($"NATIVE_GROUP_PIXELS {name} rect={header.Left},{header.Top},{header.Right},{header.Bottom} text={textPixels} border={borderPixels}");
                    Check(textPixels >= 10, name + " group title and native count follow selected theme");
                    Check(borderPixels >= 20, name + " group divider follows selected theme");
                    var target = NativeGroupCollapseArea(list, header);
                    var glyph = new NativeRect { Left = target.Left, Top = target.Top, Right = target.Right, Bottom = target.Bottom };
                    Check(NativeGroupColorPixels(image, glyph, Theme.Text) >= 5,
                        name + " themed collapse glyph lies inside its native click target");
                    if (dates)
                    {
                        int visibleItems = 0;
                        for (int item = 0; item < 4; item++)
                        {
                            var labelBounds = new NativeRect { Left = 2 }; // LVIR_LABEL
                            // LVM_ISITEMVISIBLE also reports items inside collapsed Shell groups.
                            // Only visible items have a label rectangle; assert the exact count below.
                            if (NativeAppearanceRectMessage(list, 0x100E, item, ref labelBounds) == 0) continue;
                            visibleItems++;
                            Check(NativeGroupColorPixels(image, labelBounds, Theme.Text) >= 5,
                                name + " group repaint preserves visible file labels");
                        }
                        Check(visibleItems == (collapsed ? 2 : 4), name + " collapsed state keeps other groups visible");
                    }
                    if (collapsed && !separators)
                    {
                        var belowHeader = header;
                        belowHeader.Top = header.Bottom;
                        belowHeader.Bottom = header.Bottom + 6;
                        int area = Math.Max(0, belowHeader.Right - belowHeader.Left) * Math.Max(0, belowHeader.Bottom - belowHeader.Top);
                        Check(NativeGroupColorPixels(image, belowHeader, Theme.ListBack) == area,
                            name + " collapsed footer has no system-colored leftover divider");
                    }
                }
                // Exercise the native header's own click handler, not only LVM_SETGROUPINFO.
                var expanded = new NativeGroupInfo { Size = (uint)Marshal.SizeOf<NativeGroupInfo>(), Mask = 4, StateMask = 1 };
                NativeGroupMessage(list, 0x1093, first.Id, ref expanded);
                var click = new NativeRect { Top = 1 };
                NativeAppearanceRectMessage(list, 0x1062, first.Id, ref click);
                var collapseArea = NativeGroupCollapseArea(list, click);
                Console.WriteLine($"GROUP_COLLAPSE_AREA {collapseArea}");
                Check(!collapseArea.IsEmpty, context + " native collapse hit area exists");
                nint point = ((collapseArea.Top + collapseArea.Height / 2) << 16) | ((collapseArea.Left + collapseArea.Width / 2) & 0xFFFF);
                await NativeAppearanceIdle(() =>
                {
                    PostMessage(list, 0x201, 1, point);
                    PostMessage(list, 0x202, 0, point);
                });
                Check((NativeGroupInfoAt(list, 0).State & 1) != 0, context + " native collapse hit target remains active");
                await NativeAppearanceIdle(() =>
                {
                    PostMessage(list, 0x201, 1, point);
                    PostMessage(list, 0x202, 0, point);
                });
                Check((NativeGroupInfoAt(list, 0).State & 1) == 0, context + " native expand hit target remains active");
                if (dates)
                {
                    string item = Path.Combine(folder, "group-0.txt");
                    Check(host.SelectPaths([item]) == 1 && SelectedPaths(host).SequenceEqual([item]),
                        "group custom draw preserves real Shell file selection");
                }
                Check(HasNativeDropTarget(host), context + " group custom draw preserves Shell drop target");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                redraw?.ReleaseHandle();
                form.Close();
            }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Native group scenario failed: " + context, failure);
    }

    private static NativeGroupInfo NativeGroupInfoAt(nint list, int index)
    {
        var group = new NativeGroupInfo { Size = (uint)Marshal.SizeOf<NativeGroupInfo>(), Mask = 0x4 | 0x10 | 0x4000, StateMask = uint.MaxValue };
        if (NativeGroupMessage(list, 0x1099, index, ref group) == -1)
            throw new InvalidOperationException("Cannot query native group.");
        return group;
    }

    private static int NativeGroupColorPixels(Bitmap image, NativeRect rect, Color color)
    {
        int count = 0;
        for (int y = Math.Max(0, rect.Top); y < Math.Min(image.Height, rect.Bottom); y++)
        for (int x = Math.Max(0, rect.Left); x < Math.Min(image.Width, rect.Right); x++)
            if (NativeThemeDifference(image.GetPixel(x, y), color) < 12) count++;
        return count;
    }

    private static Rectangle NativeGroupCollapseArea(nint list, NativeRect header)
    {
        var area = Rectangle.Empty;
        for (int y = header.Top; y < header.Bottom; y++)
        for (int x = header.Right - 40; x < header.Right; x++)
        {
            var hit = new NativeGroupHit { Point = new Point(x, y) };
            NativeGroupHitMessage(list, 0x1012, -1, ref hit);
            if ((hit.Flags & 0x40000000) != 0)
                area = area.IsEmpty ? new Rectangle(x, y, 1, 1) : Rectangle.Union(area, new Rectangle(x, y, 1, 1));
        }
        return area;
    }

    private sealed class NativeGroupsReady : NativeWindow, IDisposable
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NativeGroupsReady(nint list)
        {
            AssignHandle(list);
            Observe();
        }

        private void Observe()
        {
            if (!Ready.Task.IsCompleted && SendMessage(Handle, 0x1098, 0, 0) > 0 && NativeGroupInfoAt(Handle, 0).Items > 0)
                Ready.TrySetResult();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is 0x1091 or 0x1093 or 0x104D || message.Msg == 0x000B && message.WParam != 0)
                Observe();
        }

        public void Dispose() { if (Handle != 0) ReleaseHandle(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGroupInfo
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
    private static extern nint NativeGroupMessage(nint window, uint message, nint wParam, ref NativeGroupInfo group);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGroupHit { public Point Point; public uint Flags; public int Item, SubItem, Group; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint NativeGroupHitMessage(nint window, uint message, nint wParam, ref NativeGroupHit hit);
}
