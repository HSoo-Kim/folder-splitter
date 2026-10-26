using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunNativeNavigationThemeScenario(string root)
    {
        string first = Directory.CreateDirectory(Path.Combine(root, "NavigationFirst")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(root, "NavigationSecond")).FullName;
        File.WriteAllText(Path.Combine(first, "first.txt"), "first");
        for (int i = 0; i < 100; i++)
            File.WriteAllText(Path.Combine(second, $"second-{i:D3}.txt"), "second");
        Theme.Set("everforest-dark");
        using var form = new Form { Text = "Navigation theme regression", Size = new Size(900, 600), ShowInTaskbar = false };
        using var host = new ShellViewHost(first);
        form.Controls.Add(host);
        Theme.Apply(form);
        using var probe = new NavigationPaintProbe(host);
        var navigation = NavigationSignal(host);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                await NativeAppearanceDispatch(host);
                foreach (string theme in new[] { "everforest-dark", "dark", "light", "everforest" })
                {
                    Theme.Set(theme);
                    Theme.Apply(form);
                    host.ApplyThemeColors();
                    await NativeAppearancePopulated(host, () => host.Navigate(second), 100);
                    await NativeAppearancePopulated(host, () => host.Travel(0x4000), 1);
                    await NativeAppearancePopulated(host, () => host.Travel(0x8000), 100);
                    string selected = Path.Combine(second, "second-000.txt");
                    Check(host.SelectPaths([selected]) == 1 && SelectedPaths(host).SequenceEqual([selected]),
                        theme + " navigation background preserves native file selection");
                    Check(HasNativeDropTarget(host), theme + " navigation preserves native drop targets");
                    await NativeAppearancePopulated(host, () => host.Navigate(first), 1);
                }
                int errors = 0;
                host.NavigationError += (_, _) => errors++;
                host.Navigate(Path.Combine(root, "DoesNotExist"));
                Check(errors == 1, "failed navigation still reports an unavailable folder");
                await NativeAppearancePopulated(host, () => host.Navigate(second), 100);
                if (probe.Failure is not null) throw probe.Failure;
                Check(probe.Samples >= 18, "navigation observes new browser and folder backgrounds");
                Check(probe.WrongBackgrounds == 0, "new native folder containers erase with the theme background before navigation completes");
            }
            catch (Exception ex) { failure = ex; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Navigation theme regression failed.", failure);
    }

    private sealed class NavigationPaintProbe : IDisposable
    {
        private readonly ShellViewHost host;
        private readonly NavigationHook callback;
        private readonly nint hook;
        private bool sampling;
        private readonly HashSet<nint> seen = [];
        public int Samples { get; private set; }
        public int WrongBackgrounds { get; private set; }
        public Exception? Failure { get; private set; }

        public NavigationPaintProbe(ShellViewHost host)
        {
            this.host = host;
            callback = Observe;
            hook = NavigationSetHook(12, callback, 0, GetCurrentThreadId()); // WH_CALLWNDPROCRET
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private nint Observe(int code, nint wParam, nint lParam)
        {
            if (code >= 0 && !sampling && host.IsHandleCreated)
            {
                var message = Marshal.PtrToStructure<NavigationReturnedMessage>(lParam);
                if (message.Message == 0x0082) seen.Remove(message.Window);
                if (host.OwnsWindow(message.Window) && message.Message == 0x0210 && ((long)message.WParam & 0xffff) == 1)
                {
                    nint window = message.LParam; // WM_PARENTNOTIFY / WM_CREATE: parent has handled creation.
                    var name = new System.Text.StringBuilder(128);
                    GetClassName(window, name, name.Capacity);
                    if (host.OwnsWindow(window) && name.ToString() is "ExplorerBrowserControl" or "SHELLDLL_DefView"
                        && seen.Add(window))
                    {
                        sampling = true;
                        try
                        {
                            NativeAppearanceGetClientRect(window, out var client);
                            if (client.Right > 100 && client.Bottom > 100)
                            {
                                Samples++;
                                using var image = new Bitmap(client.Right, client.Bottom);
                                using (var graphics = Graphics.FromImage(image))
                                {
                                    graphics.Clear(Color.Magenta);
                                    nint dc = graphics.GetHdc();
                                    try { SendMessage(window, 0x14, dc, 0); }
                                    finally { graphics.ReleaseHdc(dc); }
                                }
                                Color pixel = image.GetPixel(image.Width / 2, image.Height / 2);
                                if (NativeThemeDifference(pixel, Theme.ListBack) > 12
                                    || NativeThemeDifference(image.GetPixel(1, 1), Theme.ListBack) > 12
                                    || NativeThemeDifference(image.GetPixel(image.Width - 2, image.Height - 2), Theme.ListBack) > 12)
                                    WrongBackgrounds++;
                                var parentName = new System.Text.StringBuilder(128);
                                GetClassName(message.Window, parentName, parentName.Capacity);
                                Console.WriteLine($"NAV_ERASE {Samples} class={name} parent={parentName} ready={host.HasNavigated} color={pixel} size={image.Size}");
                                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                                    image.Save(Path.Combine(output, $"navigation-{Samples}-{name}.png"));
                            }
                        }
                        catch (Exception ex) { Failure = ex; }
                        finally { sampling = false; }
                    }
                }
            }
            return NavigationNextHook(hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (!NavigationUnhook(hook)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            GC.KeepAlive(callback);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NavigationReturnedMessage
    {
        public nint Result, LParam, WParam;
        public uint Message;
        public nint Window;
    }

    private delegate nint NavigationHook(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint NavigationSetHook(int type, NavigationHook callback, nint module, uint thread);
    [DllImport("user32.dll", EntryPoint = "CallNextHookEx")]
    private static extern nint NavigationNextHook(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", SetLastError = true)]
    private static extern bool NavigationUnhook(nint hook);
}
