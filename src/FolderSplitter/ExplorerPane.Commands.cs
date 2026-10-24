namespace QuadExplorer;

// File Explorer's command bar for this pane: new folder, clipboard, rename, delete, sort, view, a quick filter
// and a comparison with the other pane. Each button runs the Shell's own command on the active tab.
public sealed partial class ExplorerPane
{
    private readonly ToolStrip commands = Theme.Strip(outlineItems: true);
    private readonly List<ToolStripItem> selectionCommands = [];
    private readonly ToolStripTextBox filter = new()
    {
        AutoSize = false, Width = 160, Alignment = ToolStripItemAlignment.Right,
        BorderStyle = BorderStyle.None, Tag = Theme.SearchField, Overflow = ToolStripItemOverflow.Never
    };
    private readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 250 };
    private ToolStripDropDownButton sortMenu = null!, viewMenu = null!;
    private ToolStripButton compare = null!;
    private bool showVerticalSeparators, comfortableRows = true;
    private bool commandBarVisible = true;
    internal event Action<bool, bool>? DetailsAppearanceRequested;
    internal event EventHandler? CommandBarToggled;
    private readonly ToolStripButton undoClose = new CloseHistoryButton(false) { AutoSize = false },
        redoClose = new CloseHistoryButton(true) { AutoSize = false };
    internal event EventHandler? UndoCloseRequested;
    internal event EventHandler? RedoCloseRequested;

    internal void SetCloseHistory(bool canUndo, bool canRedo)
    {
        undoClose.Enabled = canUndo;
        redoClose.Enabled = canRedo;
    }
    internal event EventHandler? BulkRenameRequested;
    internal event EventHandler? CompareRequested;
    internal event EventHandler? DetailsPaneToggled;
    internal event EventHandler? ShellSettingsChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] internal Func<bool> DetailsPaneShown { get; set; } = () => false;

    private static readonly PropertyKey NameKey = Key(10), DateModifiedKey = Key(14), SizeKey = Key(12), TypeKey = Key(4);
    private static PropertyKey Key(uint id) => new() { FormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = id };

    private ToolStrip BuildCommands()
    {
        commands.Dock = DockStyle.Top;
        commands.Padding = new Padding(6, 0, 6, 2);
        commands.CanOverflow = true;
        commands.AccessibleName = $"Pane {PaneNumber} commands";
        AddCommand("\uE8F4", "New folder", "Ctrl+Shift+N", () => ActiveView?.InvokeBackgroundVerb("NewFolder"), false);
        AddCommand("\uE8C6", "Cut", "Ctrl+X", () => ActiveView?.InvokeSelectionVerb("cut"), true);
        AddCommand("\uE8C8", "Copy", "Ctrl+C", () => ActiveView?.InvokeSelectionVerb("copy"), true);
        AddCommand("\uE77F", "Paste", "Ctrl+V", () => ActiveView?.InvokeBackgroundVerb("paste"), false);
        AddCommand("\uE8AC", "Rename", "F2", Rename, true);
        AddCommand("\uE74D", "Delete", "Del", () => ActiveView?.InvokeSelectionVerb("delete"), true);
        commands.Items.Add(new ToolStripSeparator());
        sortMenu = AddMenu("\uE8CB", "Sort", BuildSortMenu);
        viewMenu = AddMenu("\uE890", "View", BuildViewMenu);
        compare = new ToolStripButton("\uE8AB") { Font = new Font(Theme.IconFont, 10), Alignment = ToolStripItemAlignment.Right, AccessibleName = $"Pane {PaneNumber} Compare", Overflow = ToolStripItemOverflow.Never };
        compare.Click += (_, _) => CompareRequested?.Invoke(this, EventArgs.Empty);
        languageBindings.Add(() => compare.ToolTipText = text.T("Compare with the other pane"));
        commands.Items.Add(compare);
        filter.AccessibleName = $"Pane {PaneNumber} filter";
        filter.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
        filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { filter.Clear(); ApplyFilter(); FocusFiles(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { ApplyFilter(); FocusFiles(); e.SuppressKeyPress = true; }
        };
        filterTimer.Tick += (_, _) => ApplyFilter();
        Theme.ThemeCue(filter.TextBox, () => text.T("Filter this folder"));
        languageBindings.Add(() => filter.ToolTipText = text.T("Filter this folder (Ctrl+F)"));
        languageBindings.Add(SetFilterCue);
        commands.Items.Add(filter);
        foreach (var (button, name, tip) in new[]
        {
            (redoClose, "Redo close", "Redo tab close in this pane"),
            (undoClose, "Undo close", "Undo tab close in this pane")
        })
        {
            button.Alignment = ToolStripItemAlignment.Right;
            button.Overflow = ToolStripItemOverflow.Never;
            button.AccessibleName = $"Pane {PaneNumber} {name}";
            button.Enabled = false;
            languageBindings.Add(() => button.ToolTipText = text.T(tip));
            commands.Items.Add(button);
        }
        undoClose.Click += (_, _) => UndoCloseRequested?.Invoke(this, EventArgs.Empty);
        redoClose.Click += (_, _) => RedoCloseRequested?.Invoke(this, EventArgs.Empty);
        commands.SizeChanged += (_, _) => FitFilter();
        commands.ItemAdded += (_, _) => FitFilter();
        languageBindings.Add(() => { foreach (var refresh in commandTips) refresh(); });
        foreach (var refresh in commandTips) refresh();
        return commands;
    }

    private readonly List<Action> commandTips = [];

    private void AddCommand(string glyph, string key, string shortcut, Action action, bool needsSelection)
    {
        var button = new ToolStripButton(glyph) { Font = new Font(Theme.IconFont, 10), AccessibleName = $"Pane {PaneNumber} {key}", AutoToolTip = false };
        button.Click += (_, _) => { action(); FocusFiles(); };
        commandTips.Add(() => button.ToolTipText = $"{text.T(key)} ({shortcut})");
        commands.Items.Add(button);
        if (needsSelection) selectionCommands.Add(button);
    }

    private ToolStripDropDownButton AddMenu(string glyph, string key, Action<ToolStripDropDownButton> build)
    {
        var menu = new ToolStripDropDownButton(glyph) { Font = new Font(Theme.IconFont, 10), AccessibleName = $"Pane {PaneNumber} {key}", AutoToolTip = false };
        menu.DropDownItems.Add("\u2026");
        menu.DropDownOpening += (_, _) =>
        {
            foreach (var old in menu.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
            build(menu);
            foreach (ToolStripItem item in menu.DropDownItems) item.Font = Font;
            Theme.Apply(commands);
        };
        commandTips.Add(() => menu.ToolTipText = text.T(key));
        commands.Items.Add(menu);
        return menu;
    }

    private void Rename()
    {
        if (ActiveView is not { } view) return;
        if (view.SelectionCount > 1) BulkRenameRequested?.Invoke(this, EventArgs.Empty);
        else view.StartRename();
    }

    private void BuildSortMenu(ToolStripDropDownItem menu)
    {
        menu.DropDownItems.Add(new ToolStripLabel(text.T("This pane")) { ForeColor = Theme.Muted, Padding = new Padding(8, 3, 0, 1) });
        var current = ActiveView?.FirstSortColumn;
        bool Same(PropertyKey a, PropertyKey b) => a.FormatId == b.FormatId && a.PropertyId == b.PropertyId;
        bool descending = current is { Direction: < 0 };
        foreach (var (key, label) in new[] { (NameKey, "Name"), (DateModifiedKey, "Date modified"), (TypeKey, "Type"), (SizeKey, "Size") })
        {
            var item = new ToolStripMenuItem(text.T(label)) { Checked = current is { } c && Same(c.Property, key) };
            item.Click += (_, _) => ActiveView?.SortBy(key, descending);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (down, label) in new[] { (false, "Ascending"), (true, "Descending") })
        {
            var item = new ToolStripMenuItem(text.T(label)) { Checked = current is not null && descending == down };
            item.Click += (_, _) => { if (ActiveView?.FirstSortColumn is { } column) ActiveView.SortBy(column.Property, down); };
            menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        var group = ActiveView?.GroupKey;
        bool grouped = group is { } g && g.FormatId != Guid.Empty;
        var none = new ToolStripMenuItem(text.T("No grouping")) { Checked = !grouped };
        none.Click += (_, _) => ActiveView?.GroupBy(default);
        menu.DropDownItems.Add(none);
        foreach (var (key, label) in new[] { (TypeKey, "Type"), (DateModifiedKey, "Date modified") })
        {
            var item = new ToolStripMenuItem(text.T("Group by: {0}", text.T(label))) { Checked = grouped && Same(group!.Value, key) };
            item.Click += (_, _) => ActiveView?.GroupBy(key);
            menu.DropDownItems.Add(item);
        }
    }

    private void BuildViewMenu(ToolStripDropDownItem menu)
    {
        menu.DropDownItems.Add(new ToolStripLabel(text.T("This pane")) { ForeColor = Theme.Muted, Padding = new Padding(8, 3, 0, 1) });
        var current = ActiveView?.ViewModeAndSize;
        foreach (var (mode, size, label) in new (uint, int, string)[]
        {
            (1, 256, "Extra large icons"), (1, 96, "Large icons"), (1, 48, "Medium icons"), (2, 16, "Small icons"),
            (3, 16, "List"), (4, 16, "Details"), (6, 48, "Tiles"), (8, 32, "Content")
        })
        {
            var item = new ToolStripMenuItem(text.T(label))
            {
                Checked = current is { } c && c.Mode == mode && (mode != 1 || c.Size == size),
                Enabled = ActiveView is not null
            };
            item.Click += (_, _) => ActiveView?.SetViewModeAndSize(mode, size);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripLabel(text.T("Windows settings")) { ForeColor = Theme.Muted, Padding = new Padding(8, 3, 0, 1) });
        var hidden = new ToolStripMenuItem(text.T("Show hidden items")) { Checked = ShellSettings.HiddenItemsShown };
        hidden.Click += (_, _) => { ShellSettings.HiddenItemsShown = !hidden.Checked; ShellSettingsChanged?.Invoke(this, EventArgs.Empty); };
        var extensions = new ToolStripMenuItem(text.T("Show file name extensions")) { Checked = ShellSettings.ExtensionsShown };
        extensions.Click += (_, _) => { ShellSettings.ExtensionsShown = !extensions.Checked; ShellSettingsChanged?.Invoke(this, EventArgs.Empty); };
        menu.DropDownItems.Add(hidden);
        menu.DropDownItems.Add(extensions);
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripLabel(text.T("All panes")) { ForeColor = Theme.Muted, Padding = new Padding(8, 3, 0, 1) });
        var details = new ToolStripMenuItem(text.T("Details pane")) { Checked = DetailsPaneShown() };
        details.Click += (_, _) => DetailsPaneToggled?.Invoke(this, EventArgs.Empty);
        menu.DropDownItems.Add(details);
        var commandBar = new ToolStripMenuItem(text.T("Show command bar"))
        {
            Checked = commandBarVisible, AccessibleName = "Show command bar"
        };
        commandBar.Click += (_, _) => CommandBarToggled?.Invoke(this, EventArgs.Empty);
        menu.DropDownItems.Add(commandBar);
        var separators = new ToolStripMenuItem(text.T("Vertical separators")) { Checked = showVerticalSeparators };
        separators.Click += (_, _) => DetailsAppearanceRequested?.Invoke(!showVerticalSeparators, comfortableRows);
        menu.DropDownItems.Add(separators);
        var spacing = new ToolStripMenuItem(text.T("Row spacing"));
        foreach (var (comfortable, label) in new[] { (false, "Compact"), (true, "Comfortable") })
        {
            var item = new ToolStripMenuItem(text.T(label)) { Checked = comfortableRows == comfortable };
            item.Click += (_, _) => DetailsAppearanceRequested?.Invoke(showVerticalSeparators, comfortable);
            spacing.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(spacing);
    }

    internal void SetDetailsAppearance(bool separators, bool comfortable)
    {
        showVerticalSeparators = separators;
        comfortableRows = comfortable;
        foreach (var tab in tabs)
            tab.Host?.SetDetailsAppearance(separators, comfortable);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetCommandBarVisible(bool visible)
    {
        commandBarVisible = visible;
        commands.Visible = visible;
        if (!visible && commands.ContainsFocus) FocusFiles();
    }

    internal void FocusFolderFilter()
    {
        if (ActiveView is null) return;
        if (commands.Visible && filter.Available)
        {
            filter.Focus();
            filter.SelectAll();
            return;
        }
        using var dialog = new Form
        {
            Text = text.T("Filter this folder"), AccessibleName = "Folder filter",
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Font = Font
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var input = new TextBox { Width = 280, Text = ActiveView.FilterText, AccessibleName = "Folder filter input" };
        var ok = new Button { Text = text.T("Apply"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = text.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        layout.Controls.Add(input, 0, 0);
        layout.SetColumnSpan(input, 2);
        layout.Controls.Add(ok, 0, 1);
        layout.Controls.Add(cancel, 1, 1);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        Theme.Apply(dialog);
        input.SelectAll();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            filter.Text = input.Text;
            ApplyFilter();
            FocusFiles();
        }
    }

    internal void ApplyFilter()
    {
        filterTimer.Stop();
        ActiveView?.SetFilter(filter.Text);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] internal string FilterBoxText { get => filter.Text; set => filter.Text = value; }

    private void UpdateCommands()
    {
        var view = ActiveView;
        bool selected = view is not null && view.SelectionCount > 0;
        foreach (var item in selectionCommands) item.Enabled = selected;
        foreach (ToolStripItem item in commands.Items)
            if (!selectionCommands.Contains(item) && item != undoClose && item != redoClose)
                item.Enabled = view is not null || item == compare || item == viewMenu;
        filterTimer.Stop();
        string active = view?.FilterText ?? "";
        if (filter.Text.Trim() != active) filter.Text = active;
    }

    private void SetFilterCue()
    {
        filter.TextBox.Invalidate(); // the themed placeholder repaints in the new language
    }

    private void FitFilter()
    {
        int Scaled(int value) => (int)Math.Round(value * commands.DeviceDpi / 96d);
        undoClose.Size = redoClose.Size = new Size(Scaled(24), Scaled(28));
        filter.Margin = new Padding(Scaled(30), Scaled(6), Scaled(12), Scaled(5)); // room for the magnifier and rounded edge
        int used = commands.Padding.Horizontal + 8;
        foreach (ToolStripItem item in commands.Items)
            if (item != filter && item.Available) used += item.Width + item.Margin.Horizontal;
        int room = commands.DisplayRectangle.Width - used - filter.Margin.Horizontal;
        filter.Visible = room >= 70;
        if (filter.Visible) filter.Width = Math.Min(220, room);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, string lParam);
}
