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
}
