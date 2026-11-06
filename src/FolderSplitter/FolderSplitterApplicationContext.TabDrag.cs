using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class FolderSplitterApplicationContext
{
    private TabDragSession? tabDrag;

    private sealed class TabDragSession(MainForm window, ExplorerPane pane, ExplorerPane.FolderTab? tab,
        TabGroup? group, IReadOnlyList<ExplorerPane.FolderTab> tabs)
    {
        internal MainForm Window { get; } = window;
        internal ExplorerPane Pane { get; } = pane;
        internal ExplorerPane.FolderTab? Tab { get; } = tab;
        internal TabGroup? Group { get; } = group;
        internal IReadOnlyList<ExplorerPane.FolderTab> Tabs { get; } = tabs;
        internal Dictionary<MainForm, TabDockOverlay> Overlays { get; } = [];
        internal Point? ReleasePoint { get; set; }
        internal bool Cancelled { get; set; }
    }

    internal void StartTabDrag(MainForm sourceWindow, ExplorerPane source, ExplorerPane.FolderTab? tab,
        TabGroup? group, IReadOnlyList<ExplorerPane.FolderTab>? selection)
    {
        if (tabDrag is not null || saving || shuttingDown || disposed) return;
        var drag = new TabDragSession(sourceWindow, source, tab, group,
            (selection ?? source.GroupMembers(group!)).ToArray());
        if (!ValidDragSource(drag)) return;
        tabDrag = drag;
        var data = new DataObject();
        data.SetData(TabDockOverlay.DragFormat, false, group is null ? "tab" : "group");
        using var timer = new System.Windows.Forms.Timer { Interval = 60 };
        timer.Tick += (_, _) => RefreshDragOverlays(drag);
        DragDropEffects effect;
        TabDropPlan? result;
        try
        {
            RefreshDragOverlays(drag);
            timer.Start();
            effect = sourceWindow.RunTabDrag(data);
            result = drag.Overlays.Values.Select(overlay => overlay.Result).FirstOrDefault(drop => drop is not null);
        }
        finally
        {
            timer.Stop();
            foreach (var overlay in drag.Overlays.Values) overlay.Dispose();
            tabDrag = null;
        }
        if (drag.Cancelled || !ValidDragSource(drag)) return;
        if (effect == DragDropEffects.Move && result is { Allowed: true, TargetWindow: { } target }
            && windows.Contains(target) && target.Visible && target.WindowState != FormWindowState.Minimized)
        {
            if (target == sourceWindow) CommitLocalDrag(drag, result);
            else CommitCrossWindowDrag(drag, target, result);
        }
        // OLE's None is not proof of a desktop release. It must follow an explicit left-button
        // release, and a foreign target that accepted the drag owns its non-None result instead.
        else if (effect == DragDropEffects.None && drag.ReleasePoint is { } point
            && !windows.Any(window => EligibleDragWindow(window) && window.Bounds.Contains(point))
            && !Application.OpenForms.Cast<Form>().Any(form => form.Visible && form is not QuadExplorer.MainForm
                && form.Bounds.Contains(point)))
        {
            if (group is not null) MoveGroupToNewWindow(sourceWindow, source, group, point);
            else MoveTabsToNewWindow(sourceWindow, source, drag.Tabs, point);
        }
    }

    private bool ValidDragSource(TabDragSession drag) => !disposed && !shuttingDown
        && windows.Contains(drag.Window) && !drag.Window.IsDisposed
        && drag.Window.GetPane(drag.Pane.PaneNumber - 1) == drag.Pane && drag.Pane.Visible
        && drag.Tabs.Count > 0 && drag.Tabs.All(tab => drag.Pane.IndexOfTab(tab) >= 0)
        && (drag.Group is null || drag.Pane.GroupMembers(drag.Group).SequenceEqual(drag.Tabs));

    internal void ContinueTabDrag(QueryContinueDragEventArgs e, Point screen)
    {
        if (tabDrag is not { } drag) return;
        if (e.EscapePressed || e.Action == DragAction.Cancel || !ValidDragSource(drag))
        {
            drag.Cancelled = true;
            drag.ReleasePoint = null;
            e.Action = DragAction.Cancel;
        }
        else if ((e.KeyState & 1) == 0 && e.Action == DragAction.Drop)
            drag.ReleasePoint = screen;
        else if (e.Action == DragAction.Continue
            && !windows.Any(window => EligibleDragWindow(window) && window.Bounds.Contains(screen)))
            drag.Pane.SetLocalizedStatus("New window");
    }

    private static bool EligibleDragWindow(MainForm window) => !window.IsDisposed && window.Visible
        && window.WindowState != FormWindowState.Minimized;

    private void RefreshDragOverlays(TabDragSession drag)
    {
        foreach (var pair in drag.Overlays.ToArray())
            if (!windows.Contains(pair.Key) || !EligibleDragWindow(pair.Key))
            { pair.Value.Dispose(); drag.Overlays.Remove(pair.Key); }
        foreach (var window in windows.ToArray())
        {
            if (!EligibleDragWindow(window)) continue;
            if (drag.Overlays.TryGetValue(window, out var existing))
                existing.RefreshBounds(window.DragBounds, window.DragPaneBounds);
            else
            {
                var overlay = new TabDockOverlay(window.DragBounds, window.DragPaneBounds,
                    point => FindDragPlan(drag, window, point), text, window.ClearStripDropDwell);
                window.BindDragOverlay(overlay);
                drag.Overlays.Add(window, overlay);
                overlay.Show(window);
            }
        }
    }

    private TabDropPlan? FindDragPlan(TabDragSession drag, MainForm window, Point point)
    {
        if (!ValidDragSource(drag) || BrowserAt(point, drag) != window)
        {
            window.ClearStripDropDwell();
            return null;
        }
        return window.FindStripOrDockPlan(point, drag.Pane, drag.Tab, drag.Group, drag.Tabs)
            is { } drop ? drop with { TargetWindow = window } : null;
    }

    // Enumerate the real desktop stack rather than registration order. Owned overlays are only
    // transport surfaces; foreign windows and our dialogs must continue to occlude browser panes.
    private MainForm? BrowserAt(Point point, TabDragSession drag)
    {
        for (nint handle = GetTopWindow(0); handle != 0; handle = GetWindow(handle, 2))
        {
            if (!IsWindowVisible(handle) || IsIconic(handle)
                || drag.Overlays.Values.Any(overlay => !overlay.IsDisposed && overlay.Handle == handle)) continue;
            if (DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (!GetWindowRect(handle, out var bounds)
                || point.X < bounds.Left || point.X >= bounds.Right || point.Y < bounds.Top || point.Y >= bounds.Bottom) continue;
            return windows.FirstOrDefault(window => EligibleDragWindow(window) && window.Handle == handle);
        }
        return null;
    }

    private static void CommitLocalDrag(TabDragSession drag, TabDropPlan drop)
    {
        var source = drag.Pane;
        if (drop.InsertionIndex is { } insertion && drag.Window.GetPane(drop.TargetPane) == source)
        {
            if (drag.Group is { } group) source.ReorderGroup(group, insertion);
            else source.ReorderTabs(drag.Tabs, insertion, drop.JoinGroup);
        }
        else if (drag.Group is { } group)
            drag.Window.DockGroup(source, group, drop.TargetPane, drop.Edge, drop.InsertionIndex);
        else drag.Window.DockTabs(source, drag.Tabs, drop.TargetPane, drop.Edge, drop.InsertionIndex, drop.JoinGroup);
    }

    private void CommitCrossWindowDrag(TabDragSession drag, MainForm target, TabDropPlan drop)
    {
        if (saving || !ValidDragSource(drag)) return;
        bool moved = false;
        saving = true;
        try
        {
            // OLE pumps arbitrary UI messages. ReceiveDrag repeats capacity/identity checks and
            // populates the destination completely before the transfer path removes source tabs.
            if (!target.ReceiveDrag(drag.Pane, drag.Tabs, drag.Group, drop)) return;
            transferringSource = drag.Window;
            drag.Window.CompleteWindowTransfer(drag.Pane, drag.Tabs);
            target.Activate();
            target.SetApplicationStatus("Move to window");
            moved = true;
        }
        finally { transferringSource = null; saving = false; }
        if (moved) SaveSession();
    }

    private static Rectangle TearOffBounds(MainForm source, Rectangle bounds, Point point)
    {
        Rectangle work = Screen.FromPoint(point).WorkingArea;
        nint monitor = MonitorFromPoint(point, 2);
        int dpi = GetDpiForMonitor(monitor, 0, out uint monitorDpi, out _) == 0 ? (int)monitorDpi : source.DeviceDpi;
        return BrowserWindowGeometry.TearOff(bounds, point, work, source.DeviceDpi, dpi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragWindowRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out DragWindowRect bounds);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
}
