namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    private readonly Dictionary<TabGroup, TabGroupChip> groupChips = [];

    internal IReadOnlyList<FolderTab> Tabs => tabs;
    internal IReadOnlyList<TabGroup> Groups => tabs.Select(tab => tab.Group).OfType<TabGroup>().Distinct().ToArray();
    internal IReadOnlyList<FolderTab> GroupMembers(TabGroup group) => tabs.Where(tab => tab.Group == group).ToArray();

    internal TabGroup? CreateGroup(FolderTab tab)
    {
        if (!tabs.Contains(tab)) return null;
        if (tab.Group is not null) RemoveFromGroup(tab);
        var used = Groups.Select(group => group.Color).ToHashSet();
        var colors = Enum.GetValues<TabGroupColor>();
        var group = new TabGroup { Color = colors.FirstOrDefault(color => !used.Contains(color), colors[Groups.Count % colors.Length]) };
        tab.Group = group;
        FinishGroupMutation(ActiveTab);
        return group;
    }

    internal bool AddToGroup(FolderTab tab, TabGroup group)
    {
        if (!tabs.Contains(tab) || !groupChips.ContainsKey(group)) return false;
        if (tab.Group == group) return true;
        if (GroupMembers(group).Count >= TabGroup.MaxTabs)
        {
            SetLocalizedStatus("A group holds up to 12 tabs.");
            return false;
        }
        var active = ActiveTab;
        tabs.Remove(tab);
        int insertion = tabs.FindLastIndex(member => member.Group == group) + 1;
        tab.Group = group;
        tabs.Insert(insertion, tab);
        if (tab == active) group.Collapsed = false;
        FinishGroupMutation(active);
        return true;
    }

    internal void RemoveFromGroup(FolderTab tab)
    {
        if (!tabs.Contains(tab) || tab.Group is not { } group) return;
        var active = ActiveTab;
        int originalIndex = tabs.IndexOf(tab);
        tabs.Remove(tab);
        int last = tabs.FindLastIndex(member => member.Group == group);
        int insertion = last < 0 ? originalIndex : last + 1;
        tab.Group = null;
        tabs.Insert(insertion, tab);
        FinishGroupMutation(active);
    }

    internal void Ungroup(TabGroup group)
    {
        foreach (var tab in tabs) if (tab.Group == group) tab.Group = null;
        FinishGroupMutation(ActiveTab);
    }

    internal void RenameGroup(TabGroup group, string name)
    {
        if (!groupChips.ContainsKey(group)) return;
        name = name.Trim();
        group.Name = name.Length > 40 ? name[..40] : name;
        LayoutTabs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetGroupColor(TabGroup group, TabGroupColor color)
    {
        if (!groupChips.ContainsKey(group)) return;
        group.Color = color;
        foreach (var tab in tabs) if (tab.Group == group) tab.Button.GroupColor = color;
        LayoutTabs();
        tabStrip.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetGroupCollapsed(TabGroup group, bool collapsed)
    {
        if (!groupChips.ContainsKey(group) || group.Collapsed == collapsed) return;
        group.Collapsed = collapsed;
        if (collapsed && ActiveTab?.Group == group)
        {
            int first = tabs.FindIndex(tab => tab.Group == group);
            int last = tabs.FindLastIndex(tab => tab.Group == group);
            int next = last + 1 < tabs.Count ? last + 1 : first - 1;
            if (next >= 0) SelectTab(next);
            else AddBlankTab();
        }
        LayoutTabs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool AddTabInGroup(TabGroup group)
    {
        if (!groupChips.ContainsKey(group)) return false;
        return InsertTab(null, select: true, group: group) is not null;
    }

    // Preflight the whole snapshot so later pane moves cannot leave half a group behind.
    internal TabGroup? AddGroup(IReadOnlyList<string?> paths, string name, TabGroupColor color,
        bool collapsed = false, int selectedMember = 0)
    {
        if (paths.Count == 0) return null;
        if (paths.Count > TabGroup.MaxTabs) { SetLocalizedStatus("A group holds up to 12 tabs."); return null; }
        if (tabs.Count + paths.Count > 32) { SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first."); return null; }
        name = name.Trim();
        var group = new TabGroup { Name = name.Length > 40 ? name[..40] : name, Color = color };
        int start = tabs.Count;
        foreach (string? path in paths) InsertTab(path, select: false, countVisit: false, group: group);
        SelectTab(start + Math.Clamp(selectedMember, 0, paths.Count - 1));
        if (collapsed) SetGroupCollapsed(group, true);
        return group;
    }

    internal void CloseGroup(TabGroup group, bool remember = true)
    {
        var members = GroupMembers(group);
        if (members.Count == 0) return;
        int selected = ActiveTab is { } active ? members.ToList().IndexOf(active) : -1;
        var snapshot = remember ? new ClosedGroup(group.Name, group.Color,
            members.Select(tab => tab.Host?.LocationPath).ToArray(), Math.Max(0, selected)) : null;
        foreach (var tab in members.Reverse()) CloseTab(tabs.IndexOf(tab), remember: false);
        if (snapshot is null) return;
        RememberClosedTab(new ClosedTabEntry(null, snapshot));
        TabClosed?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectAdjacentTab(int delta, bool wrap = true)
    {
        var visible = tabs.Where(tab => tab.Group?.Collapsed != true).ToArray();
        if (visible.Length == 0) return;
        int current = Array.IndexOf(visible, ActiveTab);
        int target = wrap ? ((current + delta) % visible.Length + visible.Length) % visible.Length
            : Math.Clamp(current + delta, 0, visible.Length - 1);
        if (target != current) SelectTab(tabs.IndexOf(visible[target]));
    }

    private void FinishGroupMutation(FolderTab? active)
    {
        selectedIndex = active is null ? -1 : tabs.IndexOf(active);
        SynchronizeGroupStrip();
        LayoutTabs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeGroupStrip()
    {
        tabStrip.SuspendLayout();
        try
        {
            var groups = Groups;
            foreach (var group in groupChips.Keys.Where(group => !groups.Contains(group)).ToArray())
            {
                var chip = groupChips[group];
                tabStrip.Items.Remove(chip);
                chip.Dispose();
                groupChips.Remove(group);
            }
            foreach (var tab in tabs) tabStrip.Items.Remove(tab.Button);
            foreach (var chip in groupChips.Values) tabStrip.Items.Remove(chip);
            TabGroup? previous = null;
            foreach (var tab in tabs)
            {
                tab.Button.GroupColor = tab.Group?.Color;
                if (tab.Group is { } group && group != previous)
                {
                    if (!groupChips.TryGetValue(group, out var chip))
                    {
                        chip = new TabGroupChip(group) { AutoSize = false };
                        chip.Click += (_, _) => SetGroupCollapsed(group, !group.Collapsed);
                        chip.DragRequested += () => GroupDragRequested?.Invoke(this, group);
                        chip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) ShowGroupMenu(group); };
                        groupChips.Add(group, chip);
                    }
                    tabStrip.Items.Insert(tabStrip.Items.IndexOf(newTab), chip);
                }
                tabStrip.Items.Insert(tabStrip.Items.IndexOf(newTab), tab.Button);
                previous = tab.Group;
            }
        }
        finally { tabStrip.ResumeLayout(); }
    }

    private List<TabGroupState> CaptureGroups(List<FolderTab> saved)
    {
        var result = new List<TabGroupState>();
        for (int index = 0; index < saved.Count;)
        {
            if (saved[index].Group is not { } group) { index++; continue; }
            int start = index;
            while (index < saved.Count && saved[index].Group == group) index++;
            result.Add(new TabGroupState { Name = group.Name, Color = group.Color.ToString(), Collapsed = group.Collapsed,
                Start = start, Count = index - start });
        }
        return result;
    }

    private void RestoreGroups(List<TabGroupState>? saved)
    {
        var normalized = SessionStore.Normalize(new SessionState
        {
            Panes = [new PaneState { Tabs = tabs.Select(tab => tab.Host?.LocationPath ?? "").ToList(), Groups = saved ?? [] }]
        }).Panes[0];
        foreach (var entry in normalized.Groups)
        {
            var group = new TabGroup { Name = entry.Name, Color = Enum.Parse<TabGroupColor>(entry.Color), Collapsed = entry.Collapsed };
            for (int index = entry.Start; index < entry.Start + entry.Count; index++) tabs[index].Group = group;
        }
        SynchronizeGroupStrip();
    }

    private string GroupLabel(TabGroup group) => group.Name.Length == 0 ? text.T(group.Color.ToString()) : group.Name;

    private void AddGroupTabMenu(ContextMenuStrip menu, FolderTab tab)
    {
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(text.T("Add tab to new group"), null, (_, _) => CreateGroup(tab));
        var add = new ToolStripMenuItem(text.T("Add tab to group"));
        foreach (var group in Groups.Where(group => group != tab.Group))
        {
            var item = new ToolStripMenuItem(GroupLabel(group))
            {
                Enabled = GroupMembers(group).Count < TabGroup.MaxTabs,
                ForeColor = Theme.GroupColor(group.Color),
                ToolTipText = GroupMembers(group).Count >= TabGroup.MaxTabs ? text.T("A group holds up to 12 tabs.") : ""
            };
            item.Click += (_, _) => AddToGroup(tab, group);
            add.DropDownItems.Add(item);
        }
        add.Enabled = add.DropDownItems.Count > 0;
        menu.Items.Add(add);
        if (tab.Group is not null) menu.Items.Add(text.T("Remove from group"), null, (_, _) => RemoveFromGroup(tab));
        menu.Items.Add(new ToolStripSeparator());
    }

    private sealed class GroupColorItem(TabGroupColor color) : ToolStripButton
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int size = Math.Max(8, (int)Math.Round(14 * (Owner?.DeviceDpi ?? 96) / 96d));
            var circle = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            using var fill = new SolidBrush(Theme.GroupColor(color));
            e.Graphics.FillEllipse(fill, circle);
            if (Checked)
            {
                circle.Inflate(2, 2);
                using var pen = new Pen(Theme.Text, 2);
                e.Graphics.DrawEllipse(pen, circle);
            }
        }
    }

    private void ShowGroupMenu(TabGroup group)
    {
        if (!groupChips.ContainsKey(group)) return;
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        var name = new ToolStripTextBox { Text = group.Name, MaxLength = 40, AccessibleName = text.T("Tab group name") };
        name.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            RenameGroup(group, name.Text);
            menu.Close();
            e.SuppressKeyPress = true;
        };
        menu.Items.Add(name);
        var colors = Theme.Strip();
        colors.AutoSize = true;
        colors.Padding = Padding.Empty;
        colors.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        foreach (var color in Enum.GetValues<TabGroupColor>())
        {
            int size = (int)Math.Round(28 * DeviceDpi / 96d);
            var item = new GroupColorItem(color) { Checked = group.Color == color, AutoSize = false,
                Size = new Size(size, size), Margin = Padding.Empty, DisplayStyle = ToolStripItemDisplayStyle.None,
                ToolTipText = text.T(color.ToString()), AccessibleName = text.T(color.ToString()) };
            item.Click += (_, _) => { SetGroupColor(group, color); menu.Close(); };
            colors.Items.Add(item);
        }
        menu.Items.Add(new ToolStripControlHost(colors) { Margin = Padding.Empty, Padding = Padding.Empty });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(text.T("New tab in group"), null, (_, _) => AddTabInGroup(group));
        menu.Items.Add(text.T(group.Collapsed ? "Expand group" : "Collapse group"), null,
            (_, _) => SetGroupCollapsed(group, !group.Collapsed));
        menu.Items.Add(text.T("Ungroup"), null, (_, _) => Ungroup(group));
        menu.Items.Add(text.T("Close group"), null, (_, _) => CloseGroup(group));
        var move = new ToolStripMenuItem(text.T("Move group to pane"));
        for (int number = 1; number <= 4; number++)
        {
            if (number == PaneNumber) continue;
            int target = number;
            move.DropDownItems.Add(text.T("Pane {0}", target), null,
                (_, _) => MoveGroupRequested?.Invoke(this, group, target));
        }
        menu.Items.Add(move);
        menu.Closing += (_, _) => RenameGroup(group, name.Text);
        // WinForms finishes native menu teardown after Closed returns.
        menu.Closed += (_, _) => BeginInvoke(() => menu.Dispose());
        Theme.Apply(menu);
        menu.Show(Cursor.Position);
        name.Focus();
    }
}
