using QuadExplorer;
using System.Reflection;

internal static partial class Program
{
    private static Func<object, DragDropEffects, DragDropEffects>? blankTabDrop;

    private static bool DispatchBlankTabDrop(object data, DragDropEffects allowedEffects, ref DragDropEffects __result)
    {
        if (blankTabDrop is null) return true;
        __result = blankTabDrop(data, allowedEffects);
        return false;
    }

    private static void RunBlankTabDockingScenario(string root)
    {
        string session = Path.Combine(root, "blank-tab-docking.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, ExplorerFavoritesImported = true,
            Panes = [new PaneState()]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            var transport = new HarmonyLib.Harmony("FolderSplitter.BlankTabDocking");
            var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
                [typeof(object), typeof(DragDropEffects)]);
            try
            {
                var pane = form.GetPane(0);
                pane.AddBlankTab();
                var forwarding = typeof(ExplorerPane).GetField("TabDragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object? handlers = forwarding.GetValue(pane);
                forwarding.SetValue(pane, null);
                bool requested = false;
                pane.TabDragRequested += (_, _) => requested = true;
                try
                {
                    var button = pane.TabButtons.Single();
                    var drag = (EventHandler)typeof(FolderTabButton)
                        .GetField("DragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!;
                    drag(button, EventArgs.Empty);
                    Check(requested, "dragging a blank tab reaches the pane docking request without needing a shell view");
                }
                finally { forwarding.SetValue(pane, handlers); }

                // Exercise the real tab event, MainForm drag handler, overlay hit testing and drop.
                // Only OLE's global-input loop is replaced: private-desktop QA must not send mouse input.
                transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
                void Drag(ExplorerPane source, int target, SplitEdge? edge, bool allowed = true, bool cancel = false, string? capture = null)
                {
                    Rectangle files = form.GetPane(target).FileAreaOnScreen;
                    var point = edge switch
                    {
                        SplitEdge.Left => new Point(files.Left + 1, files.Top + files.Height / 2),
                        SplitEdge.Right => new Point(files.Right - 1, files.Top + files.Height / 2),
                        SplitEdge.Top => new Point(files.Left + files.Width / 2, files.Top + 1),
                        SplitEdge.Bottom => new Point(files.Left + files.Width / 2, files.Bottom - 1),
                        _ => new Point(files.Left + files.Width / 2, files.Top + files.Height / 2)
                    };
                    bool entered = false;
                    blankTabDrop = (data, effects) =>
                    {
                        entered = true;
                        var overlay = Application.OpenForms.OfType<TabDockOverlay>().Single();
                        var args = new DragEventArgs((IDataObject)data, 0, point.X, point.Y, effects, DragDropEffects.None);
                        foreach (string name in new[] { "OnDragEnter", "OnDragOver" })
                            typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);
                        Check(overlay.Visible && args.Effect == (allowed ? DragDropEffects.Move : DragDropEffects.None),
                            "blank-tab drag reaches the visible overlay and resolves the requested drop effect");
                        if (capture is not null && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                        {
                            using var image = new Bitmap(overlay.Width, overlay.Height);
                            overlay.DrawToBitmap(image, new Rectangle(Point.Empty, overlay.Size));
                            image.Save(Path.Combine(output, capture + "-preview.png"));
                        }
                        if (cancel) return DragDropEffects.None;
                        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [args]);
                        return args.Effect;
                    };
                    try
                    {
                        var button = source.ActiveTab!.Button;
                        var drag = (EventHandler)typeof(FolderTabButton)
                            .GetField("DragRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!;
                        drag(button, EventArgs.Empty);
                        Check(entered && !Application.OpenForms.OfType<TabDockOverlay>().Any(),
                            "blank-tab drag finishes through the main handler and disposes its overlay");
                    }
                    finally { blankTabDrop = null; }
                }
                void Reset()
                {
                    form.ApplyLayout(1);
                    foreach (var each in Enumerable.Range(0, 4).Select(form.GetPane))
                        while (each.TabCount > 0) each.CloseTab(remember: false);
                    pane.AddBlankTab();
                    pane.AddBlankTab();
                }
                foreach (string theme in new[] { "light", "dark" })
                foreach (SplitEdge edge in Enum.GetValues<SplitEdge>())
                {
                    Reset();
                    form.SetColorTheme(theme);
                    string capture = $"blank-{theme}-{edge}";
                    Drag(pane, 0, edge, capture: capture);
                    var receiving = form.GetPane(1);
                    Check(form.CurrentLayout.GetPanes().Length == 2 && pane.TabCount == 1
                        && receiving.TabCount == 1 && receiving.ActiveView is null && receiving.CurrentPath.Length == 0,
                        $"blank tab splits {edge} without opening a default folder in {theme}");
                    Check(pane.ActiveTab != receiving.ActiveTab && !pane.ActiveTab!.Button.IsDisposed
                        && receiving.CaptureState().Tabs.Count == 0,
                        "distinct blank tabs retain their identity and remain excluded from saved folder tabs");
                    if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                    {
                        using var image = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(output, capture + "-result.png"));
                    }
                    Drag(receiving, 0, null);
                    Check(form.CurrentLayout.GetPanes().SequenceEqual(new[] { 0 }) && pane.TabCount == 2 && receiving.TabCount == 0,
                        "moving the last blank tab into another pane collapses its source without duplication");
                }
                Reset();
                var before = form.CurrentLayout;
                var selected = pane.ActiveTab;
                Drag(pane, 0, SplitEdge.Right, cancel: true);
                Check(form.CurrentLayout == before && pane.TabCount == 2 && pane.ActiveTab == selected,
                    "canceling a blank-tab drag preserves the selected tab and layout");
                Drag(pane, 0, null, allowed: false);
                Check(form.CurrentLayout == before && pane.TabCount == 2,
                    "dropping a blank tab into its own pane center does not remove it");
                form.ApplyLayout(4);
                before = form.CurrentLayout;
                Drag(pane, 0, SplitEdge.Bottom, allowed: false);
                Check(form.CurrentLayout == before && pane.TabCount == 2,
                    "a blank tab cannot create a fifth pane");
                pane.CloseTab(remember: false);
                var relocated = pane.ActiveTab;
                Drag(pane, 1, SplitEdge.Right);
                Check(form.CurrentLayout.GetPanes().Length == 4 && pane.ActiveTab == relocated && pane.TabCount == 1,
                    "the last blank tab can reposition its existing pane at the four-pane limit");
                Reset();
                form.ApplyLayout(2);
                var full = form.GetPane(1);
                for (int i = 0; i < 32; i++) full.AddBlankTab();
                Drag(pane, 1, null, allowed: false);
                Check(pane.TabCount == 2 && full.TabCount == 32,
                    "blank-tab docking respects the destination's 32-tab limit");
                var removed = pane.ActiveTab!;
                pane.CloseTab(remember: false);
                Check(!form.DockTab(pane, removed, 1, null), "a removed blank tab cannot be docked using a stale identity");

                Reset();
                string folder = Directory.CreateDirectory(Path.Combine(root, "BlankDragFolder")).FullName;
                await PaneChange(pane, () => pane.Navigate(folder), () => pane.ActiveView is { HasNavigated: true });
                var view = pane.ActiveView;
                pane.AddBlankTab();
                Drag(pane, 0, SplitEdge.Right);
                Check(pane.ActiveView == view && pane.CurrentPath == folder && form.GetPane(1).ActiveView is null,
                    "splitting a blank tab preserves the populated tab and its live shell view");
                var destination = form.GetPane(1);
                await PaneChange(destination, () => destination.Navigate(folder), () => destination.ActiveView is { HasNavigated: true });
                Check(destination.TabCount == 1 && destination.CurrentPath == folder,
                    "a split blank tab accepts navigation in place without creating an extra tab");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally
            {
                blankTabDrop = null;
                transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
                form.ExitApplication();
            }
        };
        Application.Run(form);
    }
}
