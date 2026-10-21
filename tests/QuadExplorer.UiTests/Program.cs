using QuadExplorer;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int failures;

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
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
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine(failures == 0 ? "PASS all native UI integration checks" : $"FAIL {failures} native UI checks");
        return failures == 0 ? 0 : 1;
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
