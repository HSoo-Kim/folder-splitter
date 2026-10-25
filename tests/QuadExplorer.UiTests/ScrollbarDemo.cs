using QuadExplorer;

internal static partial class Program
{
    // Explicit opt-in interactive QA. Uses temporary files, never a user's session or input.
    private static int RunScrollbarDemo(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "ScrollbarDemo")).FullName;
        for (int i = 0; i < 100; i++) File.WriteAllText(Path.Combine(folder, $"sample-{i:D3}.txt"), "scrollbar QA");
        Theme.Set("dim");
        using var form = new ScrollbarDemoForm
        {
            Text = "Folder Splitter - Scrollbar QA", Size = new Size(900, 500),
            StartPosition = FormStartPosition.CenterScreen
        };
        using var host = new ShellViewHost(folder) { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        var navigation = NavigationSignal(host);
        var populated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ItemCountChanged += (_, _) => { if (host.ItemCount == 100) populated.TrySetResult(); };
        ScrollbarInputTrace? trace = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                await populated.Task.WaitAsync(TimeSpan.FromSeconds(20));
                host.SetViewModeAndSize(4, 16);
                host.SetDetailsAppearance(false, false);
                Theme.ApplyNative(host.Handle);
                host.ApplyThemeColors();
                nint list = NativeAppearanceList(host);
                await NativeAppearanceIdle(() => SendMessage(list, 0x101E, 0, 1400));
                trace = new ScrollbarInputTrace();
                trace.AssignHandle(list);
                Console.WriteLine($"SCROLL_DEMO_READY hwnd={form.Handle} list={list}");
                ScrollCheckNativeClip(list);
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); form.Close(); }
        };
        Application.Run(form);
        trace?.ReleaseHandle();
        Console.WriteLine($"SCROLL_DEMO_DONE inputs={trace?.Inputs ?? 0}");
        return failures == 0 ? 0 : 1;
    }

    private sealed class ScrollbarDemoForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    private sealed class ScrollbarInputTrace : NativeWindow
    {
        internal int Inputs;
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is not (0x0114 or 0x0115)) return;
            Inputs++;
            var bar = ScrollInfo(Handle, -6);
            Console.WriteLine($"SCROLL_DEMO_INPUT msg={message.Msg:X} code={(long)message.WParam & 0xFFFF} thumb={bar.ThumbTop}..{bar.ThumbBottom}");
            if (message.Msg == 0x0114) ScrollCheckNativeClip(Handle);
        }
    }
}
