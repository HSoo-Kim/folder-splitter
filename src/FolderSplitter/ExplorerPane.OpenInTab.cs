using System.Runtime.InteropServices;
using System.Text;

namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    private FolderTab? tabOpener;
    private readonly List<FolderTab> openerRunTabs = [];
    private TabDropPlan? externalDropPlan;
    private string[] externalDropPaths = [];
    private System.Runtime.InteropServices.ComTypes.IDataObject? externalDropData;
    private string? externalDropDescription;
    private bool externalDropAllowed;
    private FolderTab? dragHoverTab;
    private readonly System.Windows.Forms.Timer dragHoverTimer = new() { Interval = 600 };

    // The minimum keeps related tabs outside a pinned prefix without changing strip reorder rules.
    internal void OpenPathsInNewTabs(IReadOnlyList<string> paths, bool foreground, int minimumIndex = 0)
    {
        minimumIndex = Math.Max(minimumIndex, PinnedCount);
        var opener = ActiveTab;
        if (tabOpener != opener) { tabOpener = opener; openerRunTabs.Clear(); }
        for (int i = openerRunTabs.Count - 1; i >= 0; i--)
            if (!tabs.Contains(openerRunTabs[i])) openerRunTabs.RemoveAt(i);
        FolderTab? first = null;
        foreach (string path in paths)
        {
            int insertion = Math.Max(minimumIndex, opener is null ? tabs.Count : tabs.IndexOf(opener) + 1);
            foreach (var child in openerRunTabs) insertion = Math.Max(insertion, tabs.IndexOf(child) + 1);
            TabGroup? group = opener?.Group;
            int groupCount = group is null ? 0 : CountOpenGroupTabs(group);
            if (group is not null && groupCount < TabGroup.MaxTabs)
                insertion = Math.Min(insertion, tabs.FindLastIndex(tab => tab.Group == group) + 1);
            if (group is not null && groupCount >= TabGroup.MaxTabs)
            {
                insertion = Math.Max(insertion, tabs.FindLastIndex(tab => tab.Group == group) + 1);
                group = null;
            }
            var added = InsertTab(path, select: false, group: group, insertionIndex: Math.Clamp(insertion, 0, tabs.Count));
            if (added is null) break;
            first ??= added;
            openerRunTabs.Add(added);
        }
        if (foreground && first is not null) SelectTab(tabs.IndexOf(first));
    }

    private int CountOpenGroupTabs(TabGroup group)
    {
        int count = 0;
        foreach (var tab in tabs) if (tab.Group == group) count++;
        return count;
    }

    private void BindFolderNavigationTarget(ToolStripItem item, string path)
    {
        item.Click += (_, _) =>
        {
            if ((ModifierKeys & Keys.Control) != 0) OpenPathsInNewTabs([path], (ModifierKeys & Keys.Shift) != 0);
            else Navigate(path);
        };
        item.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Middle)
                OpenPathsInNewTabs([path], (ModifierKeys & (Keys.Control | Keys.Shift)) == (Keys.Control | Keys.Shift));
        };
    }

    private void InitializeExternalTabDrops()
    {
        InitializeGroupDropDwell();
        dragHoverTimer.Tick += (_, _) =>
        {
            dragHoverTimer.Stop();
            if (dragHoverTab is { } tab && tabs.Contains(tab)) SelectTab(tabs.IndexOf(tab), focus: false);
        };
        foreach (Control target in new Control[] { tabStrip, emptyHint, contents })
        {
            target.AllowDrop = true;
            target.DragEnter += ExternalDragEnter;
            target.DragOver += ExternalDragOver;
            target.DragLeave += (_, _) => ClearExternalDrop();
            target.DragDrop += ExternalDragDrop;
        }
        newTab.AllowDrop = true;
        newTab.DragEnter += ExternalDragEnter;
        newTab.DragOver += ExternalDragOver;
        newTab.DragLeave += (_, _) => ClearExternalDrop();
        newTab.DragDrop += ExternalDragDrop;
        tabStrip.Paint += (_, e) =>
        {
            if (externalDropPlan is not { } plan) return;
            TabDockOverlay.PaintStripFeedback(e.Graphics, plan, tabStrip.PointToScreen(Point.Empty), false);
        };
    }

    private void ExternalDragEnter(object? sender, DragEventArgs e)
    {
        ClearExternalDrop();
        externalDropPaths = ShellDropPaths.Read(e.Data);
        if (externalDropPaths.Length != 0)
            externalDropData = e.Data as System.Runtime.InteropServices.ComTypes.IDataObject;
        ExternalDragOver(sender, e);
    }

    private void ExternalDragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        if (externalDropPaths.Length == 0 || e.Data?.GetDataPresent(TabDockOverlay.DragFormat, false) == true) return;
        var point = new Point(e.X, e.Y);
        bool strip = tabStrip.RectangleToScreen(tabStrip.ClientRectangle).Contains(point);
        if (!strip) ClearStripDropDwell();
        var plan = strip ? FindStripDropPlan(point, null, null, externalPaths: externalDropPaths, minimumIndex: PinnedCount)
            : PlanExternalInsertion(tabs.Count, null, externalDropPaths, Rectangle.Empty);
        externalDropPlan = strip ? plan : null;
        tabStrip.Invalidate();
        if (plan is { Allowed: true }) e.Effect = DragDropEffects.Link;
        externalDropAllowed = plan is { Allowed: true };
        RefreshExternalDropDescription();
        FolderTab? hovered = strip && tabStrip.GetItemAt(tabStrip.PointToClient(point)) is FolderTabButton button
            ? tabs.FirstOrDefault(tab => tab.Button == button) : null;
        if (hovered != dragHoverTab)
        {
            dragHoverTimer.Stop();
            dragHoverTab = hovered;
            if (hovered is not null && hovered != ActiveTab) dragHoverTimer.Start();
        }
    }

    internal string JoinDropLabel(TabGroup? group) => group is null ? text.T("Open in new tabs")
        : group.Name.Length == 0 ? text.T("Add to group") : text.T("Add to '{0}' group", group.Name);

    private void RefreshExternalDropDescription()
    {
        if (externalDropData is null) return;
        string? description = externalDropAllowed ? JoinDropLabel(externalDropPlan?.JoinGroup) : null;
        if (description == externalDropDescription) return;
        ShellDropDescription.Set(externalDropData, description);
        externalDropDescription = description;
    }

    private TabDropPlan PlanExternalInsertion(int insertion, TabGroup? group, IReadOnlyList<string> paths, Rectangle marker)
    {
        if (group is null && insertion > 0 && insertion < tabs.Count
            && tabs[insertion - 1].Group is { } before && tabs[insertion].Group == before) group = before;
        string? reason = tabs.Count + paths.Count > 32 ? "Maximum 32 tabs per pane. Close a tab first."
            : group is not null && CountOpenGroupTabs(group) + paths.Count > TabGroup.MaxTabs ? "A group holds up to 12 tabs." : null;
        if (reason is not null) SetLocalizedStatus(reason);
        return new TabDropPlan(PaneNumber - 1, null, marker, reason is null ? JoinDropLabel(group) : text.T(reason), reason is null,
            InsertionIndex: insertion, JoinGroup: group);
    }

    private void ExternalDragDrop(object? sender, DragEventArgs e)
    {
        ExternalDragOver(sender, e);
        if (e.Effect == DragDropEffects.Link)
        {
            var plan = externalDropPlan ?? PlanExternalInsertion(tabs.Count, null, externalDropPaths, Rectangle.Empty);
            if (plan is { Allowed: true, InsertionIndex: int insertion })
            {
                FolderTab? first = null;
                foreach (string path in externalDropPaths)
                {
                    var added = InsertTab(path, select: false, group: plan.JoinGroup, insertionIndex: insertion++);
                    if (added is null) break;
                    first ??= added;
                }
                if (first is not null && plan.JoinGroup?.Collapsed != true) SelectTab(tabs.IndexOf(first));
            }
        }
        ClearExternalDrop();
    }

    private void ClearExternalDrop()
    {
        if (externalDropData is not null) ShellDropDescription.Set(externalDropData, null);
        externalDropData = null;
        externalDropDescription = null;
        externalDropAllowed = false;
        externalDropPlan = null;
        ClearStripDropDwell();
        dragHoverTimer.Stop();
        dragHoverTab = null;
        externalDropPaths = [];
        tabStrip.Invalidate();
        tabStrip.Update();
    }
}

internal static class ShellDropPaths
{
    internal static string[] Read(IDataObject? data)
    {
        if (data is null || data.GetDataPresent(TabDockOverlay.DragFormat, false)) return [];
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] paths) return Existing(paths);
            switch (data.GetData("FileNameW"))
            {
                case string name: return Existing([name]);
                case string[] names: return Existing(names);
                case Stream fileName:
                    return Existing(Encoding.Unicode.GetString(ReadStream(fileName)).Split('\0', StringSplitOptions.RemoveEmptyEntries));
            }
            if (data.GetData("Shell IDList Array") is not Stream idLists) return [];
            byte[] cida = ReadStream(idLists);
            if (cida.Length < 8) return [];
            uint count = BitConverter.ToUInt32(cida, 0);
            if (count > (cida.Length - 8) / 4) return [];
            int parent = checked((int)BitConverter.ToUInt32(cida, 4));
            if (!ValidIdList(cida, parent)) return [];
            var result = new List<string>();
            var handle = GCHandle.Alloc(cida, GCHandleType.Pinned);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int offset = checked((int)BitConverter.ToUInt32(cida, 8 + i * 4));
                    if (!ValidIdList(cida, offset)) continue;
                    nint full = ShellNative.ILCombine(handle.AddrOfPinnedObject() + parent, handle.AddrOfPinnedObject() + offset);
                    try
                    {
                        if (ShellNative.Name(full, 0x80058000) is { } path) result.Add(path);
                    }
                    finally { if (full != 0) ShellNative.ILFree(full); }
                }
            }
            finally { handle.Free(); }
            return Existing(result);
        }
        catch (Exception error) when (error is COMException or IOException or ArgumentException or OverflowException)
        { return []; }
    }

    private static byte[] ReadStream(Stream stream)
    {
        long position = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek) stream.Position = 0;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        finally { if (stream.CanSeek) stream.Position = position; }
    }

    private static string[] Existing(IEnumerable<string> paths) => paths.Where(path => Directory.Exists(path) || File.Exists(path)).ToArray();

    private static bool ValidIdList(byte[] bytes, int offset)
    {
        while (offset >= 0 && offset <= bytes.Length - 2)
        {
            int length = BitConverter.ToUInt16(bytes, offset);
            if (length == 0) return true;
            if (length < 2) return false;
            offset += length;
        }
        return false;
    }
}
