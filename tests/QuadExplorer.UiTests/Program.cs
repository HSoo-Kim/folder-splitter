using QuadExplorer;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int failures;
    private static int checks;

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Path.GetTempPath(), "QuadExplorer.UiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folders = Enumerable.Range(1, 4).Select(i => Directory.CreateDirectory(Path.Combine(root, $"Pane-{i}")).FullName).ToArray();
            string child = Directory.CreateDirectory(Path.Combine(folders[0], "\ud55c\uae00 \ud3f4\ub354")).FullName;
            File.WriteAllText(Path.Combine(folders[0], "sample.txt"), "QuadExplorer integration fixture");
            string session = Path.Combine(root, "session.json");
            SessionStore.Save(session, new SessionState
            {
                Layout = 4,
                Panes = folders.Select(path => new PaneState { Tabs = [path] }).ToList()
            });
            using var form = new MainForm(session, []);
            var panes = Descendants(form).OfType<ExplorerPane>().OrderBy(p => p.PaneNumber).ToArray();
            var initialNavigation = panes.Select(p => p.ActiveView!.HasNavigated
                ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.WhenAll(initialNavigation).WaitAsync(TimeSpan.FromSeconds(20));
                    Check(panes.Length == 4 && panes.All(p => p.Visible), "four native shell panes loaded");
                    Check(panes.Select(p => p.CurrentPath).SequenceEqual(folders, StringComparer.OrdinalIgnoreCase), "independent initial folders");
                    Check(panes.All(p => HasNativeDropTarget(p.ActiveView!)), "initial shell panes register native drop targets");

                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(child));
                    Check(panes[0].CurrentPath == child && panes[1].CurrentPath == folders[1], "Unicode folder navigation is isolated");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x4000));
                    Check(panes[0].CurrentPath == folders[0], "native back history");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x8000));
                    Check(panes[0].CurrentPath == child, "native forward history");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x2000));
                    Check(panes[0].CurrentPath == folders[0], "native parent navigation");

                    panes[0].AddTab(folders[1], select: false);
                    var newView = Descendants(panes[0]).OfType<ShellViewHost>().Single(h => h.LocationPath == folders[1]);
                    await Navigate(newView, () => panes[0].SelectTab(1));
                    Check(panes[0].TabCount == 2 && panes[0].CurrentPath == folders[1], "new independent tab");
                    Check(HasNativeDropTarget(newView), "new shell tab registers a native drop target");
                    panes[0].CloseTab();
                    Check(panes[0].TabCount == 1 && panes[0].CurrentPath == folders[0], "close restores previous tab");
                    panes[0].ReopenTab();
                    Check(panes[0].TabCount == 2, "reopen closed tab");
                    panes[0].SelectTab(0);
                    panes[0].ActiveView!.SetViewMode(3);
                    panes[0].ActiveView!.SetViewMode(4);
                    panes[0].ActiveView!.RefreshFolder();
                    Check(true, "native view modes and refresh");

                    ClickLayout(form, 1);
                    Check(panes[0].Visible && panes.Skip(1).All(p => !p.Visible), "one-pane layout");
                    ClickLayout(form, 2);
                    Check(panes[0].Visible && panes[1].Visible && !panes[2].Visible && !panes[3].Visible, "two-pane layout");
                    ClickLayout(form, 4);
                    Check(panes.All(p => p.Visible) && panes[0].TabCount == 2, "four-pane restore preserves tabs");
                    form.Size = new Size(900, 600);
                    Check(panes.All(p => p.Width > 200 && p.Height > 100), "minimum-size pane layout");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
            var restored = SessionStore.Load(session, out var warning);
            Check(warning is null && restored.Layout == 4 && restored.Panes[0].Tabs.Count == 2, "session saved on close");
            Check(restored.Panes.Skip(1).Select(p => p.Tabs[0]).SequenceEqual(folders.Skip(1)), "inactive pane paths persisted");
            RunDockingScenario(session, folders);
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine(failures == 0 ? $"PASS {checks} native UI integration checks" : $"FAIL {failures} native UI checks ({checks} passed)");
        return failures == 0 ? 0 : 1;
    }

    private static void RunDockingScenario(string session, string[] folders)
    {
        using var form = new MainForm(session, []);
        var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
        var navigation = panes.Select(p => p.ActiveView!.HasNavigated
            ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
        PaneLayout? expectedTree = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.WhenAll(navigation).WaitAsync(TimeSpan.FromSeconds(20));
                ClickLayout(form, 3);
                Check(panes[0].Visible && panes[1].Visible && panes[2].Visible && !panes[3].Visible
                    && panes[1].Height > panes[0].Height * 1.5, "three panes: two left and one right");
                form.ApplyLayout(3, false);
                Check(panes[0].Height > panes[1].Height * 1.5 && !panes[3].Visible, "three panes: one left and two right");
                form.ApplyLayout(3, true, false);
                Check(panes[1].Width > panes[0].Width * 1.5, "three panes: horizontal arrangement");
                Check(panes.Skip(1).Select(p => p.CurrentPath).SequenceEqual(folders.Skip(1)),
                    "three-pane presets preserve hidden folder paths");

                ClickLayout(form, 1);
                var moving = panes[0].ActiveView!;
                await PaneChange(panes[1], () => Check(form.DockTab(panes[0], moving, 0, SplitEdge.Right),
                    "edge docking accepts a second pane"), () => panes[1].ActiveView!.HasNavigated && panes[1].CurrentPath == folders[0]);
                Check(form.CurrentLayout.GetPanes().Length == 2 && panes[0].TabCount == 1 && panes[1].TabCount == 2,
                    "edge docking moves the chosen tab without losing cached tabs");

                moving = panes[1].ActiveView!;
                await PaneChange(panes[2], () => Check(form.DockTab(panes[1], moving, 1, SplitEdge.Bottom),
                    "edge docking accepts a third pane"), () => panes[2].ActiveView!.HasNavigated && panes[2].CurrentPath == folders[0]);
                Check(form.CurrentLayout.First!.IsLeaf && !form.CurrentLayout.Second!.IsLeaf
                    && !form.CurrentLayout.Second.Vertical, "docking creates a one-plus-two layout");

                moving = panes[2].ActiveView!;
                await PaneChange(panes[3], () => Check(form.DockTab(panes[2], moving, 2, SplitEdge.Left),
                    "edge docking accepts a fourth pane"), () => panes[3].ActiveView!.HasNavigated && panes[3].CurrentPath == folders[0]);
                var before = form.CurrentLayout;
                int totalTabs = panes.Sum(p => p.TabCount);
                Check(!form.DockTab(panes[3], panes[3].ActiveView!, 3, SplitEdge.Bottom)
                    && form.CurrentLayout == before && panes.Sum(p => p.TabCount) == totalTabs,
                    "fifth-pane rejection keeps layout and tabs unchanged");

                moving = panes[2].ActiveView!;
                await PaneChange(panes[0], () => Check(form.DockTab(panes[2], moving, 0, null),
                    "center docking accepts a tab move"), () => panes[0].ActiveView!.HasNavigated && panes[0].CurrentPath == folders[2]);
                Check(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 })
                    && !panes[2].Visible, "moving the last tab collapses the empty source pane");
                panes[3].SelectTab(0);
                expectedTree = form.CurrentLayout;
                form.ToggleFavorites();
                Check(!form.FavoritesExpanded, "favorites collapse independently of the sidebar");
                form.ToggleFavorites();
                Check(form.FavoritesExpanded, "favorites expand again");
                form.ToggleFavorites();
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session, out var warning);
        Check(warning is null && restored.Layout == 3 && restored.ActivePane == 3
            && restored.LayoutTree == expectedTree, "docked layout and sparse active pane survive session save");
        Check(restored.Panes[0].Tabs.SequenceEqual(new[] { folders[1], folders[2] })
            && restored.Panes[3].Tabs.SequenceEqual(new[] { folders[3], folders[0] }),
            "moved and cached tabs survive session save");
        Check(panes.All(p => p.IsDisposed), "closing the form disposes hidden pane views");
        Check(!restored.FavoritesExpanded, "collapsed favorites survive session save");
    }

    private static async Task PaneChange(ExplorerPane pane, Action trigger, Func<bool> complete)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e) { if (complete()) signal.TrySetResult(); }
        pane.StateChanged += Changed;
        try
        {
            trigger();
            Changed(pane, EventArgs.Empty);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { pane.StateChanged -= Changed; }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static Task NavigationSignal(ShellViewHost host)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? completed = null;
        EventHandler<string>? failed = null;
        completed = (_, _) =>
        {
            host.Navigated -= completed;
            host.NavigationError -= failed;
            source.TrySetResult();
        };
        failed = (_, error) =>
        {
            host.Navigated -= completed;
            host.NavigationError -= failed;
            source.TrySetException(new IOException(error));
        };
        host.Navigated += completed;
        host.NavigationError += failed;
        return source.Task;
    }

    private static async Task Navigate(ShellViewHost host, Action trigger)
    {
        var navigation = NavigationSignal(host);
        trigger();
        await navigation.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static void ClickLayout(MainForm form, int count)
    {
        var button = Descendants(form).OfType<ToolStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(item => item.AccessibleName == $"{count} pane layout");
        button.PerformClick();
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new InvalidOperationException(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetProp(nint window, string property);

    private static bool HasNativeDropTarget(Control host)
    {
        bool registered = false;
        EnumChildWindows(host.Handle, (window, _) =>
        {
            registered |= GetProp(window, "OleDropTargetInterface") != 0
                || GetProp(window, "OleDropTargetMarshalHwnd") != 0;
            return true;
        }, 0);
        return registered;
    }

}
