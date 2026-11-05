namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    private readonly Dictionary<TabGroup, TabGroupChip> groupChips = [];

    internal IReadOnlyList<FolderTab> Tabs => tabs;
    internal IReadOnlyList<TabGroup> Groups => tabs.Select(tab => tab.Group).OfType<TabGroup>().Distinct().ToArray();
    internal IReadOnlyList<FolderTab> GroupMembers(TabGroup group) => tabs.Where(tab => tab.Group == group).ToArray();
    internal int PinnedCount
    {
        get
        {
            int count = 0;
            while (count < tabs.Count && tabs[count].Pinned) count++;
            return count;
        }
    }

    internal void SetTabsPinned(IReadOnlyList<FolderTab> selection, bool pinned)
    {
        var moving = tabs.Where(selection.Contains).ToArray();
        if (moving.Length == 0) return;
        var active = ActiveTab;
        tabs.RemoveAll(moving.Contains);
        foreach (var tab in moving)
        {
            tab.Pinned = pinned;
            if (pinned) tab.Group = null;
            tab.AccessibleTitle = null;
        }
        tabs.InsertRange(PinnedCount, moving);
        FinishGroupMutation(active);
    }

    internal void ActiveGroupCommand(bool close)
    {
        if (ActiveTab?.Group is not { } group)
        {
            SetLocalizedStatus("The active tab is not in a group.");
            return;
        }
        if (close) CloseGroup(group);
        else AddTabInGroup(group);
    }

    internal TabGroup? CreateGroup(FolderTab tab)
    {
        if (!tabs.Contains(tab)) return null;
        if (tab.Group is not null) RemoveFromGroup(tab);
        var used = Groups.Select(group => group.Color).ToHashSet();
        var colors = Enum.GetValues<TabGroupColor>();
        var group = new TabGroup { Color = colors.FirstOrDefault(color => !used.Contains(color), colors[Groups.Count % colors.Length]) };
        if (tab.Pinned) SetTabsPinned([tab], false);
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
        tab.Pinned = false;
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
        bool keepStripFocus = tabStrip.ContainsFocus;
        group.Collapsed = collapsed;
        if (collapsed && ActiveTab?.Group == group && !SelectOutsideGroup(group, focus: !keepStripFocus))
        {
            // A completely grouped, full pane cannot make room for the visible blank tab.
            group.Collapsed = false;
            SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first.");
        }
        LayoutTabs();
        if (keepStripFocus && groupChips.TryGetValue(group, out var chip) && chip.Available) chip.Select();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private int FindTabOutsideGroup(TabGroup group)
    {
        int first = tabs.FindIndex(tab => tab.Group == group);
        int last = tabs.FindLastIndex(tab => tab.Group == group);
        if (first < 0) return -1;
        int next = last + 1 < tabs.Count
            ? tabs.FindIndex(last + 1, tab => tab.Group?.Collapsed != true) : -1;
        return next >= 0 || first == 0 ? next
            : tabs.FindLastIndex(first - 1, tab => tab.Group?.Collapsed != true);
    }

    private bool SelectOutsideGroup(TabGroup group, bool focus = true)
    {
        int next = FindTabOutsideGroup(group);
        if (next >= 0) { SelectTab(next, focus); return true; }
        int insertion = tabs.FindLastIndex(tab => tab.Group == group) + 1;
        var blank = InsertTab(null, select: false, insertionIndex: insertion);
        if (blank is null) return false;
        SelectTab(tabs.IndexOf(blank), focus);
        return true;
    }

    internal void AddRelatedTab(string path)
    {
        OpenPathsInNewTabs([path], foreground: true);
    }

    private void DuplicateTab(FolderTab original)
    {
        if (original.Host is { } host) InsertRelatedTab(original, host.LocationPath);
    }

    private void InsertRelatedTab(FolderTab original, string path)
    {
        var group = original.Group;
        int insertion = tabs.IndexOf(original) + 1;
        if (group is not null && tabs.Count(tab => tab.Group == group) >= TabGroup.MaxTabs)
        {
            insertion = tabs.FindLastIndex(tab => tab.Group == group) + 1;
            group = null;
        }
        InsertTab(path, select: true, group: group, insertionIndex: insertion);
    }

    private void UpdateTabAccessibleName(FolderTab tab)
    {
        string? groupLabel = tab.Group is { } group ? GroupLabel(group) : null;
        string title = tab.Pinned ? text.T("Pinned tab: {0}", tab.Button.Title) : tab.Button.Title;
        if (tab.AccessibleTitle == title && tab.AccessibleGroupLabel == groupLabel
            && tab.AccessibleLanguage == text.Language) return;
        tab.AccessibleTitle = title;
        tab.AccessibleGroupLabel = groupLabel;
        tab.AccessibleLanguage = text.Language;
        tab.Button.AccessibleName = tab.Pinned ? title : groupLabel is null ? $"Folder tab: {title}"
            : text.T("Folder tab: {0}, group: {1}", title, groupLabel);
    }

    internal void FocusTabStrip()
    {
        tabStrip.Focus();
        ToolStripItem? item = ActiveTab?.Button;
        if (item?.Available != true)
            item = tabStrip.Items.Cast<ToolStripItem>().FirstOrDefault(candidate => candidate.Available && candidate.CanSelect);
        item?.Select();
    }

    internal bool HandleTabStripKey(Keys key)
    {
        if (tabStrip.ContainsFocus && key == Keys.Escape) { ClearTabSelection(); return true; }
        if (!tabStrip.ContainsFocus || key is not (Keys.Apps or (Keys.Shift | Keys.F10))) return false;
        var item = tabStrip.Items.Cast<ToolStripItem>().FirstOrDefault(candidate => candidate.Selected);
        if (item is TabGroupChip chip) ShowGroupMenu(chip.Group);
        else if (item is FolderTabButton button)
            ShowTabMenu(tabs.FindIndex(tab => tab.Button == button), keyboard: true);
        else return false;
        return true;
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
        bool wasSuppressed = SuppressSavedGroupSync;
        SuppressSavedGroupSync = true;
        int selected = ActiveTab is { } active ? members.ToList().IndexOf(active) : -1;
        var snapshot = remember ? new ClosedGroup(group.Name, group.Color,
            members.Select(tab => tab.Host?.LocationPath).ToArray(), Math.Max(0, selected), group.SavedId) : null;
        try
        {
            if (selected >= 0)
            {
                int next = FindTabOutsideGroup(group);
                if (next >= 0) SelectTab(next);
                else
                {
                    ActiveView?.Hide();
                    selectedIndex = -1;
                }
            }
            foreach (var tab in members.Reverse()) CloseTabCore(tabs.IndexOf(tab), remember: false, selectReplacement: false);
            if (selected >= 0 && selectedIndex < 0 && tabs.Count > 0) AddBlankTab();
        }
        finally { SuppressSavedGroupSync = wasSuppressed; }
        StateChanged?.Invoke(this, EventArgs.Empty);
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
        // Keep pins at the front without allocating on the ordinary group-edit path.
        int prefix = 0;
        for (int index = 0; index < tabs.Count; index++)
        {
            if (!tabs[index].Pinned) continue;
            if (index != prefix)
            {
                var pin = tabs[index];
                tabs.RemoveAt(index);
                tabs.Insert(prefix, pin);
            }
            prefix++;
        }
        selectedIndex = active is null ? -1 : tabs.IndexOf(active);
        RememberGroupSelection();
        UpdateTabSelectionAppearance();
        SynchronizeGroupStrip();
        LayoutTabs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RememberGroupSelection()
    {
        if (ActiveTab?.Group is not { } group) return;
        int member = 0;
        for (int i = 0; i < selectedIndex; i++)
            if (tabs[i].Group == group) member++;
        group.LastSelectedMember = member;
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
                SavedId = group.SavedId, Start = start, Count = index - start });
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
            var group = new TabGroup { Name = entry.Name, Color = Enum.Parse<TabGroupColor>(entry.Color),
                Collapsed = entry.Collapsed };
            group.SavedId = ExistingSavedId(entry.SavedId, group);
            for (int index = entry.Start; index < entry.Start + entry.Count; index++) tabs[index].Group = group;
        }
        SynchronizeGroupStrip();
    }

    private string GroupLabel(TabGroup group) => group.Name.Length == 0 ? text.T(group.Color.ToString()) : group.Name;

    private void AddGroupTabMenu(ContextMenuStrip menu, FolderTab tab)
    {
        var selection = selectedTabs.Contains(tab) ? SelectedTabs : [tab];
        bool multiple = selection.Count > 1;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(multiple ? text.T("Add {0} tabs to new group", selection.Count) : text.T("Add tab to new group"), null, (_, _) =>
        {
            if ((multiple ? CreateGroup(selection) : CreateGroup(tab)) is { } group) BeginInvoke(() => ShowGroupMenu(group));
        });
        var add = new ToolStripMenuItem(text.T(multiple ? "Add tabs to group" : "Add tab to group"));
        foreach (var group in Groups.Where(group => selection.Any(member => member.Group != group)))
        {
            int count = GroupMembers(group).Count + selection.Count(member => member.Group != group);
            var item = new ToolStripMenuItem(GroupLabel(group))
            {
                Enabled = count <= TabGroup.MaxTabs,
                ForeColor = Theme.GroupColor(group.Color),
                ToolTipText = count > TabGroup.MaxTabs ? text.T("A group holds up to 12 tabs.") : ""
            };
            item.Click += (_, _) => { if (multiple) AddToGroup(selection, group); else AddToGroup(tab, group); };
            add.DropDownItems.Add(item);
        }
        add.Enabled = add.DropDownItems.Count > 0;
        menu.Items.Add(add);
        if (selection.Any(member => member.Group is not null))
            menu.Items.Add(text.T("Remove from group"), null, (_, _) => { if (multiple) RemoveFromGroup(selection); else RemoveFromGroup(tab); });
        menu.Items.Add(new ToolStripSeparator());
    }

    private sealed class GroupColorItem(TabGroupColor color, int index) : ToolStripButton
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int size = Math.Max(8, (int)Math.Round(16 * (Owner?.DeviceDpi ?? 96) / 96d));
            int left = ((Owner?.ClientSize.Width ?? Width) - size) * index / 8 - Bounds.Left;
            var circle = new RectangleF(left + .5f, (Height - size) / 2f + .5f, size - 1, size - 1);
            using var fill = new SolidBrush(Theme.GroupColor(color));
            int gap = Math.Max(1, (int)Math.Round(3 * (Owner?.DeviceDpi ?? 96) / 96d));
            var center = Checked ? RectangleF.Inflate(circle, -gap, -gap) : circle;
            e.Graphics.FillEllipse(fill, center);
            if (Checked)
            {
                using var pen = new Pen(Theme.GroupColor(color), Math.Max(1, size / 10f));
                var ring = RectangleF.Inflate(circle, -pen.Width / 2, -pen.Width / 2);
                e.Graphics.DrawEllipse(pen, ring);
            }
        }
    }
    internal event Action<string>? DeleteSavedGroupRequested;

    // Native cue banners use the system gray even in dark themes and normally disappear on focus.
    // Keep the native editor for selection, caret and accessibility; paint only its empty-state cue.
    private sealed class GroupNameCue : NativeWindow, IDisposable
    {
        private readonly TextBox editor;

        internal GroupNameCue(TextBox editor)
        {
            this.editor = editor;
            SendMessage(editor.Handle, 0x1501, 0, "");
            AssignHandle(editor.Handle);
            editor.Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is not (0xF or 0x317 or 0x318) || editor.TextLength != 0) return;
            using var graphics = message.Msg == 0xF ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
            using var background = new SolidBrush(editor.BackColor);
            graphics.FillRectangle(background, editor.ClientRectangle);
            var bounds = editor.ClientRectangle;
            bounds.X += 1;
            bounds.Width = Math.Max(0, bounds.Width - 1);
            TextRenderer.DrawText(graphics, editor.PlaceholderText, editor.Font, bounds, Theme.Muted,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        public void Dispose() => ReleaseHandle();
    }

    private sealed class GroupEditorMenu : ContextMenuStrip
    {
        protected override Padding DefaultPadding
        {
            get
            {
                int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
                return new Padding(Scale(14), Scale(6), Scale(14), Scale(8));
            }
        }
    }

    private sealed class GroupCommandItem(string label, string glyph) : ToolStripMenuItem(label)
    {
        private int Scale(int value) => (int)Math.Round(value * (Owner?.DeviceDpi ?? 96) / 96d);

        public override Size GetPreferredSize(Size constrainingSize)
        {
            var preferred = base.GetPreferredSize(constrainingSize);
            return new Size(preferred.Width + Scale(32), Math.Max(preferred.Height, Scale(32)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var background = new SolidBrush(Owner?.BackColor ?? Theme.Surface))
                e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, Size));
            if (Enabled && (Selected || Pressed))
                Theme.FillRounded(e.Graphics, new Rectangle(0, 0, Width, Height), Pressed ? Theme.Selected : Theme.Hover, Owner?.DeviceDpi ?? 96);
            Color foreground = Enabled ? Theme.Text : Theme.Muted;
            int iconWidth = Scale(18), inset = Scale(14), labelLeft = inset + Scale(28);
            int right = (Owner?.ClientSize.Width ?? Width) - inset;
            DrawGlyph(Checked ? "\uE73E" : glyph, new Rectangle(inset, 0, iconWidth, Height));
            int hintWidth = string.IsNullOrEmpty(ShortcutKeyDisplayString) ? 0
                : TextRenderer.MeasureText(ShortcutKeyDisplayString, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var labelBounds = Rectangle.FromLTRB(labelLeft, 0,
                Math.Max(labelLeft, right - (hintWidth > 0 ? hintWidth + Scale(16) : HasDropDownItems ? Scale(18) : 0)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, labelBounds, foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (hintWidth > 0)
                TextRenderer.DrawText(e.Graphics, ShortcutKeyDisplayString, Font,
                    Rectangle.FromLTRB(right - hintWidth, 0, right, Height), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            else if (HasDropDownItems) DrawGlyph("\uE76C", new Rectangle(right - Scale(10), 0, Scale(10), Height));

            void DrawGlyph(string symbol, Rectangle bounds)
            {
                // Paths use grayscale edge coverage, avoiding ClearType's colored fringes on small icons.
                using var font = new Font(Theme.IconFont, Scale(16), FontStyle.Regular, GraphicsUnit.Pixel);
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddString(symbol, font.FontFamily, (int)FontStyle.Regular, font.Size, Point.Empty, StringFormat.GenericTypographic);
                var ink = path.GetBounds();
                float scale = Math.Min(Scale(16), bounds.Width) / Math.Max(ink.Width, ink.Height);
                using var transform = new System.Drawing.Drawing2D.Matrix(scale, 0, 0, scale,
                    bounds.Left - ink.Left * scale, bounds.Top + (bounds.Height - ink.Height * scale) / 2 - ink.Top * scale);
                path.Transform(transform);
                var smoothing = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var fill = new SolidBrush(foreground);
                e.Graphics.FillPath(fill, path);
                e.Graphics.SmoothingMode = smoothing;
            }
        }
    }

    private void ShowGroupMenu(TabGroup group)
    {
        if (!groupChips.ContainsKey(group)) return;
        int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        int inset = Scale(14);
        var menu = new GroupEditorMenu { ShowItemToolTips = true, ShowImageMargin = false, ShowCheckMargin = false,
            Font = Font, Padding = new Padding(inset, Scale(6), inset, Scale(8)) };
        var name = new ToolStripTextBox { Text = group.Name, MaxLength = 40, AccessibleName = text.T("Tab group name"),
            AutoSize = false, Width = Scale(272), Margin = new Padding(Scale(7), Scale(10), Scale(7), Scale(10)),
            BorderStyle = BorderStyle.None, Tag = Theme.GroupNameField };
        name.TextBox.PlaceholderText = text.T("Name this group");
        name.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            RenameGroup(group, name.Text);
            menu.Close();
            e.SuppressKeyPress = true;
        };
        menu.Items.Add(name);
        var colors = Theme.Strip();
        colors.AutoSize = false;
        colors.Padding = Padding.Empty;
        colors.CanOverflow = false;
        colors.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        foreach (var color in Enum.GetValues<TabGroupColor>())
        {
            int size = Scale(32);
            var item = new GroupColorItem(color, colors.Items.Count) { Checked = group.Color == color, AutoSize = false,
                Size = new Size(size, size), Margin = Padding.Empty, DisplayStyle = ToolStripItemDisplayStyle.None,
                ToolTipText = text.T(color.ToString()), AccessibleName = text.T(color.ToString()) };
            item.Click += (_, _) => { SetGroupColor(group, color); menu.Close(); };
            colors.Items.Add(item);
        }
        var colorHost = new ToolStripControlHost(colors) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        menu.Items.Add(colorHost);
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem Command(string key, string glyph, Action action, string hint = "")
        {
            var item = new GroupCommandItem(text.T(key), glyph) { ShortcutKeyDisplayString = hint,
                Padding = new Padding(0, Scale(5), Scale(4), Scale(5)) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Command("New tab in group", "\uE710", () => AddTabInGroup(group), "Alt+Shift+C");
        Command("Move group to new window", "\uE8A7", () => MoveGroupToWindowRequested?.Invoke(this, group));
        var move = new GroupCommandItem(text.T("Move group to pane"), "\uE8DE") { Padding = new Padding(0, Scale(5), Scale(4), Scale(5)) };
        for (int number = 1; number <= 4; number++)
        {
            if (number == PaneNumber) continue;
            int target = number;
            string? reason = GroupMoveDisabledReason?.Invoke(this, group, target);
            var item = move.DropDownItems.Add(text.T("Pane {0}", target), null,
                (_, _) => MoveGroupRequested?.Invoke(this, group, target));
            item.Enabled = reason is null;
            item.ToolTipText = reason is null ? "" : text.T(reason);
        }
        menu.Items.Add(move);
        Command(group.Collapsed ? "Expand group" : "Collapse group", group.Collapsed ? "\uE70D" : "\uE70E",
            () => SetGroupCollapsed(group, !group.Collapsed));
        var save = Command("Save group", "\uE74E", () => SaveGroupRequested?.Invoke(this, group));
        save.Checked = ExistingSavedId(group.SavedId, group) is not null;
        menu.Items.Add(new ToolStripSeparator());
        Command("Close group", "\uE8BB", () => CloseGroup(group), "Alt+Shift+W");
        Command("Ungroup", "\uE71B", () => Ungroup(group));
        if (ExistingSavedId(group.SavedId, group) is { } savedId)
            Command("Delete group", "\uE74D", () =>
            {
                CloseGroup(group);
                DeleteSavedGroupRequested?.Invoke(savedId);
            });
        menu.Closing += (_, _) => RenameGroup(group, name.Text);
        // WinForms finishes native menu teardown after Closed returns.
        menu.Closed += (_, _) => menu.BeginInvoke(() => menu.Dispose());
        int contentWidth = Math.Max(Scale(300), menu.Items.OfType<ToolStripMenuItem>()
            .Max(item => item.GetPreferredSize(Size.Empty).Width));
        // Menu rows paint across the native image-gutter area; hosted fields use DefaultPadding.
        // Explicit popup width avoids the native shortcut/arrow columns adding an extra field inset.
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            int height = item.GetPreferredSize(Size.Empty).Height;
            item.AutoSize = false;
            item.Size = new Size(contentWidth + inset * 2, height);
            item.Margin = Padding.Empty;
        }
        foreach (var separator in menu.Items.OfType<ToolStripSeparator>())
        {
            separator.AutoSize = false;
            separator.Size = new Size(contentWidth, Scale(9));
            separator.Margin = Padding.Empty;
        }
        name.Width = contentWidth - Scale(14);
        colors.Size = colorHost.Size = new Size(contentWidth, Scale(32));
        // Equal hit-target slots leave room for each complete circle instead of a dot-sized final button.
        // The dots themselves span the row with equal gaps, including their antialiased edge and ring.
        for (int index = 0; index < colors.Items.Count; index++)
        {
            int left = contentWidth * index / colors.Items.Count;
            int right = contentWidth * (index + 1) / colors.Items.Count;
            colors.Items[index].Size = new Size(right - left, Scale(32));
        }
        Theme.Apply(menu);
        menu.AutoSize = false;
        menu.Size = new Size(contentWidth + inset * 2, menu.PreferredSize.Height);
        name.BackColor = name.TextBox.BackColor = Theme.Hover;
        name.ForeColor = name.TextBox.ForeColor = Theme.Text;
        var chip = groupChips[group];
        menu.Show(tabStrip, new Point(chip.Bounds.Left, chip.Bounds.Bottom));
        name.Focus();
        name.SelectAll();
        var cue = new GroupNameCue(name.TextBox);
        menu.Closed += (_, _) => cue.Dispose();
    }
}
