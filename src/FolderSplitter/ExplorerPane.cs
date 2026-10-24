using System.ComponentModel;
using System.Diagnostics;
using QuadExplorer.Search;

namespace QuadExplorer;

public sealed partial class ExplorerPane : UserControl
{
    private sealed class FolderTab(FolderTabButton button)
    {
        public ShellViewHost? Host { get; set; }
        public FolderTabButton Button { get; } = button;
    }
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
    private readonly ToolStripButton newTab;
    private readonly ToolStripDropDownButton tabList;
    private int firstVisibleTab;
    private int tabWheel;
    private bool layingOutTabs;
    private readonly ToolStrip breadcrumb = Theme.Strip();
    private string? selectionSummary;
    private int selectionGeneration;
    private (string Path, string Text)? folderSize;
    private int folderSizeGeneration;
    private readonly System.Windows.Forms.Timer folderSizeTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer selectionTimer = new() { Interval = 150 };
    internal event EventHandler? SelectionSummaryChanged;
    internal event Action<ExplorerPane, bool>? TransferRequested;
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
        BackColor = Theme.Canvas;
        UpdateCardPadding(); // room for the card's edge and soft shadow
        DpiChangedAfterParent += (_, _) => UpdateCardPadding();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
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
        var back = AddButton(navigation, 0, "\uE72B", "Back (Alt+Left)", () => ActiveView?.Travel(0x4000));
        var forward = AddButton(navigation, 1, "\uE72A", "Forward (Alt+Right)", () => ActiveView?.Travel(0x8000));
        foreach (var travel in new[] { back, forward })
            travel.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowRecentLocations(travel); };
        AddButton(navigation, 2, "\uE74A", "Up (Alt+Up)", () => ActiveView?.Travel(0x2000));
        address = new TextBox
        {
            Dock = DockStyle.Fill, Margin = new Padding(12, 9, 10, 8),
            BorderStyle = BorderStyle.None, AccessibleName = $"Pane {number} address",
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
        // The address box is drawn as the same rounded field as the folder search box.
        navigation.Paint += (_, e) => Theme.PaintField(e.Graphics,
            Rectangle.Inflate(address.Bounds, (int)Math.Round(7 * DeviceDpi / 96d), (int)Math.Round(5 * DeviceDpi / 96d)), address.Focused, DeviceDpi);
        address.GotFocus += (_, _) => navigation.Invalidate();
        address.LostFocus += (_, _) => navigation.Invalidate();
        // The breadcrumb sits inside the address box and gives way to plain text while it has focus.
        breadcrumb.Dock = DockStyle.Fill;
        breadcrumb.AutoSize = false;
        breadcrumb.CanOverflow = false;
        breadcrumb.Padding = new Padding(1, 0, 1, 0);
        breadcrumb.AccessibleName = $"Pane {number} breadcrumb";
        breadcrumb.Visible = false;
        breadcrumb.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && breadcrumb.GetItemAt(e.Location) is null) FocusAddress();
        };
        breadcrumb.SizeChanged += (_, _) => { if (breadcrumb.Visible) FitBreadcrumb(); };
        address.Controls.Add(breadcrumb);
        address.GotFocus += (_, _) => breadcrumb.Visible = false;
        selectionTimer.Tick += async (_, _) => { selectionTimer.Stop(); await UpdateSelectionSummary(); };
        folderSizeTimer.Tick += async (_, _) => { folderSizeTimer.Stop(); await UpdateFolderSize(); };
        languageBindings.Add(RefreshAddressTip);
        RefreshAddressTip();
        var go = Theme.Button("\uE8AD", $"Pane {number} go to address", () => Navigate(address.Text), quiet: true);
        go.Font = new Font(Theme.IconFont, 10);
        go.AutoSize = true;
        go.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        go.Padding = new Padding(5);
        BindTip(go, "Go to typed address (Enter)");
        navigation.Controls.Add(go, 4, 0);
        AddButton(navigation, 5, "\uE838", "Choose folder", ChooseFolder);
        AddButton(navigation, 6, "\uE713", "Pane options", ShowOptions);

        newTab = new ToolStripButton("\uE710")
        {
            Font = new Font(Theme.IconFont, 9),
            AutoSize = false, Width = 30, Margin = new Padding(12, 1, 0, 2), // gap keeps it away from the last tab's close button
            AccessibleName = $"Pane {number} new tab", ToolTipText = this.text.T("New tab (Ctrl+T)"),
            Overflow = ToolStripItemOverflow.Never
        };
        languageBindings.Add(() => newTab.ToolTipText = this.text.T("New tab (Ctrl+T)"));
        newTab.Click += (_, _) => AddBlankTab();
        tabStrip.Items.Add(newTab);
        tabList = new ToolStripDropDownButton
        {
            AccessibleName = $"Pane {number} tab list", ToolTipText = this.text.T("All tabs"),
            Text = "\uE70D", Font = new Font(Theme.IconFont, 8), ShowDropDownArrow = false,
            Overflow = ToolStripItemOverflow.Never, Visible = false, Margin = new Padding(4, 1, 0, 2)
        };
        languageBindings.Add(() => tabList.ToolTipText = this.text.T("All tabs"));
        tabList.DropDownOpening += (_, _) => Theme.Apply(tabStrip);
        tabStrip.Items.Add(tabList);
        // Tabs are sized and windowed here, so nothing is ever pushed into the hidden overflow menu.
        tabStrip.CanOverflow = false;
        tabStrip.Padding = new Padding(6, 4, 6, 0); // the viewed tab meets the address row below it
        tabStrip.SizeChanged += (_, _) => LayoutTabs();
        tabStrip.FontChanged += (_, _) => LayoutTabs();
        tabStrip.DpiChangedAfterParent += (_, _) => LayoutTabs();
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
        body.Controls.Add(BuildCommands());
        body.Controls.Add(navigation);
        body.Controls.Add(tabStrip);
        Controls.Add(body);
        body.Resize += (_, _) => { body.Region?.Dispose(); body.Region = Theme.CardRegion(body.Size, DeviceDpi); };
        // The status line is a caption: a step smaller than the body text.
        FontChanged += (_, _) => status.Font = new Font(Font.FontFamily, Math.Max(7f, Font.SizeInPoints - 1f));
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
                {
                    CloseCandidates();
                    ShowBreadcrumb();
                }
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

    private Button AddButton(TableLayoutPanel row, int column, string glyph, string name, Action action)
    {
        var button = Theme.Button(glyph, $"Pane {PaneNumber} {name}", action, quiet: true);
        button.Font = new Font(Theme.IconFont, 10);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(5);
        BindTip(button, name switch
        {
            "Choose folder" => "Browse for a folder: open folder picker",
            "Pane options" => "Pane options: view mode, refresh, favorites",
            "Back (Alt+Left)" => "Back (Alt+Left); right-click for recent locations",
            "Forward (Alt+Right)" => "Forward (Alt+Right); right-click for recent locations",
            _ => name
        });
        row.Controls.Add(button, column, 0);
        return button;
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
            tab.Host?.RefreshLanguage();
            tab.Button.Title = tab.Host?.DisplayName ?? text.T("New tab");
        }
        LayoutTabs();
        ShowBreadcrumb();
        if (selectionSummary is not null) ScheduleSelectionSummary();
        tabMenu.Close();
        RenderStatus(localizedStatus is not null ? localizedStatus() : statusMessage);
    }

    internal void RefreshTheme()
    {
        Theme.Apply(this);
        BackColor = Theme.Canvas;
        contents.BackColor = emptyHint.BackColor = status.BackColor = address.BackColor = Theme.Surface;
        emptyHint.ForeColor = status.ForeColor = Theme.Muted;
        address.ForeColor = Theme.Text;
        foreach (var tab in tabs)
        {
            tab.Button.ForeColor = tab == tabs.ElementAtOrDefault(selectedIndex) ? Theme.Text : Theme.Muted;
            if (tab.Host is not { } host) continue;
            if (host.IsHandleCreated) Theme.ApplyNative(host.Handle);
            host.ApplyThemeColors();
        }
        candidates.RefreshTheme();
    }

    public void AddTab(string path, bool select = true) => InsertTab(path, select);

    // A tab with no folder yet: the address bar takes focus so the user can type a path or search.
    public void AddBlankTab() => InsertTab(null, select: true);

    private void InsertTab(string? path, bool select)
    {
        if (tabs.Count >= 32) { SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first."); return; }
        string title = text.T("New tab");
        var button = new FolderTabButton
        {
            Title = title, Text = title, Image = folderIcon, AutoSize = false,
            Width = 164, TextAlign = ContentAlignment.MiddleLeft, Overflow = ToolStripItemOverflow.Never,
            ImageAlign = ContentAlignment.MiddleLeft, AccessibleName = $"Folder tab: {title}",
            Margin = new Padding(1, 0, 1, 0)
        };
        var tab = new FolderTab(button);
        button.CloseRequested += (_, _) => CloseTab(tabs.IndexOf(tab));
        button.DragRequested += (_, _) => { if (tab.Host is { } host) TabDragRequested?.Invoke(this, host); };
        button.Click += (_, _) => SelectTab(tabs.IndexOf(tab));
        button.MouseDown += (_, e) =>
        {
            int index = tabs.IndexOf(tab);
            if (e.Button == MouseButtons.Middle) CloseTab(index);
            else if (e.Button == MouseButtons.Right) ShowTabMenu(index);
        };
        tabs.Add(tab);
        tabStrip.Items.Insert(tabStrip.Items.IndexOf(newTab), button);
        if (path is not null) AttachHost(tab, path);
        if (select) SelectTab(tabs.Count - 1);
        else LayoutTabs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AttachHost(FolderTab tab, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? selectFile = File.Exists(path) ? Path.GetFullPath(path) : null;
        var host = new ShellViewHost(selectFile is null ? path : Path.GetDirectoryName(selectFile)!, text) { Visible = false };
        if (selectFile is not null) host.NavigateAndSelect(selectFile);
        tab.Host = host;
        var button = tab.Button;
        button.Title = host.DisplayName;
        button.ToolTipText = path;
        button.AccessibleName = $"Folder tab: {host.DisplayName}";
        host.SelectionChanged += (_, _) =>
        {
            if (host != ActiveView) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ScheduleSelectionSummary();
            UpdateCommands();
        };
        host.Navigated += (_, _) =>
        {
            Theme.ApplyNative(host.Handle);
            host.ApplyListFont();
            button.Title = host.DisplayName;
            button.ToolTipText = host.LocationPath;
            LayoutTabs();
            button.AccessibleName = $"Folder tab: {host.DisplayName}";
            if (host == ActiveView) { selectionSummary = null; address.Text = host.LocationPath; UpdateStatus(); ShowBreadcrumb(); }
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        host.NavigationError += (_, message) => SetLocalizedStatus(() => host.ErrorStatus ?? message);
        host.FilterChanged += (_, _) => { if (host == ActiveView) UpdateCommands(); };
        host.Navigated += (_, _) => { if (host == ActiveView) UpdateCommands(); };
        // Tab removal hides the host while the selected index is still being updated.
        host.ItemCountChanged += (_, _) =>
        {
            if (selectedIndex >= 0 && selectedIndex < tabs.Count && tabs[selectedIndex].Host == host)
            {
                RenderItemCount();
                ScheduleFolderSize();
            }
        };
        contents.Controls.Add(host);
    }

    internal void RefreshViews()
    {
        foreach (var tab in tabs) tab.Host?.RefreshFolder();
    }

    public void SelectTab(int index, bool focus = true)
    {
        CloseCandidates();
        if (index < 0 || index >= tabs.Count) return;
        if (selectedIndex >= 0 && selectedIndex < tabs.Count && tabs[selectedIndex].Host is { } previous) previous.Visible = false;
        selectedIndex = index;
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].Button.Checked = i == index;
            tabs[i].Button.ForeColor = i == index ? Theme.Text : Theme.Muted;
        }
        selectionSummary = null;
        var current = tabs[index].Host;
        emptyHint.Visible = current is null;
        if (current is not null)
        {
            current.Visible = true;
            current.BringToFront();
            address.Text = current.LocationPath;
        }
        else
        {
            emptyHint.BringToFront();
            address.Clear();
        }
        UpdateStatus();
        LayoutTabs();
        ShowBreadcrumb();
        UpdateCommands();
        if (focus)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            if (current is not null) current.FocusView();
            else FocusAddress();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseTab(int index = -1)
    {
        if (index < 0) index = selectedIndex;
        if (index < 0 || index >= tabs.Count) return;
        var tab = tabs[index];
        int nextIndex = selectedIndex > index ? selectedIndex - 1 : Math.Min(selectedIndex, tabs.Count - 2);
        tabs.RemoveAt(index);
        tabStrip.Items.Remove(tab.Button);
        if (tab.Host is { } host)
        {
            closedTabs.Push(host.LocationPath);
            if (closedTabs.Count > 32)
            {
                var recent = closedTabs.Take(32).Reverse().ToArray();
                closedTabs.Clear();
                foreach (string path in recent) closedTabs.Push(path);
            }
            contents.Controls.Remove(host);
            host.Dispose();
        }
        tab.Button.Dispose();
        selectedIndex = -1;
        if (tabs.Count == 0) { ShowEmptyState(); LayoutTabs(); }
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
        else if (selectedIndex >= 0 && tabs[selectedIndex].Host is null)
        {
            AttachHost(tabs[selectedIndex], path);
            SelectTab(selectedIndex);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        else AddTab(path);
    }

    public void FocusAddress() { breadcrumb.Visible = false; address.Focus(); address.SelectAll(); }
    public void FocusFiles()
    {
        if (ActiveView is { } view) view.FocusView();
        else FocusAddress();
    }
    private bool isActive;

    private void UpdateCardPadding()
    {
        int Scaled(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        Padding = new Padding(Scaled(4), Scaled(3), Scaled(4), Scaled(6));
    }

    public void SetActive(bool active)
    {
        activeLine.BackColor = active ? Theme.Accent : Theme.Border;
        if (isActive == active) return;
        isActive = active;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var card = Rectangle.FromLTRB(Padding.Left - 1, Padding.Top - 1, Width - Padding.Right, Height - Padding.Bottom);
        if (card.Width > 2 && card.Height > 2) Theme.PaintCard(e.Graphics, card, isActive, DeviceDpi, Padding.Top + tabStrip.Bottom);
    }
    public void SetStatus(string message) { localizedStatus = null; RenderStatus(message); }
    private void RenderStatus(string message)
    {
        statusMessage = message;
        RenderItemCount();
    }
    private void RenderItemCount()
    {
        status.Text = ActiveView is null ? statusMessage : statusMessage + "  |  " + (ActiveView.ItemCount is int value
            ? text.T("{0} items", value) : text.T("Counting items..."))
            + (folderSize is { } size && size.Path == ActiveView.LocationPath ? "  |  " + size.Text : "")
            + (selectionSummary is null ? "" : "  |  " + selectionSummary);
        tips.SetToolTip(status, status.Text);
    }
    internal void SetLocalizedStatus(string key, params object?[] args) => SetLocalizedStatus(() => text.T(key, args));
    internal void SetLocalizedStatus(Func<string> message) { localizedStatus = message; RenderStatus(message()); }
    private void UpdateStatus()
    {
        ScheduleFolderSize();
        UpdateStatusText();
    }

    private void UpdateStatusText() => SetLocalizedStatus(() => tabs.Count == 1
        ? text.T("Pane {0}  |  1 tab  |  {1}", PaneNumber, ActiveView?.DisplayName)
        : text.T("Pane {0}  |  {1} tabs  |  {2}", PaneNumber, tabs.Count, ActiveView?.DisplayName));
    private void ShowEmptyState()
    {
        address.Clear();
        emptyHint.Visible = true;
        UpdateStatus();
    }
    // Tabs that never opened a folder are not saved.
    public PaneState CaptureState()
    {
        var saved = tabs.Where(tab => tab.Host is not null).ToList();
        int selected = selectedIndex < 0 ? -1 : saved.Count(tab => tabs.IndexOf(tab) < selectedIndex)
            - (tabs[selectedIndex].Host is null ? 1 : 0);
        return new() { Tabs = saved.Select(tab => tab.Host!.LocationPath).ToList(), SelectedTab = Math.Max(0, selected) };
    }
    internal int IndexOfTab(ShellViewHost host) => tabs.FindIndex(tab => tab.Host == host);
    internal ToolStrip TabStrip => tabStrip;
    internal ToolStripDropDownButton TabList => tabList;
    internal IReadOnlyList<FolderTabButton> TabButtons => tabs.Select(tab => tab.Button).ToList();

    // Tabs fit their names between a readable minimum and a maximum (wider when alone). When the
    // row is full every tab shrinks evenly to a floor; past that, a window of tabs containing the
    // selected one is shown and the rest stay reachable from the tab list button.
    private void LayoutTabs()
    {
        if (layingOutTabs) return;
        layingOutTabs = true;
        tabStrip.SuspendLayout();
        try
        {
            int count = tabs.Count;
            float scale = tabStrip.DeviceDpi / 96f;
            int Scaled(int value) => (int)Math.Round(value * scale);
            int chrome = Scaled(70), floor = Scaled(90), margin = 2;
            // Fixed-size items keep the height they were created with; size them from the current font,
            // so a font or DPI change (for example connecting over Remote Desktop) never clips their text.
            int rowHeight = Math.Max(TextRenderer.MeasureText("가Ag", tabStrip.Font).Height, Scaled(16)) + Scaled(8);
            newTab.Size = new Size(Scaled(30), rowHeight);
            int[] natural = tabs.Select(tab => Math.Clamp(TextWidth(tab.Button.Title) + chrome,
                Scaled(110), Scaled(count == 1 ? 320 : 240))).ToArray();
            int available = Math.Max(0, tabStrip.ClientSize.Width - tabStrip.Padding.Horizontal
                - newTab.Width - newTab.Margin.Horizontal - Scaled(6));
            int[] widths = natural;
            int shown = count;
            if (count > 0 && natural.Sum() + margin * count > available)
            {
                int low = 0, high = natural.Max();
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (natural.Sum(width => Math.Min(width, middle)) + margin * count <= available) low = middle;
                    else high = middle - 1;
                }
                if (low >= floor) widths = natural.Select(width => Math.Min(width, low)).ToArray();
                else
                {
                    int room = Math.Max(0, available - Scaled(34));
                    shown = Math.Clamp(room / (floor + margin), 1, count);
                    int each = Math.Max(floor, room / shown - margin);
                    widths = natural.Select(width => Math.Min(width, each)).ToArray();
                }
            }
            int first = Math.Clamp(firstVisibleTab, 0, Math.Max(0, count - shown));
            if (selectedIndex >= 0 && selectedIndex < first) first = selectedIndex;
            else if (selectedIndex >= first + shown) first = selectedIndex - shown + 1;
            firstVisibleTab = first;
            for (int i = 0; i < count; i++)
            {
                var button = tabs[i].Button;
                button.Padding = new Padding(0, 0, button.CloseWidth, 0);
                button.Size = new Size(widths[i], rowHeight);
                button.Text = FitTitle(button.Title, widths[i] - chrome);
                button.Visible = i >= first && i < first + shown;
            }
            tabList.Visible = shown < count;
            foreach (var old in tabList.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(tabs[i].Button.Title)
                {
                    Checked = i == selectedIndex, ToolTipText = tabs[i].Host?.LocationPath ?? ""
                };
                item.Click += (_, _) => SelectTab(index);
                tabList.DropDownItems.Add(item);
            }
        }
        finally
        {
            tabStrip.ResumeLayout();
            layingOutTabs = false;
        }
    }

    private string FitTitle(string title, int width)
    {
        if (TextWidth(title) <= width) return title;
        int low = 0, high = title.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (TextWidth(title[..middle] + "\u2026") <= width) low = middle;
            else high = middle - 1;
        }
        if (low > 0 && char.IsHighSurrogate(title[low - 1])) low--;
        return title[..low] + "\u2026";
    }

    // Tab titles are measured on every resize step; widths are cached per font.
    private readonly Dictionary<string, int> textWidths = [];
    private Font? measuredFont;

    private int TextWidth(string value)
    {
        if (!ReferenceEquals(measuredFont, tabStrip.Font) || textWidths.Count > 4096)
        {
            textWidths.Clear();
            measuredFont = tabStrip.Font;
        }
        if (!textWidths.TryGetValue(value, out int width))
            textWidths[value] = width = TextRenderer.MeasureText(value, tabStrip.Font).Width;
        return width;
    }

    // Over the tab row, wheel up / tilt left selects the previous tab and wheel down / tilt right the next.
    internal bool HandleTabWheel(ref Message message)
    {
        if (message.Msg is not (0x20A or 0x20E) || tabs.Count == 0 || !tabStrip.Visible) return false;
        var point = new Point(unchecked((short)(long)message.LParam), unchecked((short)((long)message.LParam >> 16)));
        if (!tabStrip.RectangleToScreen(tabStrip.ClientRectangle).Contains(point)) return false;
        int delta = unchecked((short)((long)message.WParam >> 16));
        tabWheel += message.Msg == 0x20E ? delta : -delta;
        int steps = tabWheel / 120;
        tabWheel %= 120;
        int target = Math.Clamp(selectedIndex + steps, 0, tabs.Count - 1);
        if (steps != 0 && target != selectedIndex) SelectTab(target);
        return true;
    }

    private void ShowTabMenu(int index)
    {
        if (index < 0) return;
        SelectTab(index, focus: false);
        tabMenu.Items.Clear();
        tabMenu.Items.Add(text.T("New tab"), null, (_, _) => AddBlankTab());
        tabMenu.Items.Add(text.T("Duplicate tab"), null, (_, _) => AddTab(CurrentPath)).Enabled = ActiveView is not null;
        tabMenu.Items.Add(text.T("Reopen closed tab"), null, (_, _) => ReopenTab()).Enabled = closedTabs.Count > 0;
        var move = new ToolStripMenuItem(text.T("Move tab to pane"));
        for (int i = 1; i <= 4; i++)
        {
            int target = i;
            if (target == PaneNumber) continue;
            move.DropDownItems.Add(text.T("Pane {0}", target), null, (_, _) => MoveTabRequested?.Invoke(this, CurrentPath, target));
        }
        move.Enabled = ActiveView is not null;
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
        tabMenu.Items.Add(text.T("Copy path    Ctrl+Shift+C"), null, (_, _) => CopySelectionPaths());
        tabMenu.Items.Add(text.T("Open terminal here"), null, (_, _) => OpenTerminal()).Enabled = Directory.Exists(CurrentPath);
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(text.T("Copy selection to other pane    Ctrl+Alt+C"), null, (_, _) => TransferRequested?.Invoke(this, false));
        tabMenu.Items.Add(text.T("Move selection to other pane    Ctrl+Alt+X"), null, (_, _) => TransferRequested?.Invoke(this, true));
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

    internal ToolStrip Breadcrumb => breadcrumb;
    internal string? SelectionSummary => selectionSummary;

    internal void ReplaceTabs(PaneState state)
    {
        while (tabs.Count > 0) CloseTab(tabs.Count - 1);
        foreach (string path in state.Tabs) AddTab(path, select: false);
        if (tabs.Count > 0) SelectTab(Math.Clamp(state.SelectedTab, 0, tabs.Count - 1), focus: false);
    }

    // Path segments for the breadcrumb: the root (drive or UNC share) and each folder below it.
    // Shell locations without a file-system path (This PC, Recycle Bin) have no segments.
    internal static List<(string Label, string Path)> PathSegments(string path)
    {
        var segments = new List<(string, string)>();
        if (string.IsNullOrEmpty(path) || path.StartsWith("::", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path))
            return segments;
        string root = Path.GetPathRoot(path)!;
        string current = root.EndsWith('\\') ? root : root + "\\";
        segments.Add((root.TrimEnd('\\'), current));
        foreach (string part in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            segments.Add((part, current));
        }
        return segments;
    }

    private void ShowBreadcrumb()
    {
        bool show = ActiveView is not null && !address.Focused && address.Text == CurrentPath;
        if (show) RebuildBreadcrumb();
        breadcrumb.Visible = show;
    }

    private string? breadcrumbPath;
    private readonly List<(ToolStripItem[] Items, int Width)> breadcrumbGroups = [];
    private List<(string Label, string Path)> breadcrumbSegments = [];
    private ToolStripDropDownButton? breadcrumbOverflow;

    private void RebuildBreadcrumb()
    {
        if (breadcrumbPath == CurrentPath && breadcrumb.Items.Count > 0) { FitBreadcrumb(); return; }
        breadcrumb.SuspendLayout();
        try
        {
            foreach (var old in breadcrumb.Items.Cast<ToolStripItem>().ToArray()) old.Dispose();
            breadcrumbGroups.Clear();
            breadcrumbOverflow = null;
            breadcrumbPath = CurrentPath;
            breadcrumbSegments = PathSegments(CurrentPath);
            if (breadcrumbSegments.Count == 0)
            {
                breadcrumb.Items.Add(new ToolStripLabel(ActiveView?.DisplayName ?? "") { Margin = new Padding(4, 0, 0, 0) });
                return;
            }
            foreach (var (label, path) in breadcrumbSegments)
            {
                string target = path;
                var segment = new ToolStripButton(label) { Margin = new Padding(0), Padding = new Padding(2, 0, 2, 0), ToolTipText = target };
                segment.Click += (_, _) => Navigate(target);
                var chevron = new ToolStripDropDownButton("\u203A")
                {
                    ShowDropDownArrow = false, Margin = new Padding(0), Padding = new Padding(1, 0, 1, 0),
                    AccessibleName = $"Subfolders of {label}"
                };
                chevron.DropDownItems.Add("\u2026");
                chevron.DropDownOpening += (_, _) => FillSubfolders(chevron, target);
                breadcrumb.Items.Add(segment);
                breadcrumb.Items.Add(chevron);
                ToolStripItem[] items = [segment, chevron];
                breadcrumbGroups.Add((items, items.Sum(item => item.GetPreferredSize(Size.Empty).Width + item.Margin.Horizontal)));
            }
        }
        finally { breadcrumb.ResumeLayout(); }
        FitBreadcrumb();
    }

    // Keep the deepest folders; ancestors that do not fit move into a leading dropdown.
    private void FitBreadcrumb()
    {
        if (breadcrumbGroups.Count == 0) return;
        int available = breadcrumb.ClientSize.Width - breadcrumb.Padding.Horizontal - 4;
        int overflowWidth = 24, used = 0, first = breadcrumbGroups.Count - 1;
        for (int i = breadcrumbGroups.Count - 1; i >= 0; i--)
        {
            int width = breadcrumbGroups[i].Width;
            if (i < breadcrumbGroups.Count - 1 && used + width > available - (i > 0 ? overflowWidth : 0)) break;
            used += width;
            first = i;
        }
        int hiddenNow = breadcrumbOverflow?.DropDownItems.Count ?? 0;
        if (hiddenNow == first) return;
        breadcrumb.SuspendLayout();
        try
        {
            for (int i = 0; i < breadcrumbGroups.Count; i++)
                foreach (var item in breadcrumbGroups[i].Items) item.Visible = i >= first;
            breadcrumbOverflow?.Dispose();
            breadcrumbOverflow = null;
            if (first == 0) return;
            var hidden = new ToolStripDropDownButton("\u00AB")
            {
                ShowDropDownArrow = false, Margin = new Padding(0), Padding = new Padding(2, 0, 2, 0),
                AccessibleName = "Hidden path folders"
            };
            for (int i = first - 1; i >= 0; i--)
            {
                string target = breadcrumbSegments[i].Path;
                hidden.DropDownItems.Add(breadcrumbSegments[i].Label, null, (_, _) => Navigate(target));
            }
            hidden.DropDownOpening += (_, _) => Theme.Apply(breadcrumb);
            breadcrumb.Items.Insert(0, hidden);
            breadcrumbOverflow = hidden;
        }
        finally { breadcrumb.ResumeLayout(); }
    }

    private void FillSubfolders(ToolStripDropDownButton chevron, string folder)
    {
        foreach (var old in chevron.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
        try
        {
            var names = new DirectoryInfo(folder).EnumerateDirectories()
                .Where(directory => (directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Select(directory => directory.FullName).Take(500).ToList();
            names.Sort((left, right) => StrCmpLogicalW(Path.GetFileName(left), Path.GetFileName(right)));
            foreach (string path in names)
                chevron.DropDownItems.Add(Path.GetFileName(path), null, (_, _) => Navigate(path));
            if (names.Count == 0) chevron.DropDownItems.Add(text.T("(No subfolders)")).Enabled = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            chevron.DropDownItems.Add(text.T("(Cannot open this folder)")).Enabled = false;
        }
        Theme.Apply(breadcrumb);
    }

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string left, string right);

    private void ShowRecentLocations(Control anchor)
    {
        tabMenu.Items.Clear();
        var places = ActiveView?.RecentLocations
            .Where(place => !string.Equals(place, CurrentPath, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        if (places.Count == 0) tabMenu.Items.Add(text.T("No recent locations")).Enabled = false;
        foreach (string place in places) tabMenu.Items.Add(place, null, (_, _) => Navigate(place));
        tabMenu.Show(anchor, new Point(0, anchor.Height));
    }

    internal IReadOnlyList<string> RecentLocationChoices => ActiveView?.RecentLocations
        .Where(place => !string.Equals(place, CurrentPath, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];

    private void ScheduleFolderSize()
    {
        folderSizeTimer.Stop();
        folderSizeTimer.Start();
    }

    // Total size of the files directly in this folder, like File Explorer's status bar without a selection.
    // Subfolders are not walked, so this stays one directory read; network folders are skipped.
    internal async Task UpdateFolderSize()
    {
        int generation = ++folderSizeGeneration;
        string? path = ActiveView?.LocationPath;
        string? result = path is null ? null : await Task.Run(() => FolderFilesSize(path, 50_000));
        if (generation != folderSizeGeneration || IsDisposed || path != ActiveView?.LocationPath) return;
        folderSize = result is null ? null : (path!, text.T("Files here: {0}", result));
        RenderItemCount();
    }

    internal static string? FolderFilesSize(string path, int limit)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(path)) return null;
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) return null;
            long total = 0;
            int count = 0;
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                if (++count > limit) return FormatSize(total) + "+";
                total += file.Length;
            }
            return count == 0 ? null : FormatSize(total);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException) { return null; }
    }

    private void ScheduleSelectionSummary()
    {
        selectionTimer.Stop();
        selectionTimer.Start();
    }

    // "3 selected · 1.2 GB" in the status line. Sizes add up selected files; folders are counted only.
    internal async Task UpdateSelectionSummary()
    {
        int generation = ++selectionGeneration;
        var view = ActiveView;
        var (paths, count) = view?.SelectedPaths(5000) ?? ([], 0);
        if (count == 0)
        {
            selectionSummary = null;
            RenderItemCount();
            SelectionSummaryChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        long bytes = await Task.Run(() =>
        {
            long total = 0;
            foreach (string path in paths)
            {
                try { var file = new FileInfo(path); if (file.Exists) total += file.Length; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            return total;
        });
        if (generation != selectionGeneration || IsDisposed || view != ActiveView) return;
        selectionSummary = bytes > 0 ? text.T("{0} selected · {1}", count, FormatSize(bytes)) : text.T("{0} selected", count);
        RenderItemCount();
        SelectionSummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static string FormatSize(long bytes)
    {
        var buffer = new System.Text.StringBuilder(32);
        return StrFormatByteSizeEx((ulong)bytes, 1, buffer, (uint)buffer.Capacity) >= 0 ? buffer.ToString() : bytes + " B";
    }

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrFormatByteSizeEx(ulong size, int flags, System.Text.StringBuilder buffer, uint length);

    // Like File Explorer's Copy as path: selected items, each quoted, one per line; the folder itself when nothing is selected.
    public void CopySelectionPaths()
    {
        if (ActiveView is not { } view) return;
        var (paths, _) = view.SelectedPaths();
        IReadOnlyList<string> chosen = paths.Count > 0 ? paths : [CurrentPath];
        try
        {
            Clipboard.SetText(PathsForClipboard(chosen));
            SetLocalizedStatus("Path copied: {0}", chosen.Count == 1 ? chosen[0] : text.T("{0} items", chosen.Count));
        }
        catch (System.Runtime.InteropServices.ExternalException ex) { SetStatus(ex.Message); }
    }

    internal static string PathsForClipboard(IEnumerable<string> paths) => string.Join("\r\n", paths.Select(path => "\"" + path + "\""));

    internal static ProcessStartInfo TerminalStartInfo(string folder, bool windowsTerminal)
    {
        var info = new ProcessStartInfo(windowsTerminal ? "wt.exe" : "powershell.exe")
        {
            UseShellExecute = true, WorkingDirectory = folder
        };
        if (windowsTerminal) { info.ArgumentList.Add("-d"); info.ArgumentList.Add(folder); }
        else info.ArgumentList.Add("-NoExit");
        return info;
    }

    public void OpenTerminal()
    {
        if (!Directory.Exists(CurrentPath)) return;
        try { Process.Start(TerminalStartInfo(CurrentPath, windowsTerminal: true)); }
        catch (Win32Exception)
        {
            try { Process.Start(TerminalStartInfo(CurrentPath, windowsTerminal: false)); }
            catch (Win32Exception ex) { SetLocalizedStatus("Could not open a terminal: {0}", ex.Message); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseCandidates();
            if (search is not null) search.IndexChanged -= SearchIndexChanged;
            searchTimer.Dispose();
            selectionTimer.Dispose();
            folderSizeTimer.Dispose();
            candidates.Dispose();
            tips.Dispose();
            tabMenu.Dispose();
            folderIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
