using System.ComponentModel;
using System.Diagnostics;
using QuadExplorer.Search;

namespace QuadExplorer;

public sealed class ExplorerPane : UserControl
{
    private sealed record FolderTab(ShellViewHost Host, ToolStripButton Button);
    private readonly List<FolderTab> tabs = [];
    private readonly UiText text;
    private readonly List<Action> languageBindings = [];
    private Func<string>? localizedStatus;
    private string statusMessage = "";
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
    private readonly ToolTip tips = new() { ShowAlways = true, AutoPopDelay = 10000 };
    private readonly ContextMenuStrip tabMenu = new();
    private readonly Image folderIcon = Theme.FolderIcon();
    private int selectedIndex = -1;
    private readonly IPathSearchBackend? search;
    private readonly Func<IReadOnlyList<string>>? searchRoots;
    private readonly AddressCandidates candidates;
    private readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 50 };
    private CancellationTokenSource? searchCancellation;
    private int searchGeneration;
    internal event EventHandler? CandidatesUpdated;
    internal event EventHandler? SearchSettled;
    internal IReadOnlyList<SearchHit> SearchCandidates => candidates.Hits;
    internal bool CandidatesVisible => candidates.Visible;

    public int PaneNumber { get; }
    public int SelectedIndex => selectedIndex;
    public int TabCount => tabs.Count;
    public ShellViewHost? ActiveView => selectedIndex >= 0 ? tabs[selectedIndex].Host : null;
    public string CurrentPath => ActiveView?.LocationPath ?? "";
    internal IEnumerable<string> RecentPaths => closedTabs.Take(4);
    public event EventHandler? Activated;
    public event EventHandler? StateChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<string>? FavoriteRequested;
    public event Action<ExplorerPane, string, int>? MoveTabRequested;
    internal event Action<ExplorerPane, ShellViewHost>? TabDragRequested;
    internal event Action<ExplorerPane, ShellViewHost, SplitEdge>? SplitTabRequested;
    internal Rectangle FileAreaOnScreen => contents.RectangleToScreen(contents.ClientRectangle);

    public ExplorerPane(int number, PaneState state, UiText? text = null,
        IPathSearchBackend? search = null, Func<IReadOnlyList<string>>? searchRoots = null)
    {
        this.text = text ?? new UiText();
        this.search = search;
        this.searchRoots = searchRoots;
        candidates = new AddressCandidates(number);
        candidates.Chosen += OpenCandidate;
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await QueryCandidates(); };
        if (search is not null) search.IndexChanged += SearchIndexChanged;
        PaneNumber = number;
        Dock = DockStyle.Fill;
        BackColor = Theme.Border;
        Padding = new Padding(1);
        AccessibleName = $"Pane {number}";

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var navigation = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 7, RowCount = 1,
            Padding = new Padding(5, 4, 5, 4), BackColor = Color.White
        };
        for (int i = 0; i < 3; i++) navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
            if (candidates.Visible && e.KeyCode is Keys.Down or Keys.Up)
            {
                candidates.MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (candidates.Visible && candidates.SelectedHit is { } hit) OpenCandidate(hit);
                else { CloseCandidates(); Navigate(address.Text); }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                if (candidates.Visible) CloseCandidates();
                else { address.Text = CurrentPath; FocusFiles(); }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Tab) CloseCandidates();
        };
        navigation.Controls.Add(address, 3, 0);
        languageBindings.Add(RefreshAddressTip);
        RefreshAddressTip();
        var go = Theme.Button("\uE8AD", $"Pane {number} go to address", () => Navigate(address.Text));
        go.Font = new Font("Segoe Fluent Icons", 10);
        go.AutoSize = true;
        go.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        go.Padding = new Padding(5);
        BindTip(go, "Go to typed address (Enter)");
        navigation.Controls.Add(go, 4, 0);
        AddButton(navigation, 5, "\uE838", "Choose folder", ChooseFolder);
        AddButton(navigation, 6, "\uE713", "Pane options", ShowOptions);

        var newTab = new OutlinedToolStripButton("+")
        {
            AutoSize = false, Width = 30, Margin = new Padding(12, 1, 0, 2), // gap keeps it away from the last tab's close button
            AccessibleName = $"Pane {number} new tab", ToolTipText = this.text.T("New tab (Ctrl+T)"),
            Overflow = ToolStripItemOverflow.Never
        };
        languageBindings.Add(() => newTab.ToolTipText = this.text.T("New tab (Ctrl+T)"));
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
        address.TextChanged += (_, _) => ScheduleSearch();
        address.GotFocus += (_, _) => ScheduleSearch();
        address.LostFocus += (_, _) =>
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                if (!IsDisposed && !address.Focused && !candidates.Bounds.Contains(Cursor.Position))
                    CloseCandidates();
            });
        };
        SizeChanged += (_, _) => CloseCandidates();
        VisibleChanged += (_, _) => { if (!Visible) CloseCandidates(); };
        RefreshLanguage();
    }

    private void ScheduleSearch()
    {
        CloseCandidates();
        bool rooted = SearchQuery.IsRootedInput(address.Text);
        bool enabled = search is { IsAvailable: true };
        RefreshAddressTip();
        var completion = !enabled || rooted ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None;
        if (address.AutoCompleteMode != completion && IsHandleCreated)
            BeginInvoke(() =>
            {
                if (IsDisposed || address.IsDisposed) return;
                var current = search is not { IsAvailable: true } || SearchQuery.IsRootedInput(address.Text)
                    ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None;
                if (address.AutoCompleteMode != current) address.AutoCompleteMode = current;
            });
        if (!address.Focused || search is not { IsAvailable: true } || rooted || address.Text.Trim().Length < 2) return;
        if (!search.IsReady)
        {
            candidates.Present(address, [], text.T("Building search index..."));
            CandidatesUpdated?.Invoke(this, EventArgs.Empty);
        }
        searchTimer.Start();
    }

    private async Task QueryCandidates()
    {
        if (!address.Focused || search is not { IsAvailable: true }) return;
        int generation = searchGeneration;
        string input = address.Text;
        var source = new CancellationTokenSource();
        searchCancellation = source;
        try
        {
            if (!search.IsReady) { candidates.Present(address, [], text.T("Building search index...")); return; }
            var roots = searchRoots?.Invoke() ?? new[] { CurrentPath };
            var hits = await Task.Run(() => search.QueryAsync(input, roots, 50, source.Token), source.Token);
            if (source.IsCancellationRequested || IsDisposed || generation != searchGeneration ||
                address.Text != input || !address.Focused) return;
            candidates.Present(address, hits);
            CandidatesUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or Win32Exception)
        {
            if (!IsDisposed && generation == searchGeneration)
            {
                candidates.Hide();
                SetLocalizedStatus("Search failed: {0}", ex.Message);
                CandidatesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            if (ReferenceEquals(searchCancellation, source)) searchCancellation = null;
            source.Dispose();
            if (!IsDisposed) SearchSettled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SearchIndexChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (search is not { IsAvailable: true }) { CloseCandidates(); return; }
                // Whole-volume changes must not remove a row while the user is choosing it.
                // A text edit refreshes results; opening still checks target existence.
                if (candidates.Visible && candidates.Hits.Count > 0) return;
                if (searchCancellation is null) ScheduleSearch();
            });
        }
        catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated) { }
    }

    private void CloseCandidates()
    {
        searchGeneration++;
        searchTimer.Stop();
        searchCancellation?.Cancel();
        candidates.Hide();
    }

    private void OpenCandidate(SearchHit hit)
    {
        CloseCandidates();
        if (hit.IsDirectory)
        {
            if (Directory.Exists(hit.FullPath)) Navigate(hit.FullPath);
            else SetLocalizedStatus("No longer available: {0}", hit.FullPath);
        }
        else if (File.Exists(hit.FullPath))
        {
            if (ActiveView is { } view) view.NavigateAndSelect(hit.FullPath);
            else AddTab(hit.FullPath);
        }
        else SetLocalizedStatus("No longer available: {0}", hit.FullPath);
    }

    private void AddButton(TableLayoutPanel row, int column, string glyph, string name, Action action)
    {
        var button = Theme.Button(glyph, $"Pane {PaneNumber} {name}", action);
        button.Font = new Font("Segoe Fluent Icons", 10);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(5);
        BindTip(button, name switch
        {
            "Choose folder" => "Browse for a folder: open folder picker",
            "Pane options" => "Pane options: view mode, refresh, favorites",
            _ => name
        });
        row.Controls.Add(button, column, 0);
    }

    private void BindTip(Control control, string key)
    {
        void Refresh() => tips.SetToolTip(control, text.T(key));
        languageBindings.Add(Refresh);
        Refresh();
    }

    internal string ToolTipFor(Control control) => tips.GetToolTip(control) ?? "";

    private void RefreshAddressTip() => tips.SetToolTip(address, text.T(search is { IsAvailable: true }
        ? "Enter a folder path, or type part of a file/folder name to search; Ctrl+L to select address"
        : "Enter a folder path; Enter to navigate, Ctrl+L to select address"));

    internal void RefreshLanguage()
    {
        if (candidates.Visible) ScheduleSearch();
        foreach (var refresh in languageBindings) refresh();
        emptyHint.Text = text.T("Enter a folder path above or choose a folder from Pane options.");
        foreach (var tab in tabs)
        {
            tab.Host.RefreshLanguage();
            tab.Button.Text = ShortTitle(tab.Host.DisplayName);
        }
        tabMenu.Close();
        RenderStatus(localizedStatus is not null ? localizedStatus() : statusMessage);
    }

    internal void RefreshTheme()
    {
        Theme.Apply(this);
        BackColor = Theme.Border;
        contents.BackColor = emptyHint.BackColor = status.BackColor = address.BackColor = Theme.Surface;
        emptyHint.ForeColor = status.ForeColor = Theme.Muted;
        address.ForeColor = Theme.Text;
        foreach (var tab in tabs)
        {
            tab.Button.ForeColor = tab == tabs.ElementAtOrDefault(selectedIndex) ? Theme.Accent : Theme.Text;
            if (tab.Host.IsHandleCreated) Theme.ApplyNative(tab.Host.Handle);
            tab.Host.ApplyThemeColors();
        }
        candidates.RefreshTheme();
    }

    public void AddTab(string path, bool select = true)
    {
        if (tabs.Count >= 32) { SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first."); return; }
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? selectFile = File.Exists(path) ? Path.GetFullPath(path) : null;
        var host = new ShellViewHost(selectFile is null ? path : Path.GetDirectoryName(selectFile)!, text) { Visible = false };
        if (selectFile is not null) host.NavigateAndSelect(selectFile);
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
        host.SelectionChanged += (_, _) => { if (host == ActiveView) SelectionChanged?.Invoke(this, EventArgs.Empty); };
        host.Navigated += (_, _) =>
        {
            Theme.ApplyNative(host.Handle);
            button.Text = ShortTitle(host.DisplayName);
            button.ToolTipText = host.LocationPath;
            button.AccessibleName = $"Folder tab: {host.DisplayName}";
            if (host == ActiveView) { address.Text = host.LocationPath; UpdateStatus(); }
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        host.NavigationError += (_, message) => SetLocalizedStatus(() => host.ErrorStatus ?? message);
        // Tab removal hides the host while the selected index is still being updated.
        host.ItemCountChanged += (_, _) =>
        {
            if (selectedIndex >= 0 && selectedIndex < tabs.Count && tabs[selectedIndex].Host == host) RenderItemCount();
        };
        tabs.Add(tab);
        tabStrip.Items.Insert(tabStrip.Items.Count - 1, button);
        contents.Controls.Add(host);
        if (select) SelectTab(tabs.Count - 1);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectTab(int index, bool focus = true)
    {
        CloseCandidates();
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
    public void SetStatus(string message) { localizedStatus = null; RenderStatus(message); }
    private void RenderStatus(string message)
    {
        statusMessage = message;
        RenderItemCount();
    }
    private void RenderItemCount()
    {
        status.Text = ActiveView is null ? statusMessage : statusMessage + "  |  " + (ActiveView.ItemCount is int value
            ? text.T("{0} items", value) : text.T("Counting items..."));
        tips.SetToolTip(status, status.Text);
    }
    internal void SetLocalizedStatus(string key, params object?[] args) => SetLocalizedStatus(() => text.T(key, args));
    internal void SetLocalizedStatus(Func<string> message) { localizedStatus = message; RenderStatus(message()); }
    private void UpdateStatus() => SetLocalizedStatus(() => tabs.Count == 1
        ? text.T("Pane {0}  |  1 tab  |  {1}", PaneNumber, ActiveView?.DisplayName)
        : text.T("Pane {0}  |  {1} tabs  |  {2}", PaneNumber, tabs.Count, ActiveView?.DisplayName));
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
        tabMenu.Items.Add(text.T("New tab"), null, (_, _) => AddTab(CurrentPath));
        tabMenu.Items.Add(text.T("Duplicate tab"), null, (_, _) => AddTab(CurrentPath));
        tabMenu.Items.Add(text.T("Reopen closed tab"), null, (_, _) => ReopenTab()).Enabled = closedTabs.Count > 0;
        var move = new ToolStripMenuItem(text.T("Move tab to pane"));
        for (int i = 1; i <= 4; i++)
        {
            int target = i;
            if (target == PaneNumber) continue;
            move.DropDownItems.Add(text.T("Pane {0}", target), null, (_, _) => MoveTabRequested?.Invoke(this, CurrentPath, target));
        }
        tabMenu.Items.Add(move);
        var split = new ToolStripMenuItem(text.T("Split tab"));
        foreach (SplitEdge edge in Enum.GetValues<SplitEdge>())
        {
            var side = edge;
            split.DropDownItems.Add(text.T(side.ToString()), null, (_, _) =>
            {
                if (ActiveView is { } view) SplitTabRequested?.Invoke(this, view, side);
            });
        }
        tabMenu.Items.Add(split);
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(text.T("Close tab    Ctrl+W"), null, (_, _) => CloseTab());
        tabMenu.Items.Add(text.T("Close other tabs"), null, (_, _) =>
        {
            var keep = tabs[selectedIndex];
            for (int i = tabs.Count - 1; i >= 0; i--) if (tabs[i] != keep) CloseTab(i);
        }).Enabled = tabs.Count > 1;
        tabMenu.Show(Cursor.Position);
    }

    private void ShowOptions()
    {
        tabMenu.Items.Clear();
        tabMenu.Items.Add(text.T("Refresh    F5"), null, (_, _) => ActiveView?.RefreshFolder());
        tabMenu.Items.Add(text.T("Add to favorites    Ctrl+D"), null, (_, _) => FavoriteRequested?.Invoke(this, CurrentPath));
        tabMenu.Items.Add(text.T("Copy folder path"), null, (_, _) => CopyPath());
        tabMenu.Items.Add(text.T("Open in File Explorer"), null, (_, _) => OpenInExplorer());
        tabMenu.Items.Add(text.T("Choose folder..."), null, (_, _) => ChooseFolder());
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(text.T("Details view"), null, (_, _) => ActiveView?.SetViewMode(4));
        tabMenu.Items.Add(text.T("List view"), null, (_, _) => ActiveView?.SetViewMode(3));
        tabMenu.Items.Add(text.T("Icons view"), null, (_, _) => ActiveView?.SetViewMode(1));
        tabMenu.Show(Cursor.Position);
    }

    private void ChooseFolder()
    {
        using var picker = new FolderBrowserDialog { Description = text.T("Choose folder"), UseDescriptionForTitle = true };
        if (Directory.Exists(CurrentPath)) picker.InitialDirectory = CurrentPath;
        if (picker.ShowDialog(this) == DialogResult.OK) Navigate(picker.SelectedPath);
    }

    public void CopyPath()
    {
        if (ActiveView is null) return;
        try { Clipboard.SetText(CurrentPath); SetLocalizedStatus("Folder path copied."); }
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
        if (disposing)
        {
            CloseCandidates();
            if (search is not null) search.IndexChanged -= SearchIndexChanged;
            searchTimer.Dispose();
            candidates.Dispose();
            tips.Dispose();
            tabMenu.Dispose();
            folderIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
