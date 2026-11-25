using System.Runtime.InteropServices;
using QuadExplorer;

internal static partial class Program
{
    // A process's system DPI cannot change, so the display DPI the app compares it with is substituted. The triggers
    // are the messages Windows sends: a display change and a session connect to the activation window, and a real
    // per-monitor DPI change when the window moves to a monitor with other scaling.
    private static void RunDisplayScaleScenario(string root)
    {
        Check(FolderSplitterApplicationContext.ShouldRestartForDisplayScale(192, 120, 0)
            && FolderSplitterApplicationContext.ShouldRestartForDisplayScale(120, 192, 0)
            && !FolderSplitterApplicationContext.ShouldRestartForDisplayScale(120, 120, 0)
            && !FolderSplitterApplicationContext.ShouldRestartForDisplayScale(192, 120, 120)
            && !FolderSplitterApplicationContext.ShouldRestartForDisplayScale(120, 0, 0),
            "scaling other than the process started with restarts the app, unless the process already started that way");

        string folder = Directory.CreateDirectory(Path.Combine(root, "DisplayScale")).FullName;
        string session = Path.Combine(root, "display-scale.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, CheckUpdates = false, AutoRestartMemory = 0,
            WindowWidth = 900, WindowHeight = 600, SidebarWidth = 166, Panes = [new PaneState { Tabs = [folder] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                var context = form.ApplicationContext;
                var sidebarSplitter = Descendants(form).OfType<Splitter>().Single(s => s.AccessibleName == "Resize quick access");
                bool SidebarAtScale() => sidebarSplitter.Left == (int)Math.Round(166 * form.DeviceDpi / 96d);
                Console.WriteLine($"SIDEBAR_START dpi={form.DeviceDpi} system={context.SystemDpi} splitter_left={sidebarSplitter.Left}");
                Check(SidebarAtScale(), "a saved sidebar width opens at the window's scaling");
                var launches = new List<IReadOnlyList<string>>();
                form.RestartLauncher = arguments => { launches.Add(arguments); return false; };
                int scale = context.SystemDpi;
                context.DisplayDpi = () => scale;
                async Task<bool> Launched(int seconds)
                {
                    for (int i = 0; i < seconds * 10 && launches.Count == 0; i++) await Task.Delay(100);
                    return launches.Count > 0;
                }

                SendMessage(context.ActivationHandle, 0x007E, 32, 0); // WM_DISPLAYCHANGE
                Check(!await Launched(4), "a display change that keeps the scaling does not restart");

                scale = context.SystemDpi * 2; // a Mac session at 200%
                using (var dialog = new Form { Text = "Display scale dialog", ShowInTaskbar = false, Size = new Size(240, 140) })
                {
                    dialog.Show(form);
                    SendMessage(context.ActivationHandle, 0x02B1, 3, 1); // WM_WTSSESSION_CHANGE, WTS_REMOTE_CONNECT
                    Check(!await Launched(4), "a scaling change waits while a dialog is open");
                }
                Check(await Launched(5) && launches is [["--wait-pid", _, "--session", var given]] && given == session && !form.IsDisposed,
                    "after a session connects with other scaling, the app restarts with the same session once the dialog closes");
                await Task.Delay(4000);
                Check(launches.Count == 1, "a restart that could not start is not retried until the display changes again");

                int formDpi = form.DeviceDpi;
                var other = Screen.AllScreens.FirstOrDefault(screen => DsMonitorDpi(screen) != formDpi);
                if (other is null) Console.WriteLine("SKIP no monitor with other scaling for the window DPI change");
                else
                {
                    var home = Screen.FromControl(form);
                    async Task MoveTo(Screen screen)
                    {
                        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void OnChanged(object? sender, DpiChangedEventArgs e) => changed.TrySetResult();
                        form.DpiChanged += OnChanged;
                        try
                        {
                            form.Location = new Point(screen.WorkingArea.Left + 40, screen.WorkingArea.Top + 40);
                            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        }
                        finally { form.DpiChanged -= OnChanged; }
                    }
                    launches.Clear();
                    scale = context.SystemDpi;
                    await MoveTo(other);
                    Check(form.DeviceDpi != formDpi && !await Launched(4),
                        "moving a window to a monitor with other scaling does not restart while the session scaling is unchanged");
                    Console.WriteLine($"SIDEBAR_MOVED dpi={form.DeviceDpi} splitter_left={sidebarSplitter.Left}");
                    Check(SidebarAtScale(), "the sidebar keeps its saved width at the other monitor's scaling");
                    scale = context.SystemDpi * 2;
                    await MoveTo(home);
                    Check(await Launched(5) && launches.Count == 1, "a window's own DPI change also checks the session scaling");
                    Check(SidebarAtScale(), "the sidebar returns to its saved width at the first monitor's scaling");
                }
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }

    private static int DsMonitorDpi(Screen screen) =>
        DsGetDpiForMonitor(DsMonitorFromPoint(new Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1), 2), 0, out uint dpi, out _) == 0
            ? (int)dpi : 0;

    [DllImport("user32.dll", EntryPoint = "MonitorFromPoint")] private static extern nint DsMonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")] private static extern int DsGetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
}
