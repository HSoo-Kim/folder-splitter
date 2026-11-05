namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    private readonly HashSet<FolderTab> selectedTabs = [];
    private FolderTab? selectionAnchor;
    internal IReadOnlyList<FolderTab> SelectedTabs => tabs.Where(tab => selectedTabs.Contains(tab) || tab == ActiveTab).ToArray();
    internal IReadOnlyList<FolderTab> LastReopenedTabs { get; private set; } = [];
    internal IReadOnlyList<FolderTab> LastDroppedTabs { get; private set; } = [];
    internal bool SuppressSavedGroupSync { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, bool>? SavedGroupExists { get; set; }

    private string? ExistingSavedId(string? id) => id is not null && SavedGroupExists?.Invoke(id) == false ? null : id;
    internal event Action<ExplorerPane, IReadOnlyList<FolderTab>, int>? MoveTabsRequested;
    internal event Action<ExplorerPane, IReadOnlyList<FolderTab>>? MoveTabsToWindowRequested;
    internal event Action<ExplorerPane, TabGroup>? SaveGroupRequested;

    internal IReadOnlyList<FolderTab> SelectionForDrag(FolderTab tab) =>
        selectedTabs.Contains(tab) ? SelectedTabs : [tab];

    internal void ClearTabSelection()
    {
        selectedTabs.Clear();
        if (ActiveTab is { } active) selectedTabs.Add(active);
        UpdateTabSelectionAppearance();
    }

    private void UpdateTabSelectionAppearance()
    {
        selectedTabs.RemoveWhere(tab => !tabs.Contains(tab));
        if (ActiveTab is { } active) selectedTabs.Add(active);
        foreach (var tab in tabs)
        {
            tab.Button.MultiSelected = selectedTabs.Contains(tab);
            tab.Button.ForeColor = selectedTabs.Contains(tab) ? Theme.Text : Theme.Muted;
        }
    }

    internal void SelectTabWithModifiers(FolderTab tab, Keys modifiers)
    {
        if (!tabs.Contains(tab)) return;
        if ((modifiers & Keys.Shift) != 0)
        {
            int anchor = tabs.IndexOf(selectionAnchor is { } saved && tabs.Contains(saved) ? saved : ActiveTab ?? tab);
            int target = tabs.IndexOf(tab);
            selectedTabs.Clear();
            for (int i = Math.Min(anchor, target); i <= Math.Max(anchor, target); i++)
                if (tabs[i].Group?.Collapsed != true) selectedTabs.Add(tabs[i]);
            SelectTab(target, preserveSelection: true);
        }
        else if ((modifiers & Keys.Control) != 0)
        {
            selectionAnchor = tab;
            if (selectedTabs.Contains(tab))
            {
                if (tab != ActiveTab) selectedTabs.Remove(tab);
                UpdateTabSelectionAppearance();
            }
            else
            {
                selectedTabs.Add(tab);
                SelectTab(tabs.IndexOf(tab), preserveSelection: true);
            }
        }
        else SelectTab(tabs.IndexOf(tab));
    }

    internal TabGroup? CreateGroup(IReadOnlyList<FolderTab> selection)
    {
        var members = tabs.Where(selection.Contains).ToArray();
        if (members.Length == 0) return null;
        if (members.Length > TabGroup.MaxTabs) { SetLocalizedStatus("A group holds up to 12 tabs."); return null; }
        var used = Groups.Select(group => group.Color).ToHashSet();
        var colors = Enum.GetValues<TabGroupColor>();
        var group = new TabGroup { Color = colors.FirstOrDefault(color => !used.Contains(color), colors[Groups.Count % colors.Length]) };
        var active = ActiveTab;
        int insertion = tabs.IndexOf(members[0]);
        tabs.RemoveAll(members.Contains);
        // A partial old group must remain one run rather than straddling the new chip.
        if (insertion > 0 && insertion < tabs.Count && tabs[insertion - 1].Group is { } before && tabs[insertion].Group == before)
            insertion = tabs.FindLastIndex(tab => tab.Group == before) + 1;
        foreach (var member in members) member.Group = group;
        tabs.InsertRange(insertion, members);
        FinishGroupMutation(active);
        return group;
    }

    internal bool AddToGroup(IReadOnlyList<FolderTab> selection, TabGroup group)
    {
        var members = tabs.Where(selection.Contains).ToArray();
        if (!Groups.Contains(group) || members.Length == 0) return false;
        if (members.All(tab => tab.Group == group)) return true;
        if (GroupMembers(group).Count + members.Count(tab => tab.Group != group) > TabGroup.MaxTabs)
        { SetLocalizedStatus("A group holds up to 12 tabs."); return false; }
        var active = ActiveTab;
        int originalStart = tabs.FindIndex(tab => tab.Group == group);
        tabs.RemoveAll(members.Contains);
        int insertion = tabs.FindLastIndex(tab => tab.Group == group) + 1;
        if (insertion == 0) insertion = Math.Min(originalStart, tabs.Count);
        foreach (var member in members) member.Group = group;
        tabs.InsertRange(insertion, members);
        if (members.Contains(active)) group.Collapsed = false;
        FinishGroupMutation(active);
        return true;
    }

    internal void RemoveFromGroup(IReadOnlyList<FolderTab> selection)
    {
        var active = ActiveTab;
        foreach (var group in selection.Select(tab => tab.Group).OfType<TabGroup>().Distinct().ToArray())
        {
            var members = tabs.Where(tab => tab.Group == group && selection.Contains(tab)).ToArray();
            int first = tabs.FindIndex(tab => tab.Group == group);
            tabs.RemoveAll(members.Contains);
            int last = tabs.FindLastIndex(tab => tab.Group == group);
            foreach (var member in members) member.Group = null;
            tabs.InsertRange(last < 0 ? Math.Min(first, tabs.Count) : last + 1, members);
        }
        FinishGroupMutation(active);
    }

    internal void CloseTabs(IReadOnlyList<FolderTab> selection, bool remember = true)
    {
        var members = tabs.Where(selection.Contains).ToArray();
        if (members.Length == 0) return;
        var snapshot = members.Select(tab => new ClosedSelectionTab(tab.Host?.LocationPath, tabs.IndexOf(tab), tab.Group, tab == ActiveTab)).ToArray();
        var active = ActiveTab;
        SuppressSavedGroupSync = true;
        try
        {
            foreach (var tab in members.Reverse()) CloseTabCore(tabs.IndexOf(tab), remember: false, selectReplacement: false);
            if (active is not null && !tabs.Contains(active) && tabs.Count > 0)
                SelectTab(Math.Min(snapshot[0].Index, tabs.Count - 1));
            ClearTabSelection();
        }
        finally { SuppressSavedGroupSync = false; }
        if (remember)
        {
            RememberClosedTab(new ClosedTabEntry(null, Selection: snapshot));
            TabClosed?.Invoke(this, EventArgs.Empty);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RemoveTabsForMove(IReadOnlyList<FolderTab> selection) => CloseTabs(selection, remember: false);

    internal IReadOnlyList<FolderTab> AddTabBatch(IReadOnlyList<FolderTab> source, bool preserveGroups = true)
    {
        if (source.Count == 0 || tabs.Count + source.Count > 32)
        { SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first."); return []; }
        var copied = new List<FolderTab>();
        var groups = new Dictionary<TabGroup, TabGroup>();
        SuppressSavedGroupSync = true;
        foreach (var original in source)
        {
            TabGroup? group = null;
            if (preserveGroups && original.Group is { } old)
            {
                if (!groups.TryGetValue(old, out group))
                {
                    Control? owner = original.Button.Owner;
                    while (owner is not null && owner is not ExplorerPane) owner = owner.Parent;
                    bool wholeGroup = owner is ExplorerPane pane && pane.GroupMembers(old).All(source.Contains);
                    group = new TabGroup { Name = old.Name, Color = old.Color, SavedId = wholeGroup ? old.SavedId : null };
                    groups.Add(old, group);
                }
            }
            copied.Add(InsertTab(original.Host?.LocationPath, select: false, countVisit: false, group: group)!);
        }
        SelectTab(tabs.IndexOf(copied[0]));
        SuppressSavedGroupSync = false;
        return copied;
    }

    private void ReopenSelection(ClosedSelectionTab[] snapshot)
    {
        var restored = new List<FolderTab>();
        FolderTab? active = null;
        SuppressSavedGroupSync = true;
        try
        {
            foreach (var entry in snapshot.OrderBy(entry => entry.Index))
            {
                var group = entry.Group;
                if (group is not null) group.SavedId = ExistingSavedId(group.SavedId);
                if (group is not null && tabs.Count(tab => tab.Group == group) >= TabGroup.MaxTabs) group = null;
                int insertion = Math.Min(entry.Index, tabs.Count);
                if (group is not null && Groups.Contains(group))
                {
                    int first = tabs.FindIndex(tab => tab.Group == group);
                    int end = tabs.FindLastIndex(tab => tab.Group == group) + 1;
                    insertion = Math.Clamp(insertion, first, end);
                }
                else insertion = GroupBoundary(insertion);
                var tab = InsertTab(entry.Path, select: false, countVisit: false, group: group, insertionIndex: insertion)!;
                restored.Add(tab);
                if (entry.Active) active = tab;
            }
        }
        finally { SuppressSavedGroupSync = false; }
        LastReopenedTabs = restored;
        SelectTab(tabs.IndexOf(active ?? restored[0]));
    }
}
