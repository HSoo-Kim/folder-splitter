using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    // The Shell's own sorted-column highlight paints over the selection in the sorted column (Name by default),
    // leaving that cell in the list background. The Shell turns it back on by itself while the folder is in use,
    // also through its list interface, which sends no window message the app could rewrite.
    private static void RunSelectionFillScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "SelectionFill")).FullName;
        for (int i = 0; i < 6; i++)
            File.WriteAllText(Path.Combine(folder, $"sample-{i:D2}.txt"), "selection fill");
        var previousFont = Theme.ListFont;
        Theme.ListFont = (SystemFonts.IconTitleFont!.Name, SystemFonts.IconTitleFont.SizeInPoints);
        Theme.SetCustom(SessionStore.ParseColor("#626525"), SessionStore.ParseColor("#F6F7F9"));
        using var form = new Form { Text = "Selection fill regression", Size = new Size(1000, 650), ShowInTaskbar = false };
        using var host = new ShellViewHost(folder) { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        NativeAppearanceRedrawWatcher? redraw = null;
        Exception? failure = null;
        string[] selection = [Path.Combine(folder, "sample-00.txt"), Path.Combine(folder, "sample-02.txt")];
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                nint list = NativeAppearanceList(host);
                redraw = new NativeAppearanceRedrawWatcher();
                redraw.AssignHandle(list);
                await ItemCount(host, () => { }, 6);
                host.SetViewModeAndSize(4, 16);
                Theme.ApplyNative(host.Handle);
                host.ApplyThemeColors();
                // The owner's settings: no separators, comfortable rows, no sorted-column highlight.
                host.SetDetailsAppearance(false, true, false);
                await Settle();
                await CheckFill("initial");

                await ItemCount(host, () => File.WriteAllText(Path.Combine(folder, "sample-09.txt"), "arrived"), 7);
                await Settle();
                await CheckFill("after a file arrives");

                // The command bar's Sort menu goes through IFolderView2, not the header.
                var nameKey = new PropertyKey { FormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = 10 };
                host.SortBy(nameKey, true);
                await Settle();
                host.SortBy(nameKey, false);
                await Settle();
                await CheckFill("after the Sort menu");

                // Opening another folder and coming back builds a new Shell list in the same tab.
                string elsewhere = Directory.CreateDirectory(Path.Combine(root, "SelectionFillElsewhere")).FullName;
                File.WriteAllText(Path.Combine(elsewhere, "elsewhere.txt"), "elsewhere");
                await NativeAppearancePopulated(host, () => host.Navigate(elsewhere), 1);
                await NativeAppearancePopulated(host, () => host.Navigate(folder), 7);
                list = NativeAppearanceList(host);
                redraw.ReleaseHandle();
                redraw.AssignHandle(list);
                await CheckFill("after opening another folder and returning");

                // The list's own window procedure, below every subclass: the state the Shell's list interface
                // leaves behind without a message.
                SelectionFillCallWindowProc(SelectionFillClassProc(list, -24), list, 0x108C, 0, 0); // GCLP_WNDPROC, LVM_SETSELECTEDCOLUMN
                Check((int)SendMessage(list, 0x10AE, 0, 0) == 0, "selection fill: the Shell list highlights Name as its sorted column");
                await CheckFill("after the Shell highlights its sorted column without a message");

                // Selected rows are filled past the last column to the list's right edge. Moving the selection
                // must schedule that tail for repaint on both rows, or the old row keeps its fill there.
                string first = Path.Combine(folder, "sample-00.txt"), third = Path.Combine(folder, "sample-02.txt");
                Check(host.SelectPaths([first]) == 1, "selection fill: selects one row before moving the selection");
                await NativeAppearanceDispatch(host);
                SelectionFillUpdateWindow(list);
                Check(host.SelectPaths([third]) == 1, "selection fill: moves the selection to another row");
                NativeAppearanceGetClientRect(list, out var client);
                nint region = SelectionFillCreateRectRgn(0, 0, 0, 0);
                try
                {
                    SelectionFillGetUpdateRgn(list, region, false);
                    foreach (var (index, name) in new[] { (0, "the row that lost the selection"), (2, "the row that gained the selection") })
                    {
                        var row = new NativeRect();
                        NativeAppearanceRectMessage(list, 0x100E, index, ref row);
                        Check(row.Right < client.Right - 8, "selection fill: the columns end before the list's right edge");
                        Check(SelectionFillPtInRegion(region, client.Right - 4, (row.Top + row.Bottom) / 2),
                            "selection fill: " + name + " is repainted to the right of the last column");
                    }
                }
                finally { SelectionFillDeleteObject(region); }
                await NativeAppearanceDispatch(host);

                async Task Settle()
                {
                    await NativeAppearanceDispatch(host);
                    await NativeAppearanceRedrawn(redraw, list);
                }

                async Task CheckFill(string step)
                {
                    Check(host.SelectPaths(selection) == 2, "selection fill " + step + ": selects two real Shell items");
                    NativeThemeSetFocus(list);
                    await NativeAppearanceDispatch(host);
                    using var image = NativeThemeCapture(list, "selection-fill-" + step.Replace(' ', '-'));
                    NativeThemeSelectionPixels(list, image, "selection fill " + step);
                }
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                redraw?.ReleaseHandle();
                form.Close();
            }
        };
        try
        {
            Application.Run(form);
            if (failure is not null) throw new InvalidOperationException("Selection fill regression failed.", failure);
        }
        finally
        {
            Theme.ListFont = previousFont;
            Theme.Set("light");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint SelectionFillClassProc(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint SelectionFillCallWindowProc(nint procedure, nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "UpdateWindow")]
    private static extern bool SelectionFillUpdateWindow(nint window);

    [DllImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
    private static extern nint SelectionFillCreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll", EntryPoint = "GetUpdateRgn")]
    private static extern int SelectionFillGetUpdateRgn(nint window, nint region, bool erase);

    [DllImport("gdi32.dll", EntryPoint = "PtInRegion")]
    private static extern bool SelectionFillPtInRegion(nint region, int x, int y);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    private static extern bool SelectionFillDeleteObject(nint handle);
}
