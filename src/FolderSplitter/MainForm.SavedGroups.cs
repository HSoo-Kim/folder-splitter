namespace QuadExplorer;

public sealed partial class MainForm
{
    private readonly Panel savedGroupsPanel = new() { Dock = DockStyle.Bottom };
    private readonly ListBox savedGroupPlaces = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false, AccessibleName = "Saved groups"
    };
    private Button? savedGroupsHeader;
    private bool savedGroupsExpanded = true;
    private sealed record SavedGroupPlace(SavedTabGroup Group, string Label)
    {
        public override string ToString() => Label;
    }

    private void InitializeSavedGroupsSidebar()
    {
        foreach (var pane in panes)
        {
            pane.SavedGroupExists = id => application.SavedGroups.Any(group => group.Id == id);
            pane.SavedGroupLinkAvailable = application.CanLinkSavedGroup;
            foreach (var group in pane.Groups)
                group.SavedId = pane.ExistingSavedId(group.SavedId, group);
        }
        savedGroupsHeader = Theme.Button("", "Saved groups", () =>
        {
            savedGroupsExpanded = !savedGroupsExpanded;
            RefreshSavedGroupsSidebar();
        }, quiet: true);
        savedGroupsHeader.Dock = DockStyle.Top;
        savedGroupsHeader.TextAlign = ContentAlignment.MiddleLeft;
        savedGroupsHeader.FlatAppearance.BorderSize = 0;
        savedGroupPlaces.DrawItem += DrawSavedGroup;
        TrackHover(savedGroupPlaces);
        savedGroupPlaces.Resize += (_, _) => savedGroupPlaces.Invalidate();
        savedGroupPlaces.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && savedGroupPlaces.IndexFromPoint(e.Location) >= 0
                && savedGroupPlaces.SelectedItem is SavedGroupPlace item)
                application.OpenSavedGroup(this, item.Group.Id);
        };
        savedGroupPlaces.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && savedGroupPlaces.SelectedItem is SavedGroupPlace item)
            {
                application.OpenSavedGroup(this, item.Group.Id);
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        savedGroupPlaces.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                savedGroupPlaces.SelectedIndex = savedGroupPlaces.IndexFromPoint(e.Location);
        };
        var menu = new ContextMenuStrip();
        void AddCommand(string key, Action<string> action)
        {
            BindText(menu.Items.Add(key, null, (_, _) =>
            {
                if (savedGroupPlaces.SelectedItem is SavedGroupPlace item) action(item.Group.Id);
            }), key);
        }
        AddCommand("Open", id => application.OpenSavedGroup(this, id));
        AddCommand("Open in new window", id => application.OpenSavedGroup(this, id, newWindow: true));
        menu.Items.Add(new ToolStripSeparator());
        AddCommand("Rename", PromptSavedGroupName);
        AddCommand("Delete", application.DeleteSavedGroup);
        menu.Opening += (_, e) => e.Cancel = savedGroupPlaces.SelectedItem is not SavedGroupPlace;
        savedGroupPlaces.ContextMenuStrip = menu;
        savedGroupsPanel.Controls.Add(savedGroupPlaces);
        savedGroupsPanel.Controls.Add(savedGroupsHeader);
        sidebar.Controls.Add(savedGroupsPanel);
        sidebar.Resize += (_, _) => ResizeSavedGroupsSidebar();
        RefreshSavedGroupsSidebar();
    }

    internal void RefreshSavedGroupsSidebar()
    {
        if (savedGroupsHeader is null || IsDisposed) return;
        string? selected = (savedGroupPlaces.SelectedItem as SavedGroupPlace)?.Group.Id;
        savedGroupPlaces.BeginUpdate();
        try
        {
            savedGroupPlaces.Items.Clear();
            foreach (var group in application.SavedGroups)
            {
                string label = text.T("{0} ({1} tabs)", SavedGroupLabel(group), group.Paths.Count);
                int index = savedGroupPlaces.Items.Add(new SavedGroupPlace(group, label));
                if (group.Id == selected) savedGroupPlaces.SelectedIndex = index;
            }
        }
        finally { savedGroupPlaces.EndUpdate(); }
        savedGroupPlaces.BackColor = savedGroupsPanel.BackColor = Theme.Sidebar;
        savedGroupPlaces.ForeColor = Theme.Text;
        savedGroupPlaces.ItemHeight = (int)Math.Round(32 * DeviceDpi / 96d);
        savedGroupsHeader.Height = savedGroupPlaces.ItemHeight;
        savedGroupsHeader.BackColor = Theme.Canvas;
        savedGroupsHeader.ForeColor = Theme.Muted;
        savedGroupsHeader.Font = favoritesHeader.Font;
        savedGroupsHeader.Text = (savedGroupsExpanded ? "▾  " : "▸  ") + text.T("Saved groups");
        savedGroupsHeader.AccessibleName = text.T("Saved groups");
        tips.SetToolTip(savedGroupsHeader, text.T("Collapse / expand saved groups"));
        bool hasGroups = application.SavedGroups.Count > 0;
        // Splitter includes hidden docked siblings in its size limits; an empty section must reserve no space.
        savedGroupsPanel.Visible = hasGroups;
        savedGroupsPanel.Dock = hasGroups ? DockStyle.Bottom : DockStyle.None;
        if (!hasGroups) savedGroupsPanel.Bounds = Rectangle.Empty;
        savedGroupPlaces.Visible = savedGroupsExpanded;
        ResizeSavedGroupsSidebar();
        savedGroupPlaces.Invalidate();
    }

    private void ResizeSavedGroupsSidebar()
    {
        if (savedGroupsHeader is null || application.SavedGroups.Count == 0) return;
        int rows = savedGroupsExpanded ? Math.Min(6, application.SavedGroups.Count) : 0;
        int rowHeight = savedGroupPlaces.ItemHeight;
        int height = savedGroupsHeader.Height + Math.Min(rows * rowHeight, Math.Max(rowHeight, sidebar.ClientSize.Height / 2));
        if (savedGroupsPanel.Height != height) savedGroupsPanel.Height = height;
    }

    private string SavedGroupLabel(SavedTabGroup group) => group.Name.Length > 0 ? group.Name : text.T(group.Color);

    private void DrawSavedGroup(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= savedGroupPlaces.Items.Count) return;
        var item = (SavedGroupPlace)savedGroupPlaces.Items[e.Index];
        using var background = new SolidBrush(Theme.Sidebar);
        e.Graphics.FillRectangle(background, e.Bounds);
        bool selected = e.State.HasFlag(DrawItemState.Selected);
        if (selected) Theme.FillRounded(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), Theme.Selected, DeviceDpi);
        else if (hoverRows.GetValueOrDefault(savedGroupPlaces, -1) == e.Index)
            Theme.FillRounded(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), Theme.Hover, DeviceDpi);
        int gap = (int)Math.Round(10 * DeviceDpi / 96d);
        int dot = (int)Math.Round(10 * DeviceDpi / 96d);
        Enum.TryParse<TabGroupColor>(item.Group.Color, out var color);
        using var brush = new SolidBrush(Theme.GroupColor(color));
        e.Graphics.FillEllipse(brush, e.Bounds.Left + gap, e.Bounds.Top + (e.Bounds.Height - dot) / 2, dot, dot);
        string count = item.Group.Paths.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        int countWidth = TextRenderer.MeasureText(count, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var countBounds = new Rectangle(e.Bounds.Right - gap - countWidth, e.Bounds.Top, countWidth, e.Bounds.Height);
        var bounds = new Rectangle(e.Bounds.Left + dot + gap * 2, e.Bounds.Top,
            Math.Max(0, countBounds.Left - e.Bounds.Left - dot - gap * 3), e.Bounds.Height);
        Color foreground = selected ? Theme.Accent : Theme.Text;
        TextRenderer.DrawText(e.Graphics, SavedGroupLabel(item.Group), Font, bounds, foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, count, Font, countBounds, foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        e.DrawFocusRectangle();
    }

    private void ToggleSavedGroup(ExplorerPane pane, TabGroup group) => application.ToggleSavedGroup(pane, group);

    internal bool AddSavedGroup(SavedTabGroup saved)
    {
        Enum.TryParse<TabGroupColor>(saved.Color, out var color);
        var group = Active.AddGroup(saved.Paths, saved.Name, color);
        if (group is null) return false;
        group.SavedId = Active.ExistingSavedId(saved.Id, group);
        ScheduleSave();
        return true;
    }

    internal bool FocusSavedGroup(string id)
    {
        for (int index = 0; index < panes.Length; index++)
        {
            var pane = panes[index];
            var group = pane.Groups.FirstOrDefault(candidate => candidate.SavedId == id);
            if (group is null || pane.GroupMembers(group).FirstOrDefault() is not { } first) continue;
            if (!visiblePanes.Contains(index))
                RenderLayout(layoutTree.Split(activePane, index, SplitEdge.Right), preserveClosedPanes: true);
            Reveal();
            Activate();
            ActivatePane(index);
            pane.SelectTab(pane.IndexOfTab(first));
            return true;
        }
        return false;
    }

    private void PromptSavedGroupName(string id)
    {
        var saved = application.SavedGroups.FirstOrDefault(group => group.Id == id);
        if (saved is null) return;
        using var dialog = new Form
        {
            Text = text.T("Rename saved group"), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12), Font = Font
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var label = new Label { Text = text.T("Tab group name"), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var input = new TextBox { Width = 280, MaxLength = 40, Text = saved.Name, AccessibleName = text.T("Tab group name") };
        var ok = new Button { Text = text.T("Save"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = text.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        layout.Controls.Add(label, 0, 0);
        layout.SetColumnSpan(label, 2);
        layout.Controls.Add(input, 0, 1);
        layout.SetColumnSpan(input, 2);
        layout.Controls.Add(ok, 0, 2);
        layout.Controls.Add(cancel, 1, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        Theme.Apply(dialog);
        input.SelectAll();
        if (dialog.ShowDialog(this) == DialogResult.OK) application.RenameSavedGroup(id, input.Text);
    }
}
