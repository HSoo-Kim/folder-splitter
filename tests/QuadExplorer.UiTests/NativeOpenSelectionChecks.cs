using QuadExplorer;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

internal static partial class Program
{
    // The shortcut opens this test executable, never a registered user application.
    // Both processes inherit the runner's private desktop and kill-on-close job.
    private static int RunNativeOpenReceipt(string pipe)
    {
        using var receiver = new Form { Text = "Owned native open receiver", Size = new Size(320, 180), ShowInTaskbar = false };
        receiver.Shown += (_, _) =>
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(10_000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine($"{Environment.ProcessId} {receiver.Handle}");
        };
        Application.Run(receiver);
        return 0;
    }

    private static void RunNativeOpenSelectionScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "NativeOpenSelection")).FullName;
        string pipe = "FolderSplitter-Open-" + Guid.NewGuid().ToString("N");
        const int targetIndex = 12;
        string shortcut = Path.Combine(folder, $"sample-{targetIndex:D3}.lnk");
        for (int i = 0; i < 15; i++)
            if (i != targetIndex) File.WriteAllText(Path.Combine(folder, $"sample-{i:D3}.txt"), "owned scroll fixture");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object link = ((dynamic)shell).CreateShortcut(shortcut);
        try
        {
            ((dynamic)link).TargetPath = Environment.ProcessPath!;
            string assembly = typeof(Program).Assembly.Location;
            ((dynamic)link).Arguments = (Path.GetFileNameWithoutExtension(Environment.ProcessPath!)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? $"\"{assembly}\" " : "")
                + "--native-open-receipt " + pipe;
            ((dynamic)link).Save();
        }
        finally { Marshal.ReleaseComObject(link); Marshal.ReleaseComObject(shell); }

        var previousFont = Theme.ListFont;
        Theme.ListFont = (SystemFonts.IconTitleFont!.Name, SystemFonts.IconTitleFont.SizeInPoints);
        string session = Path.Combine(root, "native-open-session.json");
        SessionStore.Save(session, new SessionState
        {
            WindowWidth = 900, WindowHeight = 600, Layout = 1, ColorTheme = "dark",
            GlobalHotkey = false, KeepInTray = false, CheckUpdates = false, AutoRestartMemory = 0,
            ExplorerFavoritesImported = true, ShowFolderTree = false,
            Panes = Enumerable.Range(0, 4).Select(i => new PaneState
            {
                Tabs = [i == 0 ? folder : Directory.CreateDirectory(Path.Combine(root, "OpenUnused-" + i)).FullName]
            }).ToList()
        });
        using var form = new MainForm(session, []);
        var pane = form.GetPane(0);
        var host = pane.ActiveView!;
        var navigation = host.HasNavigated ? Task.CompletedTask : NavigationSignal(host);
        Exception? failure = null;
        NativeAppearanceRedrawWatcher? redraw = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                redraw = new NativeAppearanceRedrawWatcher();
                redraw.AssignHandle(NativeAppearanceList(host));
                await ItemCountKnown(host);
                host.SetViewModeAndSize(4, 16);
                nint list = NativeAppearanceList(host);
                await NativeAppearanceRedrawn(redraw, list);
                host.SetDetailsAppearance(false, false);
                int compactHeight = NativeAppearanceRowHeight(list);
                host.SetDetailsAppearance(false, true);
                int comfortableHeight = NativeAppearanceRowHeight(list);
                NativeAppearanceGetClientRect(SendMessage(list, 0x101F, 0, 0), out var header);
                NativeAppearanceGetClientRect(list, out var client);
                // Keep the failure boundary deterministic across DPI: all 15 rows fit with the
                // compact measurement font, but not with the comfortable font used for display.
                form.MinimumSize = Size.Empty;
                await NativeAppearanceIdle(() =>
                    form.Height += header.Bottom + 15 * (compactHeight + comfortableHeight) / 2 - client.Bottom);
                foreach (string theme in new[] { "light", "dark" })
                foreach (bool separators in new[] { false, true })
                {
                    string name = $"{theme}-s{separators}";
                    await NativeAppearanceIdle(() =>
                    {
                        Theme.Set(theme);
                        Theme.ApplyNative(host.Handle);
                        host.ApplyThemeColors();
                        host.SetDetailsAppearance(separators, true);
                    });
                    await NativeAppearanceRedrawn(redraw, list);
                    NativeAppearanceGetClientRect(list, out client);
                    int available = client.Bottom - header.Bottom;
                    Check(compactHeight * 15 <= available && comfortableHeight * 15 > available,
                        name + " compact font fits all files while comfortable rows require scrolling");
                    host.FocusView();
                    Check(host.SelectPaths([Path.Combine(folder, "sample-000.txt")]) == 1,
                        name + " starts with a different focused file above the viewport");
                    await NativeAppearanceIdle(() => { });
                    int page = (int)SendMessage(list, 0x1028, 0, 0); // LVM_GETCOUNTPERPAGE
                    SendMessage(list, 0x1014, 0, NativeAppearanceRowHeight(list) * (targetIndex - Math.Min(3, page - 1)));
                    int scrolled = (int)SendMessage(list, 0x1027, 0, 0);
                    NativeThemeSetFocus(form.Handle);
                    var selectedBounds = new NativeRect();
                    NativeAppearanceRectMessage(list, 0x100E, targetIndex, ref selectedBounds);
                    nint point = ((selectedBounds.Top + selectedBounds.Bottom) / 2 << 16) | 100;
                    var summary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void SummaryChanged(object? sender, EventArgs e)
                    {
                        if (SelectedPaths(host).SequenceEqual([shortcut])) summary.TrySetResult();
                    }
                    pane.SelectionSummaryChanged += SummaryChanged;
                    await NativeAppearanceIdle(() =>
                    {
                        PostMessage(list, 0x0201, 1, point);
                        PostMessage(list, 0x0202, 0, point);
                    });
                    try { await summary.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                    finally { pane.SelectionSummaryChanged -= SummaryChanged; }
                    int selected = (int)SendMessage(list, 0x100C, -1, 2);
                    int before = (int)SendMessage(list, 0x1027, 0, 0);
                    int thumb = ScrollInfo(list, -5).ThumbTop;
                    Console.WriteLine($"NATIVE_OPEN before {name} hwnd={list:X} top={before} selected={selected} thumb={thumb}");
                    Check(before > 0 && selected > before, name + " fixture is scrolled to a lower visible item");
                    Check(before == scrolled && SelectedPaths(host).SequenceEqual([shortcut]),
                        name + " native click selects the visible file without jumping to the old focused file");
                    CheckOpenSelectionPixels(host, list, name + "-before");

                    using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    var connected = server.WaitForConnectionAsync();
                    // Posted mouse messages do not synthesize the parent notification generated
                    // by the Windows input route. Deliver that native notification explicitly.
                    SendMessage(WheelGetParent(list), 0x0210, 0x0201, point);
                    Check(PostMessage(list, 0x0203, 1, point) && PostMessage(list, 0x0202, 0, point),
                        name + " queues native double-click open without global input");
                    await connected.WaitAsync(TimeSpan.FromSeconds(20));
                    using var reader = new StreamReader(server);
                    string[] receipt = (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!.Split(' ');
                    using var process = Process.GetProcessById(int.Parse(receipt[0]));
                    nint receiver = nint.Parse(receipt[1]);
                    Console.WriteLine($"NATIVE_OPEN_RECEIPT {name} pid={process.Id} hwnd={receiver:X} fixture={shortcut}");
                    var exited = process.WaitForExitAsync();
                    Check(PostMessage(receiver, 0x0010, 0, 0), name + " closes only the owned receiver");
                    await exited.WaitAsync(TimeSpan.FromSeconds(20));
                    await NativeAppearanceIdle(() => { form.Activate(); host.FocusView(); });

                    int after = (int)SendMessage(list, 0x1027, 0, 0);
                    Console.WriteLine($"NATIVE_OPEN after {name} hwnd={NativeAppearanceList(host):X} top={after} selected={(int)SendMessage(list, 0x100C, -1, 2)} thumb={ScrollInfo(list, -5).ThumbTop}");
                    Check(SelectedPaths(host).SequenceEqual([shortcut]), name + " delete-target paths remain the opened file");
                    CheckOpenSelectionPixels(host, list, name + "-after");
                    Check(NativeAppearanceList(host) == list && before == after && thumb == ScrollInfo(list, -5).ThumbTop,
                        name + " opening and returning focus preserves native vertical scroll");
                    string[] multiple = [shortcut, Path.Combine(folder, "sample-013.txt")];
                    Check(host.SelectPaths(multiple) == 2, name + " selects two owned delete-target files");
                    await pane.UpdateSelectionSummary();
                    int multiTop = (int)SendMessage(list, 0x1027, 0, 0);
                    CheckOpenSelectionPixels(host, list, name + "-multiple-before");
                    await NativeAppearanceIdle(() =>
                    {
                        NativeThemeSetFocus(form.Handle);
                        SendMessage(WheelGetParent(list), 0x0210, 0x0201, point);
                        host.FocusView();
                    });
                    Check(SelectedPaths(host).Order().SequenceEqual(multiple.Order())
                        && (int)SendMessage(list, 0x1027, 0, 0) == multiTop,
                        name + " focus and appearance preserve both actual delete-target paths and scroll");
                    CheckOpenSelectionPixels(host, list, name + "-multiple-after");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { redraw?.ReleaseHandle(); form.ExitApplication(); }
        };
        try
        {
            Application.Run(form);
            if (failure is not null) throw new InvalidOperationException("Native open selection regression failed.", failure);
        }
        finally { Theme.ListFont = previousFont; }
    }

    private static void CheckOpenSelectionPixels(ShellViewHost host, nint list, string name)
    {
        using var image = NativeThemeCapture(list, "open-" + name);
        int top = (int)SendMessage(list, 0x1027, 0, 0);
        NativeAppearanceGetClientRect(SendMessage(list, 0x101F, 0, 0), out var header);
        int selectedRows = 0, visibleRows = 0;
        for (int index = top; index < host.ItemCount; index++)
        {
            var row = new NativeRect();
            if (NativeAppearanceRectMessage(list, 0x100E, index, ref row) == 0 || row.Top >= image.Height) break;
            int y = row.Bottom - 2;
            if (row.Top < header.Bottom || y >= image.Height) continue;
            bool selected = (SendMessage(list, 0x102C, index, 2) & 2) != 0;
            int matching = 0, total = 0;
            for (int x = 40; x < Math.Min(row.Right, image.Width) - 8; x++)
            {
                if (NativeThemeDifference(image.GetPixel(x, y), selected ? Theme.Selected : Theme.ListBack) < 12) matching++;
                total++;
            }
            Console.WriteLine($"OPEN_PIXELS {name} index={index} y={y} matching={matching}/{total} selected={selected}");
            Check(total > 0 && matching > total * .99, name + $" row {index} pixels match actual selection={selected}");
            if (selected) selectedRows++;
            visibleRows++;
        }
        Check(visibleRows >= 2 && selectedRows == host.SelectionCount,
            name + " every delete-target item has one visible highlighted row");
    }

}
