namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<ExplorerPane, TabGroup, int, string?>? GroupMoveDisabledReason { get; set; }
    internal bool CanInsertStripTabs(IReadOnlyList<FolderTab> moving, int insertionIndex, TabGroup? joinGroup,
        out TabGroup? group, out string reason)
    {
        group = joinGroup;
        reason = "This tab is no longer available.";
        if (moving.Count == 0 || insertionIndex < 0 || insertionIndex > tabs.Count) return false;
        if (group is null && insertionIndex > 0 && insertionIndex < tabs.Count
            && tabs[insertionIndex - 1].Group is { } before && tabs[insertionIndex].Group == before) group = before;
        if (group is not null)
        {
            var joining = group;
            int start = tabs.FindIndex(tab => tab.Group == joining);
            int end = tabs.FindLastIndex(tab => tab.Group == joining) + 1;
            if (start < 0 || insertionIndex < start || insertionIndex > end) return false;
        }
        if (tabs.Count + moving.Count(tab => !tabs.Contains(tab)) > 32)
        { reason = "Destination has 32 tabs.\nClose a tab first."; return false; }
        if (group is not null && GroupMembers(group).Count(tab => !moving.Contains(tab)) + moving.Count > TabGroup.MaxTabs)
        { reason = "A group holds up to 12 tabs."; return false; }
        reason = "";
        return true;
    }

    internal bool ReorderTabs(IReadOnlyList<FolderTab> selection, int insertionIndex, TabGroup? joinGroup = null,
        bool preservePinned = false)
    {
        var moving = tabs.Where(selection.Contains).ToArray();
        if (moving.Length != selection.Count)
        {
            SetLocalizedStatus("This tab is no longer available.");
            return false;
        }
        if (!CanInsertStripTabs(moving, insertionIndex, joinGroup, out var targetGroup, out string reason))
        {
            SetLocalizedStatus(reason);
            return false;
        }
        bool pin = targetGroup is null && (insertionIndex < PinnedCount
            || insertionIndex == PinnedCount && moving.All(tab => tab.Pinned));
        var wholeGroups = moving.Select(tab => tab.Group).OfType<TabGroup>().Distinct()
            .Where(group => GroupMembers(group).All(moving.Contains)).ToHashSet();
        var active = ActiveTab;
        int destination = insertionIndex - tabs.Take(insertionIndex).Count(moving.Contains);
        tabs.RemoveAll(moving.Contains);
        if (targetGroup is null && !pin) destination = GroupBoundary(destination);
        foreach (var member in moving)
        {
            bool keepPin = pin || preservePinned && member.Pinned && targetGroup is null;
            member.Pinned = keepPin;
            member.Group = keepPin ? null : targetGroup ?? (member.Group is { } original && wholeGroups.Contains(original) ? original : null);
        }
        tabs.InsertRange(destination, moving);
        if (active?.Group is { } activeGroup) activeGroup.Collapsed = false;
        FinishGroupMutation(active);
        return true;
    }

    internal bool InsertDroppedTabs(IReadOnlyList<FolderTab> moving, int insertionIndex, TabGroup? joinGroup)
    {
        if (!CanInsertStripTabs(moving, insertionIndex, joinGroup, out var group, out string reason))
        { SetLocalizedStatus(reason); return false; }
        var anchor = insertionIndex < tabs.Count ? tabs[insertionIndex] : null;
        var copied = AddTabBatch(moving, preserveGroups: moving.Count > 1);
        if (copied.Count != moving.Count) return false;
        LastDroppedTabs = copied;
        return ReorderTabs(copied, anchor is null ? tabs.Count : tabs.IndexOf(anchor), group, preservePinned: true);
    }

    internal bool ReorderTab(FolderTab tab, int insertionIndex, TabGroup? joinGroup = null)
    {
        if (!tabs.Contains(tab))
        {
            SetLocalizedStatus("This tab is no longer available.");
            return false;
        }
        if (!CanInsertStripTab(tab, insertionIndex, joinGroup, out var group, out string reason))
        {
            SetLocalizedStatus(reason);
            return false;
        }
        var active = ActiveTab;
        bool pin = group is null && (insertionIndex < PinnedCount || tab.Pinned && insertionIndex == PinnedCount);
        int old = tabs.IndexOf(tab);
        tabs.RemoveAt(old);
        int destination = insertionIndex - (old < insertionIndex ? 1 : 0);
        tab.Group = group;
        tab.Pinned = pin;
        if (pin) tab.Group = null;
        tabs.Insert(destination, tab);
        if (tab == active && group is not null) group.Collapsed = false;
        FinishGroupMutation(active);
        return true;
    }

    internal bool ReorderGroup(TabGroup group, int insertionIndex)
    {
        var members = GroupMembers(group);
        if (members.Count == 0 || insertionIndex < 0 || insertionIndex > tabs.Count) return false;
        insertionIndex = Math.Max(PinnedCount, GroupBoundary(insertionIndex));
        var active = ActiveTab;
        int start = tabs.IndexOf(members[0]);
        int destination = insertionIndex - Math.Clamp(insertionIndex - start, 0, members.Count);
        tabs.RemoveRange(start, members.Count);
        tabs.InsertRange(destination, members);
        FinishGroupMutation(active);
        return true;
    }

    private bool CanInsertStripTab(FolderTab tab, int insertionIndex, TabGroup? joinGroup,
        out TabGroup? group, out string reason)
    {
        group = joinGroup;
        reason = "This tab is no longer available.";
        if (insertionIndex < 0 || insertionIndex > tabs.Count) return false;
        if (group is null && insertionIndex > 0 && insertionIndex < tabs.Count
            && tabs[insertionIndex - 1].Group is { } before && tabs[insertionIndex].Group == before)
            group = before;
        if (group is not null)
        {
            var targetGroup = group;
            int start = tabs.FindIndex(member => member.Group == targetGroup);
            int end = tabs.FindLastIndex(member => member.Group == targetGroup) + 1;
            if (start < 0 || insertionIndex < start || insertionIndex > end) return false;
            if (tab.Group != group && GroupMembers(group).Count >= TabGroup.MaxTabs)
            {
                reason = "A group holds up to 12 tabs.";
                return false;
            }
        }
        if (!tabs.Contains(tab) && tabs.Count >= 32)
        {
            reason = "Destination has 32 tabs.\nClose a tab first.";
            return false;
        }
        reason = "";
        return true;
    }

    // The insertion index is relative to the receiving strip before the new tab exists.
    internal bool InsertDroppedTab(FolderTab source, int insertionIndex, TabGroup? joinGroup)
    {
        if (!CanInsertStripTab(source, insertionIndex, joinGroup, out var group, out string reason))
        {
            SetLocalizedStatus(reason);
            return false;
        }
        var tab = InsertTab(source.Host?.LocationPath, select: false, countVisit: false, group: group);
        if (tab is null) return false;
        var active = ActiveTab;
        tabs.Remove(tab);
        tab.Pinned = group is null && (source.Pinned || insertionIndex < PinnedCount);
        tabs.Insert(insertionIndex, tab);
        FinishGroupMutation(active);
        SelectTab(tabs.IndexOf(tab));
        return true;
    }

    internal bool MoveCurrentTab(int direction)
    {
        if (ActiveTab is not { } tab || direction == 0) return false;
        direction = Math.Sign(direction);
        int index = tabs.IndexOf(tab);
        int adjacent = index + direction;
        if (tab.Group is { } currentGroup
            && (adjacent < 0 || adjacent >= tabs.Count || tabs[adjacent].Group != currentGroup))
        {
            // Leaving a group crosses its chip/underline boundary, not the adjacent tab.
            tab.Group = null;
            FinishGroupMutation(tab);
            return true;
        }
        if (adjacent < 0 || adjacent >= tabs.Count) return false;
        if (tab.Group is null && tabs[adjacent].Group is { } nextGroup)
        {
            if (GroupMembers(nextGroup).Count >= TabGroup.MaxTabs)
            {
                SetLocalizedStatus("A group holds up to 12 tabs.");
                return false;
            }
            tab.Pinned = false;
            tab.Group = nextGroup;
            nextGroup.Collapsed = false;
        }
        else
        {
            tabs.RemoveAt(index);
            tabs.Insert(adjacent, tab);
        }
        FinishGroupMutation(tab);
        return true;
    }

    // Whole-group drops choose a group boundary rather than splitting another group's members.
    private int GroupBoundary(int insertionIndex)
    {
        insertionIndex = Math.Max(PinnedCount, insertionIndex);
        if (insertionIndex <= 0 || insertionIndex >= tabs.Count
            || tabs[insertionIndex - 1].Group is not { } group || tabs[insertionIndex].Group != group)
            return insertionIndex;
        int first = tabs.FindIndex(tab => tab.Group == group);
        int end = tabs.FindLastIndex(tab => tab.Group == group) + 1;
        return insertionIndex - first <= end - insertionIndex ? first : end;
    }

    internal TabDropPlan? FindStripDropPlan(Point screen, FolderTab? tab, TabGroup? group,
        IReadOnlyList<FolderTab>? movingTabs = null, IReadOnlyList<string>? externalPaths = null, int minimumIndex = 0)
    {
        if (!tabStrip.RectangleToScreen(tabStrip.ClientRectangle).Contains(screen)) return null;
        if (tab is null && group is null && externalPaths is null) return null;
        int x = tabStrip.PointToClient(screen).X;
        int insertion = 0;
        TabGroup? joinGroup = null;
        foreach (ToolStripItem item in tabStrip.Items)
        {
            if (!item.Available) continue;
            if (item is TabGroupChip chip)
            {
                int start = tabs.FindIndex(member => member.Group == chip.Group);
                if (x < item.Bounds.Left + item.Width / 2)
                {
                    joinGroup = null;
                    insertion = start;
                    break;
                }
                insertion = chip.Group.Collapsed ? tabs.FindLastIndex(member => member.Group == chip.Group) + 1 : start;
                joinGroup = chip.Group.Collapsed ? null : chip.Group;
                if (x <= item.Bounds.Right) break;
            }
            else if (item is FolderTabButton button)
            {
                int index = tabs.FindIndex(member => member.Button == button);
                if (x < item.Bounds.Left + item.Width / 2)
                {
                    insertion = index;
                    break;
                }
                joinGroup = null;
                insertion = index + 1;
                if (x <= item.Bounds.Right) break;
            }
        }
        if (group is not null)
        {
            insertion = GroupBoundary(insertion);
            joinGroup = null;
        }
        // A collapsed chip has no interior tab gaps, even when reached by an adjacent overflow edge.
        if (insertion > 0 && insertion < tabs.Count && tabs[insertion].Group is { Collapsed: true } collapsed
            && tabs[insertion - 1].Group == collapsed) insertion = GroupBoundary(insertion);
        insertion = Math.Clamp(insertion, Math.Clamp(minimumIndex, 0, tabs.Count), tabs.Count);
        string reason = "";
        bool allowed = tab is null || (movingTabs is { Count: > 1 }
            ? CanInsertStripTabs(movingTabs, insertion, joinGroup, out joinGroup, out reason)
            : CanInsertStripTab(tab, insertion, joinGroup, out joinGroup, out reason));
        if (!allowed) SetLocalizedStatus(reason);
        string label = allowed ? text.T(group is not null ? "Reorder group" : movingTabs is { Count: > 1 } ? "Reorder tabs" : "Reorder tab") : text.T(reason);
        int markerX = StripInsertionX(insertion, joinGroup);
        var point = tabStrip.PointToScreen(new Point(markerX, 2));
        var marker = new Rectangle(point.X - 2, point.Y, Math.Max(3, (int)Math.Round(3 * DeviceDpi / 96d)), Math.Max(1, tabStrip.Height - 4));
        if (externalPaths is not null) return PlanExternalInsertion(insertion, joinGroup, externalPaths, marker);
        return new TabDropPlan(PaneNumber - 1, null, marker, label, allowed, InsertionIndex: insertion, JoinGroup: joinGroup);
    }

    private int StripInsertionX(int insertion, TabGroup? joinGroup)
    {
        int fallback = tabStrip.Padding.Left;
        foreach (ToolStripItem item in tabStrip.Items)
        {
            if (!item.Available) continue;
            if (item is TabGroupChip chip)
            {
                int start = tabs.FindIndex(tab => tab.Group == chip.Group);
                if (insertion <= start)
                    return insertion == start && joinGroup == chip.Group ? item.Bounds.Right : item.Bounds.Left;
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
