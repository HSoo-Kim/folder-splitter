using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class FolderSplitterApplicationContext
{
    private sealed record FileSelectionSnapshot(int Count, HashSet<string> Paths);

    // Only views explicitly Ctrl-clicked in this selection session participate. A selection
    // left over in another pane must never be swept into an unrelated file operation.
    private readonly Dictionary<ShellViewHost, FileSelectionSnapshot> crossViewFileSelections = [];
    private bool crossViewFileDragging;
    internal Func<nint, nint, (int Result, uint Effect)>? CrossViewFileDragOverride { get; set; }

    internal void CrossViewFileMouseDown(ShellViewHost source, bool control, bool selectedItem)
    {
        if (crossViewFileDragging || control) return;
        // A plain click on an already selected item may become the start of a drag. Wait for
        // button-up before clearing; a plain click elsewhere starts an unrelated selection.
        if (!selectedItem || crossViewFileSelections.Count < 2 || !crossViewFileSelections.ContainsKey(source))
            ClearCrossViewFileSelection();
    }

    internal void CrossViewFileMouseUp(ShellViewHost source, bool controlClick)
    {
        if (crossViewFileDragging) return;
        if (controlClick) RecordCrossViewFileSelection(source);
        else ClearCrossViewFileSelection();
    }

    internal void RecordCrossViewFileSelection(ShellViewHost source)
    {
        if (!IsVisibleFileView(source)) { RemoveCrossViewFileSelection(source); return; }
        var (paths, count) = ReadCrossViewFileSelection(source);
        if (count == 0) { RemoveCrossViewFileSelection(source); return; }
        if (!crossViewFileSelections.ContainsKey(source))
        {
            source.Disposed += CrossViewFileHostClosed;
            source.VisibleChanged += CrossViewFileHostVisibilityChanged;
        }
        crossViewFileSelections[source] = new FileSelectionSnapshot(count, new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase));
    }

    private static (IReadOnlyList<string> Paths, int Count) ReadCrossViewFileSelection(ShellViewHost source)
    {
        try { return source.SelectedPaths(); }
        catch (Exception error) when (error is COMException or InvalidOperationException) { return ([], -1); }
    }

    private void CrossViewFileHostClosed(object? sender, EventArgs e)
    {
        if (sender is ShellViewHost source) RemoveCrossViewFileSelection(source);
    }

    private void CrossViewFileHostVisibilityChanged(object? sender, EventArgs e)
    {
        if (sender is ShellViewHost { Visible: false } source) RemoveCrossViewFileSelection(source);
    }

    private void RemoveCrossViewFileSelection(ShellViewHost source)
    {
        if (!crossViewFileSelections.Remove(source)) return;
        source.Disposed -= CrossViewFileHostClosed;
        source.VisibleChanged -= CrossViewFileHostVisibilityChanged;
    }

    internal void ClearCrossViewFileSelection()
    {
        foreach (var source in crossViewFileSelections.Keys)
        {
            source.Disposed -= CrossViewFileHostClosed;
            source.VisibleChanged -= CrossViewFileHostVisibilityChanged;
        }
        crossViewFileSelections.Clear();
    }

    private bool IsVisibleFileView(ShellViewHost source)
    {
        if (source.IsDisposed || source.Disposing || !source.Visible || source.FindForm() is not MainForm window
            || window.IsDisposed || !window.Visible || window.WindowState == FormWindowState.Minimized
            || !windows.Contains(window)) return false;
        foreach (int number in window.CurrentLayout.GetPanes())
            if (ReferenceEquals(window.GetPane(number).ActiveView, source)) return true;
        return false;
    }

    private static void CrossViewFileDragFailed(ShellViewHost source)
    {
        for (Control? control = source; control is not null; control = control.Parent)
            if (control is ExplorerPane pane)
            {
                pane.SetLocalizedStatus("Could not drag all selected files and folders. Nothing was moved. Select them again.");
                return;
            }
    }

    internal bool TryStartCrossViewFileDrag(ShellViewHost source, nint list)
    {
        if (crossViewFileDragging || crossViewFileSelections.Count < 2
            || !crossViewFileSelections.ContainsKey(source) || !IsVisibleFileView(source)) return false;

        var views = new List<ShellViewHost>(crossViewFileSelections.Count) { source };
        foreach (var candidate in crossViewFileSelections.Keys)
            if (!ReferenceEquals(candidate, source) && IsVisibleFileView(candidate)) views.Add(candidate);
        if (views.Count < 2) return false;

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int sourceCount = 0;
        foreach (var view in views)
        {
            crossViewFileSelections.TryGetValue(view, out var recorded);
            var (selected, count) = ReadCrossViewFileSelection(view);
            // A view or its selection can change while the Shell supplies selection data.
            // Never silently drag a partial or no-longer-current multi-view selection.
            if (recorded is null || !IsVisibleFileView(view)
                || !crossViewFileSelections.TryGetValue(view, out var current) || !ReferenceEquals(recorded, current)
                || count != recorded.Count || selected.Count != count || !recorded.Paths.SetEquals(selected))
            {
                CrossViewFileDragFailed(source);
                ClearCrossViewFileSelection();
                return true;
            }
            foreach (string path in selected) if (seen.Add(path)) paths.Add(path);
            if (ReferenceEquals(view, source)) sourceCount = paths.Count;
        }
        if (paths.Count == sourceCount) return false; // Other views selected only the same items.
        if (!CrossViewFileDragData.TryCreate(paths, out nint data))
        {
            CrossViewFileDragFailed(source);
            ClearCrossViewFileSelection();
            return true;
        }

        crossViewFileDragging = true;
        try
        {
            int result;
            uint effect;
            if (CrossViewFileDragOverride is { } operation) (result, effect) = operation(list, data);
            else result = CrossViewFileDragData.DoDragDrop(list, data, out effect);
            if (result < 0) CrossViewFileDragFailed(source);
            else if ((effect & 2) != 0) ClearCrossViewFileSelection(); // Moved items may no longer exist at their source.
        }
        finally
        {
            crossViewFileDragging = false;
            Marshal.Release(data);
        }
        return true;
    }
}
