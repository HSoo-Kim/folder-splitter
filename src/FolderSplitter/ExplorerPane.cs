using System.ComponentModel;
using System.Diagnostics;

namespace QuadExplorer;

public sealed class ExplorerPane : UserControl
{
    private sealed record FolderTab(ShellViewHost Host, ToolStripButton Button);
    private readonly List<FolderTab> tabs = [];
    private readonly Stack<string> closedTabs = [];
    private readonly ToolStrip tabStrip = Theme.Strip();
    private readonly Panel contents = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly Label emptyHint = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "Enter a folder path above or choose a folder from Pane options.",
        ForeColor = Theme.Muted, BackColor = Color.White, Padding = new Padding(20)
    };
    private readonly TextBox address;
    private readonly Label status;
    private readonly Panel activeLine;
    private readonly ToolTip tips = new();
    private readonly ContextMenuStrip tabMenu = new();
    private readonly Image folderIcon = Theme.FolderIcon();
    private int selectedIndex = -1;

    public int PaneNumber { get; }
    public int SelectedIndex => selectedIndex;
    public int TabCount => tabs.Count;
    public ShellViewHost? ActiveView => selectedIndex >= 0 ? tabs[selectedIndex].Host : null;
    public string CurrentPath => ActiveView?.LocationPath ?? "";
    public event EventHandler? Activated;
    public event EventHandler? StateChanged;
    public event EventHandler<string>? FavoriteRequested;
    public event Action<ExplorerPane, string, int>? MoveTabRequested;
    internal event Action<ExplorerPane, ShellViewHost>? TabDragRequested;
    internal event Action<ExplorerPane, ShellViewHost, SplitEdge>? SplitTabRequested;
    internal Rectangle FileAreaOnScreen => contents.RectangleToScreen(contents.ClientRectangle);

    public ExplorerPane(int number, PaneState state)
    {
        PaneNumber = number;
        Dock = DockStyle.Fill;
        BackColor = Theme.Border;
        Padding = new Padding(1);
        AccessibleName = $"Pane {number}";

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var navigation = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 6, RowCount = 1,
            Padding = new Padding(5, 4, 5, 4), BackColor = Color.White
        };
        for (int i = 0; i < 3; i++) navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddButton(navigation, 0, "\uE72B", "Back (Alt+Left)", () => ActiveView?.Travel(0x4000));
        AddButton(navigation, 1, "\uE72A", "Forward (Alt+Right)", () => ActiveView?.Travel(0x8000));
        AddButton(navigation, 2, "\uE74A", "Up (Alt+Up)", () => ActiveView?.Travel(0x2000));
        address = new TextBox
        {
            Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 2),
            BorderStyle = BorderStyle.FixedSingle, AccessibleName = $"Pane {number} address",
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.FileSystemDirectories
        };
        address.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Navigate(address.Text); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { address.Text = CurrentPath; FocusFiles(); e.SuppressKeyPress = true; }
        };
        navigation.Controls.Add(address, 3, 0);
        var go = Theme.Button("\uE8AD", $"Pane {number} go to address", () => Navigate(address.Text));
        go.Font = new Font("Segoe Fluent Icons", 10);
        go.AutoSize = true;
        go.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        go.Padding = new Padding(5);
        tips.SetToolTip(go, "Go to this folder (Enter)");
        navigation.Controls.Add(go, 4, 0);
        AddButton(navigation, 5, "\uE713", "Pane options", ShowOptions);

        var newTab = new ToolStripButton("+")
        {
            Alignment = ToolStripItemAlignment.Right, AutoSize = false, Width = 30,
            AccessibleName = $"Pane {number} new tab", ToolTipText = "New tab (Ctrl+T)",
            Overflow = ToolStripItemOverflow.Never
        };
        newTab.Click += (_, _) => AddTab(CurrentPath);
        tabStrip.Items.Add(newTab);
        tabStrip.AccessibleName = $"Pane {number} tabs";
        status = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Muted, BackColor = Color.White, Padding = new Padding(9, 0, 4, 0),
            AutoEllipsis = true, AccessibleName = $"Pane {number} status"
        };
        activeLine = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Theme.Border };
        body.Controls.Add(contents);
        body.Controls.Add(status);
        body.Controls.Add(navigation);
        body.Controls.Add(tabStrip);
        body.Controls.Add(activeLine);
        Controls.Add(body);
        contents.Controls.Add(emptyHint);
        emptyHint.Click += (_, _) => FocusAddress();
        Enter += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        MouseDown += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        foreach (string path in state.Tabs) AddTab(path, select: false);
        if (tabs.Count > 0) SelectTab(Math.Clamp(state.SelectedTab, 0, tabs.Count - 1), focus: false);
        else ShowEmptyState();
    }

    private void AddButton(TableLayoutPanel row, int column, string glyph, string name, Action action)
    {
        var button = Theme.Button(glyph, $"Pane {PaneNumber} {name}", action);
        button.Font = new Font("Segoe Fluent Icons", 10);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(5);
        tips.SetToolTip(button, name);
        row.Controls.Add(button, column, 0);
    }

    public void AddTab(string path, bool select = true)
    {
        if (tabs.Count >= 32) { SetStatus("Maximum 32 tabs per pane. Close a tab first."); return; }
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var host = new ShellViewHost(path) { Visible = false };
        var button = new FolderTabButton
        {
            Text = ShortTitle(host.DisplayName), Image = folderIcon, AutoSize = false,
            Width = 164, TextAlign = ContentAlignment.MiddleLeft,
            ImageAlign = ContentAlignment.MiddleLeft, ToolTipText = path,
            AccessibleName = $"Folder tab: {host.DisplayName}",
            Margin = new Padding(1, 0, 1, 0)
        };
        var tab = new FolderTab(host, button);
        button.CloseRequested += (_, _) => CloseTab(tabs.IndexOf(tab));
        button.DragRequested += (_, _) => TabDragRequested?.Invoke(this, host);
        button.Click += (_, _) => SelectTab(tabs.IndexOf(tab));
        button.MouseDown += (_, e) =>
        {
            int index = tabs.IndexOf(tab);
            if (e.Button == MouseButtons.Middle) CloseTab(index);
            else if (e.Button == MouseButtons.Right) ShowTabMenu(index);
        };
        host.Navigated += (_, _) =>
        {
            button.Text = ShortTitle(host.DisplayName);
            button.ToolTipText = host.LocationPath;
            button.AccessibleName = $"Folder tab: {host.DisplayName}";
            if (host == ActiveView) { address.Text = host.LocationPath; UpdateStatus(); }
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        host.NavigationError += (_, message) => SetStatus(message);
        tabs.Add(tab);
        tabStrip.Items.Insert(tabStrip.Items.Count - 1, button);
        contents.Controls.Add(host);
        if (select) SelectTab(tabs.Count - 1);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectTab(int index, bool focus = true)
    {
        if (index < 0 || index >= tabs.Count) return;
        if (selectedIndex >= 0 && selectedIndex < tabs.Count) tabs[selectedIndex].Host.Visible = false;
        selectedIndex = index;
        emptyHint.Visible = false;
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].Button.Checked = i == index;
            tabs[i].Button.ForeColor = i == index ? Theme.Accent : Theme.Text;
        }
        var current = tabs[index].Host;
        current.Visible = true;
        current.BringToFront();
        address.Text = current.LocationPath;
        UpdateStatus();
        if (focus) { Activated?.Invoke(this, EventArgs.Empty); current.FocusView(); }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseTab(int index = -1)
    {
        if (index < 0) index = selectedIndex;
        if (index < 0 || index >= tabs.Count) return;
        var tab = tabs[index];
        closedTabs.Push(tab.Host.LocationPath);
        int nextIndex = selectedIndex > index ? selectedIndex - 1 : Math.Min(selectedIndex, tabs.Count - 2);
        tabs.RemoveAt(index);
        tabStrip.Items.Remove(tab.Button);
        contents.Controls.Remove(tab.Host);
        tab.Host.Dispose();
        tab.Button.Dispose();
        selectedIndex = -1;
        if (tabs.Count == 0) ShowEmptyState();
        else SelectTab(Math.Max(0, nextIndex));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReopenTab()
    {
        if (closedTabs.Count > 0 && tabs.Count < 32) AddTab(closedTabs.Pop());
    }

    public void Navigate(string path)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (ActiveView is { } view) view.Navigate(path);
        else AddTab(path);
    }

    public void FocusAddress() { address.Focus(); address.SelectAll(); }
    public void FocusFiles()
    {
        if (ActiveView is { } view) view.FocusView();
        else FocusAddress();
    }
    public void SetActive(bool active) => activeLine.BackColor = active ? Theme.Accent : Theme.Border;
    public void SetStatus(string text) { status.Text = text; tips.SetToolTip(status, text); }
    private void UpdateStatus() => SetStatus($"Pane {PaneNumber}  |  {tabs.Count} tab{(tabs.Count == 1 ? "" : "s")}  |  {ActiveView?.DisplayName}");
    private void ShowEmptyState()
    {
        address.Clear();
        emptyHint.Visible = true;
        UpdateStatus();
    }
    public PaneState CaptureState() => new() { Tabs = tabs.Select(t => t.Host.LocationPath).ToList(), SelectedTab = selectedIndex };
    internal int IndexOfTab(ShellViewHost host) => tabs.FindIndex(tab => tab.Host == host);
    private static string ShortTitle(string title) => title.Length <= 18 ? title : title[..16] + "\u2026";

    private void ShowTabMenu(int index)
    {
        if (index < 0) return;
        SelectTab(index, focus: false);
        tabMenu.Items.Clear();
        tabMenu.Items.Add("New tab", null, (_, _) => AddTab(CurrentPath));
        tabMenu.Items.Add("Duplicate tab", null, (_, _) => AddTab(CurrentPath));
        tabMenu.Items.Add("Reopen closed tab", null, (_, _) => ReopenTab()).Enabled = closedTabs.Count > 0;
        var move = new ToolStripMenuItem("Move tab to pane");
        for (int i = 1; i <= 4; i++)
        {
            int target = i;
            if (target == PaneNumber) continue;
            move.DropDownItems.Add($"Pane {target}", null, (_, _) => MoveTabRequested?.Invoke(this, CurrentPath, target));
        }
        tabMenu.Items.Add(move);
        var split = new ToolStripMenuItem("Split tab");
        foreach (SplitEdge edge in Enum.GetValues<SplitEdge>())
        {
            var side = edge;
            split.DropDownItems.Add(side.ToString(), null, (_, _) =>
            {
                if (ActiveView is { } view) SplitTabRequested?.Invoke(this, view, side);
            });
        }
        tabMenu.Items.Add(split);
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add("Close tab    Ctrl+W", null, (_, _) => CloseTab());
        tabMenu.Items.Add("Close other tabs", null, (_, _) =>
        {
            var keep = tabs[selectedIndex];
            for (int i = tabs.Count - 1; i >= 0; i--) if (tabs[i] != keep) CloseTab(i);
        }).Enabled = tabs.Count > 1;
        tabMenu.Show(Cursor.Position);
    }

    private void ShowOptions()
    {
        tabMenu.Items.Clear();
        tabMenu.Items.Add("Refresh    F5", null, (_, _) => ActiveView?.RefreshFolder());
        tabMenu.Items.Add("Add to favorites    Ctrl+D", null, (_, _) => FavoriteRequested?.Invoke(this, CurrentPath));
        tabMenu.Items.Add("Copy folder path", null, (_, _) => CopyPath());
        tabMenu.Items.Add("Open in File Explorer", null, (_, _) => OpenInExplorer());
        tabMenu.Items.Add("Choose folder...", null, (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Choose a folder", UseDescriptionForTitle = true };
            if (Directory.Exists(CurrentPath)) picker.InitialDirectory = CurrentPath;
            if (picker.ShowDialog(this) == DialogResult.OK) Navigate(picker.SelectedPath);
        });
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add("Details view", null, (_, _) => ActiveView?.SetViewMode(4));
        tabMenu.Items.Add("List view", null, (_, _) => ActiveView?.SetViewMode(3));
        tabMenu.Items.Add("Icons view", null, (_, _) => ActiveView?.SetViewMode(1));
        tabMenu.Show(Cursor.Position);
    }

    public void CopyPath()
    {
        if (ActiveView is null) return;
        try { Clipboard.SetText(CurrentPath); SetStatus("Folder path copied."); }
        catch (System.Runtime.InteropServices.ExternalException ex) { SetStatus(ex.Message); }
    }

    public void OpenInExplorer()
    {
        if (ActiveView is null) return;
        try
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            info.ArgumentList.Add(CurrentPath);
            Process.Start(info);
        }
        catch (Win32Exception ex) { SetStatus(ex.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tips.Dispose(); tabMenu.Dispose(); folderIcon.Dispose(); }
        base.Dispose(disposing);
    }
}
