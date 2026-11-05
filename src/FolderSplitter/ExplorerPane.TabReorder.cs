namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    internal bool ReorderTab(FolderTab tab, int insertionIndex)
    {
        if (!CanReorderTab(tab, insertionIndex, out var group, out string reason))
        {
            SetLocalizedStatus(reason);
            return false;
        }
        var active = ActiveTab;
        int old = tabs.IndexOf(tab);
        tabs.RemoveAt(old);
        int destination = insertionIndex - (old < insertionIndex ? 1 : 0);
        tab.Group = group;
        tabs.Insert(destination, tab);
        if (tab == active && group is not null) group.Collapsed = false;
        FinishGroupMutation(active);
        return true;
    }

    internal bool ReorderGroup(TabGroup group, int insertionIndex)
    {
        var members = GroupMembers(group);
        if (members.Count == 0 || insertionIndex < 0 || insertionIndex > tabs.Count) return false;
        insertionIndex = GroupBoundary(insertionIndex);
        var active = ActiveTab;
        int start = tabs.IndexOf(members[0]);
        int destination = insertionIndex - Math.Clamp(insertionIndex - start, 0, members.Count);
        tabs.RemoveRange(start, members.Count);
        tabs.InsertRange(destination, members);
        FinishGroupMutation(active);
        return true;
    }

    private bool CanReorderTab(FolderTab tab, int insertionIndex, out TabGroup? group, out string reason)
    {
        group = null;
        reason = "This tab is no longer available.";
        if (!tabs.Contains(tab) || insertionIndex < 0 || insertionIndex > tabs.Count) return false;
        if (insertionIndex > 0 && insertionIndex < tabs.Count
            && tabs[insertionIndex - 1].Group is { } before && tabs[insertionIndex].Group == before)
            group = before;
        if (group is not null && tab.Group != group && GroupMembers(group).Count >= TabGroup.MaxTabs)
        {
            reason = "A group holds up to 12 tabs.";
            return false;
        }
        reason = "";
        return true;
    }

    // Whole-group drops choose a group boundary rather than splitting another group's members.
    private int GroupBoundary(int insertionIndex)
    {
        if (insertionIndex <= 0 || insertionIndex >= tabs.Count
            || tabs[insertionIndex - 1].Group is not { } group || tabs[insertionIndex].Group != group)
            return insertionIndex;
        int first = tabs.FindIndex(tab => tab.Group == group);
        int end = tabs.FindLastIndex(tab => tab.Group == group) + 1;
        return insertionIndex - first <= end - insertionIndex ? first : end;
    }

    internal TabDropPlan? FindStripDropPlan(Point screen, FolderTab? tab, TabGroup? group)
    {
        if (!tabStrip.RectangleToScreen(tabStrip.ClientRectangle).Contains(screen)) return null;
        if (tab is null && (group is null || !groupChips.ContainsKey(group))) return null;
        int x = tabStrip.PointToClient(screen).X;
        int insertion = tabs.Count;
        foreach (ToolStripItem item in tabStrip.Items)
        {
            if (!item.Available) continue;
            if (item is TabGroupChip chip)
            {
                int start = tabs.FindIndex(member => member.Group == chip.Group);
                if (x < item.Bounds.Left + item.Width / 2)
                {
                    insertion = start;
                    break;
                }
                if (x <= item.Bounds.Right)
                {
                    insertion = chip.Group.Collapsed ? tabs.FindLastIndex(member => member.Group == chip.Group) + 1 : start;
                    break;
                }
            }
            else if (item is FolderTabButton button)
            {
                int index = tabs.FindIndex(member => member.Button == button);
                if (x < item.Bounds.Left + item.Width / 2)
                {
                    insertion = index;
                    break;
                }
                if (x <= item.Bounds.Right)
                {
                    insertion = index + 1;
                    break;
                }
            }
        }
        if (group is not null) insertion = GroupBoundary(insertion);
        // A collapsed chip has no interior tab gaps, even when reached by an adjacent overflow edge.
        if (insertion > 0 && insertion < tabs.Count && tabs[insertion].Group is { Collapsed: true } collapsed
            && tabs[insertion - 1].Group == collapsed) insertion = GroupBoundary(insertion);
        bool allowed = tab is null || CanReorderTab(tab, insertion, out _, out _);
        string label = allowed ? text.T(group is null ? "Reorder tab" : "Reorder group") : text.T("A group holds up to 12 tabs.");
        if (!allowed) SetLocalizedStatus("A group holds up to 12 tabs.");
        int markerX = StripInsertionX(insertion);
        var point = tabStrip.PointToScreen(new Point(markerX, 2));
        var marker = new Rectangle(point.X - 2, point.Y, Math.Max(3, (int)Math.Round(3 * DeviceDpi / 96d)), Math.Max(1, tabStrip.Height - 4));
        return new TabDropPlan(PaneNumber - 1, null, marker, label, allowed, InsertionIndex: insertion);
    }

    private int StripInsertionX(int insertion)
    {
        int fallback = tabStrip.Padding.Left;
        foreach (ToolStripItem item in tabStrip.Items)
        {
            if (!item.Available) continue;
            if (item is TabGroupChip chip)
            {
                int start = tabs.FindIndex(tab => tab.Group == chip.Group);
                if (insertion <= start) return item.Bounds.Left;
                if (chip.Group.Collapsed && insertion == tabs.FindLastIndex(tab => tab.Group == chip.Group) + 1)
                    fallback = item.Bounds.Right;
            }
            else if (item is FolderTabButton button)
            {
                int index = tabs.FindIndex(tab => tab.Button == button);
                if (insertion <= index) return item.Bounds.Left;
                fallback = item.Bounds.Right;
            }
        }
        return Math.Min(fallback, newTab.Bounds.Left);
    }
}
