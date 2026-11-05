using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using QuadExplorer.Search;

namespace QuadExplorer;

public sealed partial class ExplorerPane : UserControl
{
    private sealed class PaneCloseButton : ToolStripButton
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.PaintClose(e.Graphics, new Rectangle(Point.Empty, Size),
                !Enabled ? Theme.Border : Selected ? Theme.Text : Theme.Muted, Owner?.DeviceDpi ?? 96);
        }
    }

    private sealed class PaneStatusLabel : Label
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string FolderName { get; set; } = "";

        protected override void OnPaint(PaintEventArgs e)
        {
            if (FolderName.Length == 0) { base.OnPaint(e); return; }
            var bounds = new Rectangle(Padding.Left, Padding.Top,
                ClientSize.Width - Padding.Horizontal, ClientSize.Height - Padding.Vertical);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;
            int width = Math.Min(bounds.Width / 2,
                TextRenderer.MeasureText(e.Graphics, FolderName, Font, new Size(int.MaxValue, bounds.Height), flags).Width);
            var folderBounds = new Rectangle(bounds.Location, new Size(width, bounds.Height));
            var folderColor = Color.FromArgb((Theme.Text.R + Theme.Muted.R) / 2,
                (Theme.Text.G + Theme.Muted.G) / 2, (Theme.Text.B + Theme.Muted.B) / 2);
            TextRenderer.DrawText(e.Graphics, FolderName, Font, folderBounds, folderColor, flags | TextFormatFlags.EndEllipsis);
            bounds.X += width;
            bounds.Width -= width;
            TextRenderer.DrawText(e.Graphics, Text[FolderName.Length..], Font, bounds, ForeColor, flags | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class FolderTab(FolderTabButton button)
    {
        public ShellViewHost? Host { get; set; }
        public FolderTabButton Button { get; } = button;
        public TabGroup? Group { get; set; }
        public bool Pinned { get => Button.Pinned; set => Button.Pinned = value; }
        internal string? AccessibleTitle { get; set; }
        internal string? AccessibleGroupLabel { get; set; }
        internal string? AccessibleLanguage { get; set; }
    }
    private readonly List<FolderTab> tabs = [];
    private readonly UiText text;
    private readonly List<Action> languageBindings = [];
    private Func<string>? localizedStatus;
    private string statusMessage = "";
    private readonly Stack<ClosedTabEntry> closedTabs = [];
    private readonly ToolStrip tabStrip = Theme.Strip();
    private readonly Panel contents = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly Label emptyHint = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "Enter a folder path above or choose a folder from Pane options.",
        ForeColor = Theme.Muted, BackColor = Color.White, Padding = new Padding(20)
    };
    private readonly TextBox address;
    private readonly PaneStatusLabel status;
    private readonly Panel activeLine;
    private readonly ToolTip tips = new() { ShowAlways = true, AutoPopDelay = 10000 };
    private readonly ContextMenuStrip tabMenu = new();
    private readonly Image folderIcon = Theme.FolderIcon();
    private int selectedIndex = -1;
    private readonly ToolStripButton newTab;
    private readonly ToolStripDropDownButton tabList;
    private readonly ToolStripButton closePane;
    private int firstVisibleTab;
    private int tabWheel;
    private bool layingOutTabs;
    private readonly ToolStrip breadcrumb = Theme.Strip();
    private string? selectionSummary;
    private int selectionGeneration;
    private (string Path, string Size)? folderSize;
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
    // Which of AddressCandidates.ResultLimits the popup shows; every new query starts at the first.
    private int resultStep;
    private int pendingSelect;
    internal event EventHandler? CandidatesUpdated;
    internal event EventHandler? SearchSettled;
    internal IReadOnlyList<SearchHit> SearchCandidates => candidates.Hits;
    internal bool CandidatesVisible => candidates.Visible;

    public int PaneNumber { get; }
    public int SelectedIndex => selectedIndex;
    public int TabCount => tabs.Count;
    internal FolderTab? ActiveTab => selectedIndex >= 0 ? tabs[selectedIndex] : null;
    public ShellViewHost? ActiveView => selectedIndex >= 0 ? tabs[selectedIndex].Host : null;
    public string CurrentPath => ActiveView?.LocationPath ?? "";
    internal IEnumerable<string> RecentPaths => ClosedPaths().Take(4);
    internal bool CanReopenTab => closedTabs.TryPeek(out var entry) && tabs.Count + entry.Count <= 32;
    internal TabGroup? LastReopenedGroup { get; private set; }
    internal event EventHandler? TabClosed;
    internal event EventHandler? TabReopened;
    internal event EventHandler? ClosePaneRequested;
    public event EventHandler? Activated;
    public event EventHandler? StateChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<string>? FavoriteRequested;
    internal event Action<ExplorerPane, FolderTab>? TabDragRequested;
    internal event Action<ExplorerPane, FolderTab, SplitEdge>? SplitTabRequested;
    internal event Action<ExplorerPane, TabGroup>? GroupDragRequested;
    internal event Action<ExplorerPane, TabGroup, int>? MoveGroupRequested;
    internal event Action<ExplorerPane, TabGroup>? MoveGroupToWindowRequested;
    public event EventHandler<string>? FolderVisited;
    internal event Action<IReadOnlyList<string>>? OpenFoldersInNewWindowRequested;
    internal event Action<ExplorerPane>? OpenInOtherPaneRequested;
    internal Rectangle FileAreaOnScreen => contents.RectangleToScreen(contents.ClientRectangle);

    public ExplorerPane(int number, PaneState state, UiText? text = null,
        IPathSearchBackend? search = null, Func<IReadOnlyList<string>>? searchRoots = null)
    {
        this.text = text ?? new UiText();
        this.search = search;
        this.searchRoots = searchRoots;
        candidates = new AddressCandidates(number);
        candidates.Chosen += OpenCandidate;
        candidates.OpenInTabRequested += (hit, foreground) => { CloseCandidates(); OpenPathsInNewTabs([hit.FullPath], foreground); };
        candidates.MoreRequested += ShowMoreCandidates;
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
                if (candidates.Visible && candidates.MoreSelected) ShowMoreCandidates();
                else if (candidates.Visible && candidates.SelectedHit is { } hit) OpenCandidate(hit);
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
        address.LostFocus += (_, _) => { navigation.Invalidate(); ShowBreadcrumb(); };
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
        // ToolStrip has its own default font rather than inheriting the address font.
        // Keep both on the same metrics when DPI or the user's text size changes.
        breadcrumb.Font = address.Font;
        address.FontChanged += (_, _) => UpdateBreadcrumbMetrics();
        breadcrumb.DpiChangedAfterParent += (_, _) => UpdateBreadcrumbMetrics();
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
        closePane = new PaneCloseButton
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoSize = false, Width = 30, Margin = new Padding(6, 1, 0, 2),
            AccessibleName = $"Pane {number} close pane", Overflow = ToolStripItemOverflow.Never
        };
        languageBindings.Add(() => closePane.ToolTipText = this.text.T("Close pane (Ctrl+W when empty)"));
        closePane.Click += (_, _) => ClosePaneRequested?.Invoke(this, EventArgs.Empty);
        tabStrip.Items.Add(closePane);
        // Tabs are sized and windowed here, so nothing is ever pushed into the hidden overflow menu.
        tabStrip.CanOverflow = false;
        tabStrip.Padding = new Padding(6, 4, 6, 0); // the viewed tab meets the address row below it
        tabStrip.SizeChanged += (_, _) => LayoutTabs();
        tabStrip.FontChanged += (_, _) => LayoutTabs();
        tabStrip.DpiChangedAfterParent += (_, _) => LayoutTabs();
        tabStrip.AccessibleName = $"Pane {number} tabs";
        status = new PaneStatusLabel
        {
            Dock = DockStyle.Bottom, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Muted, BackColor = Color.White, Padding = new Padding(9, 0, 4, 0),
            AutoEllipsis = true, AccessibleName = $"Pane {number} status"
        };
        status.Height = status.PreferredHeight;
        status.FontChanged += (_, _) => status.Height = status.PreferredHeight;
        status.DpiChangedAfterParent += (_, _) => status.Height = status.PreferredHeight;
        activeLine = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Theme.Border };
        body.Controls.Add(contents);
        body.Controls.Add(status);
        body.Controls.Add(BuildCommands());
        viewMenu.DropDownOpening += (_, _) =>
        {
            AddActivePaneColorMenu(viewMenu);
            Theme.Apply(commands);
        };
        body.Controls.Add(navigation);
        body.Controls.Add(tabStrip);
        Controls.Add(body);
        body.Resize += (_, _) => { body.Region?.Dispose(); body.Region = Theme.CardRegion(body.Size, DeviceDpi); };
        // The status line is a caption: a step smaller than the body text.
        FontChanged += (_, _) => status.Font = new Font(Font.FontFamily, Math.Max(7f, Font.SizeInPoints - 1f));
        contents.Controls.Add(emptyHint);
        InitializeExternalTabDrops();
        emptyHint.Click += (_, _) => FocusAddress();
        Enter += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        MouseDown += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        ReplaceTabs(state);
        if (tabs.Count == 0) ShowEmptyState();
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
        int limit = AddressCandidates.ResultLimits[resultStep];
        int select = pendingSelect;
        pendingSelect = 0;
        string input = address.Text;
        var source = new CancellationTokenSource();
        searchCancellation = source;
        try
        {
            if (!search.IsReady) { candidates.Present(address, [], text.T("Building search index...")); return; }
            var roots = searchRoots?.Invoke() ?? new[] { CurrentPath };
            // Asking for one more than is shown tells whether the list continues.
            var hits = await Task.Run(() => search.QueryAsync(input, roots, limit + 1, source.Token), source.Token);
            if (source.IsCancellationRequested || IsDisposed || generation != searchGeneration ||
                address.Text != input || !address.Focused) return;
            candidates.Present(address, hits.Take(limit).ToArray(), null, FooterFor(hits.Count > limit, limit), select);
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

    private (CandidateFooter Kind, string Text)? FooterFor(bool more, int limit)
    {
        if (!more) return null;
        return resultStep + 1 < AddressCandidates.ResultLimits.Length
            ? (CandidateFooter.More, text.T("Show more results"))
            : (CandidateFooter.Limit, text.T("Showing the first {0} matches. Type more of the name to narrow the search.", limit));
    }

    // The "show more" row asks again for the next, larger page and selects the first new result.
    private async void ShowMoreCandidates()
    {
        if (resultStep + 1 >= AddressCandidates.ResultLimits.Length || !candidates.Visible || !address.Focused) return;
        resultStep++;
        pendingSelect = candidates.Hits.Count;
        searchCancellation?.Cancel();
        await QueryCandidates();
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
        // Every closed popup starts over at the first page; "show more" never closes it.
        resultStep = 0;
        pendingSelect = 0;
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
        LayoutTabs();
    }

    public void AddTab(string path, bool select = true, bool countVisit = true) => InsertTab(path, select, countVisit);

    // A tab with no folder yet: the address bar takes focus so the user can type a path or search.
    public void AddBlankTab() => InsertTab(null, select: true);

    private FolderTab? InsertTab(string? path, bool select, bool countVisit = true, TabGroup? group = null,
        int? insertionIndex = null)
    {
        if (tabs.Count >= 32) { SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first."); return null; }
        if (group is not null && GroupMembers(group).Count >= TabGroup.MaxTabs)
        {
            SetLocalizedStatus("A group holds up to 12 tabs.");
            return null;
        }
        string title = text.T("New tab");
        var button = new FolderTabButton
        {
            Title = title, Text = title, Image = folderIcon, AutoSize = false,
            Width = 164, TextAlign = ContentAlignment.MiddleLeft, Overflow = ToolStripItemOverflow.Never,
            ImageAlign = ContentAlignment.MiddleLeft, AccessibleName = $"Folder tab: {title}",
            Margin = new Padding(1, 0, 1, 0)
        };
        var tab = new FolderTab(button) { Group = group };
        button.CloseRequested += (_, _) => CloseTab(tabs.IndexOf(tab));
        button.UnpinRequested += (_, _) => SetTabsPinned([tab], false);
        button.DragRequested += (_, _) => TabDragRequested?.Invoke(this, tab);
        button.Click += (_, _) => SelectTabWithModifiers(tab, ModifierKeys);
        button.MouseDown += (_, e) =>
        {
            int index = tabs.IndexOf(tab);
            if (e.Button == MouseButtons.Middle) CloseTab(index);
            else if (e.Button == MouseButtons.Right) ShowTabMenu(index);
        };
        var active = ActiveTab;
        int lastMember = group is null ? -1 : tabs.FindLastIndex(member => member.Group == group);
        int insertion = Math.Max(PinnedCount, insertionIndex ?? (lastMember < 0 ? tabs.Count : lastMember + 1));
        tabs.Insert(insertion, tab);
        selectedIndex = active is null ? -1 : tabs.IndexOf(active);
        if (group is null && groupChips.Count == 0)
            tabStrip.Items.Insert(insertion + 1 < tabs.Count
                ? tabStrip.Items.IndexOf(tabs[insertion + 1].Button) : tabStrip.Items.IndexOf(newTab), button);
        else SynchronizeGroupStrip();
        if (path is not null) AttachHost(tab, path, countVisit);
        if (select) SelectTab(insertion);
        else LayoutTabs();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    private void AttachHost(FolderTab tab, string path, bool countVisit = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? selectFile = File.Exists(path) ? Path.GetFullPath(path) : null;
        var host = new ShellViewHost(selectFile is null ? path : Path.GetDirectoryName(selectFile)!, text) { Visible = false };
        host.SetDetailsAppearance(showVerticalSeparators, comfortableRows, highlightSortedColumn);
        host.OpenFoldersInTabsRequested += (paths, foreground) =>
        {
            if (host == ActiveView) OpenPathsInNewTabs(paths, foreground);
        };
        host.OpenFoldersInNewWindowRequested += paths =>
        {
            if (host == ActiveView) OpenFoldersInNewWindowRequested?.Invoke(paths);
        };
        if (selectFile is not null) host.NavigateAndSelect(selectFile);
        tab.Host = host;
        var button = tab.Button;
        button.Title = host.DisplayName;
        UpdateTabAccessibleName(tab);
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
            LayoutTabs();
            if (host == ActiveView) { selectionSummary = null; address.Text = host.LocationPath; UpdateStatus(); ShowBreadcrumb(); }
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        host.NavigationError += (_, message) => SetLocalizedStatus(() => host.ErrorStatus ?? message);
        host.FilterChanged += (_, _) => { if (host == ActiveView) UpdateCommands(); };
        host.Navigated += (_, _) => { if (host == ActiveView) UpdateCommands(); };
        // A folder the user opens counts as a visit. The folder a restored tab starts on does not, even if it never loads.
        string? lastVisit = countVisit ? null : FolderVisits.NormalizePath(host.LocationPath);
        host.Navigated += (_, _) =>
        {
            string location = host.LocationPath;
            string? key = FolderVisits.NormalizePath(location);
            bool changed = !string.Equals(key, lastVisit, StringComparison.OrdinalIgnoreCase);
            lastVisit = key;
            if (changed && key is not null) FolderVisited?.Invoke(this, location);
        };
        // Tab removal hides the host while the selected index is still being updated.
        host.ItemCountChanged += (_, _) =>
        {
            // Releasing a shell view publishes a final count while its pane is being torn down.
            if (Disposing || IsDisposed || host.Disposing || host.IsDisposed) return;
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

    public void SelectTab(int index, bool focus = true, bool preserveSelection = false)
    {
        CloseCandidates();
        if (index < 0 || index >= tabs.Count) return;
        if (ActiveTab != tabs[index]) { tabOpener = null; openerRunTabs.Clear(); }
        if (tabs[index].Group is { Collapsed: true } group) group.Collapsed = false;
        if (selectedIndex >= 0 && selectedIndex < tabs.Count && tabs[selectedIndex].Host is { } previous) previous.Visible = false;
        selectedIndex = index;
        if (!preserveSelection)
        {
            selectedTabs.Clear();
            selectionAnchor = tabs[index];
        }
        selectedTabs.Add(tabs[index]);
        RememberGroupSelection();
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].Button.Checked = i == index;
            tabs[i].Button.ForeColor = i == index ? Theme.Text : Theme.Muted;
        }
        UpdateTabSelectionAppearance();
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

    public void CloseTab(int index = -1, bool remember = true) => CloseTabCore(index, remember, selectReplacement: true);

    private void CloseTabCore(int index, bool remember, bool selectReplacement)
    {
        if (index < 0) index = selectedIndex;
        if (index < 0 || index >= tabs.Count) return;
        var tab = tabs[index];
        ClearTabSelection();
        var active = ActiveTab;
        int nextIndex = selectedIndex > index ? selectedIndex - 1 : Math.Min(selectedIndex, tabs.Count - 2);
        tabs.RemoveAt(index);
        openerRunTabs.Remove(tab);
        selectedIndex = active == tab || active is null ? -1 : tabs.IndexOf(active);
        if (tab.Group is not null || groupChips.Count > 0) SynchronizeGroupStrip();
        tabStrip.Items.Remove(tab.Button);
        if (remember) RememberClosedTab(new ClosedTabEntry(tab.Host?.LocationPath, OriginalGroup: tab.Group, Pinned: tab.Pinned));
        if (tab.Host is { } host)
        {
            contents.Controls.Remove(host);
            host.Dispose();
        }
        tab.Button.Dispose();
        if (tabs.Count == 0) { selectedIndex = -1; ShowEmptyState(); LayoutTabs(); }
        else if (selectedIndex >= 0)
        {
            UpdateStatus();
            LayoutTabs();
        }
        else if (!selectReplacement) LayoutTabs();
        else
        {
            int next = Math.Max(0, nextIndex);
            if (tabs[next].Group is { Collapsed: true })
            {
                int visible = tabs.FindIndex(next, member => member.Group?.Collapsed != true);
                if (visible < 0) visible = tabs.FindLastIndex(member => member.Group?.Collapsed != true);
                if (visible >= 0) next = visible;
            }
            SelectTab(next);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (remember) TabClosed?.Invoke(this, EventArgs.Empty);
    }

    public void ReopenTab()
    {
        if (!CanReopenTab)
        {
            if (closedTabs.Count > 0) SetLocalizedStatus("Maximum 32 tabs per pane. Close a tab first.");
            return;
        }
        var entry = closedTabs.Pop();
        LastReopenedTabs = [];
        if (entry.Selection is { } selection)
        {
            ReopenSelection(selection);
            LastReopenedGroup = null;
            TabReopened?.Invoke(this, EventArgs.Empty);
            return;
        }
        LastReopenedGroup = entry.Group is { } group
            ? AddGroup(group.Paths, group.Name, group.Color, selectedMember: group.SelectedMember) : null;
        if (LastReopenedGroup is { } reopened) reopened.SavedId = ExistingSavedId(entry.Group!.SavedId, reopened);
        if (entry.Group is null)
        {
            var original = entry.OriginalGroup;
            if (original is not null && (!groupChips.ContainsKey(original)
                || tabs.Count(tab => tab.Group == original) >= TabGroup.MaxTabs)) original = null;
            if (InsertTab(entry.Path, select: true, group: original) is { } restored && entry.Pinned)
                SetTabsPinned([restored], true);
        }
        LastReopenedTabs = LastReopenedGroup is { } restoredGroup ? GroupMembers(restoredGroup)
            : ActiveTab is { } restoredTab ? [restoredTab] : [];
        TabReopened?.Invoke(this, EventArgs.Empty);
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

    internal enum ShowResult { Opened, AlreadyOpen, Full }

    // Opens a folder here for another pane: a tab already showing it is selected, a blank tab is filled,
    // otherwise a new tab is added. The pane takes focus, as with any tab the user opens.
    internal ShowResult ShowFolder(string path)
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].Host is not { } host
                || !string.Equals(host.LocationPath.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            SelectTab(i);
            return ShowResult.AlreadyOpen;
        }
        if (selectedIndex >= 0 && selectedIndex < tabs.Count && tabs[selectedIndex].Host is null)
        {
            Navigate(path);
            return ShowResult.Opened;
        }
        if (tabs.Count >= 32) return ShowResult.Full;
        AddTab(path);
        return ShowResult.Opened;
    }

    public void FocusAddress() { breadcrumb.Visible = false; address.Focus(); address.SelectAll(); }
    public void FocusFiles()
    {
        if (ActiveView is { } view) view.FocusView();
        else FocusAddress();
    }
    private bool isActive;
    private string activePaneColor = "default";
    private const int ActiveBorderWidth = 2;
    // Match Theme.PaintCard's rounded edge; only the outline changes, never the content bounds.
    private const int CardCornerRadius = 8;
    internal event Action<string>? ActivePaneColorRequested;
    internal string ActivePaneColor => activePaneColor;
    // Reuse the existing light/green/beige palette accents, with their softer dark-theme counterparts.
    internal Color ActiveBorderColor => activePaneColor switch
    {
        "blue" => Theme.IsDark ? Color.FromArgb(112, 183, 255) : Color.FromArgb(0, 95, 184),
        "green" => Theme.IsDark ? Color.FromArgb(167, 192, 128) : Color.FromArgb(40, 118, 74),
        "amber" => Theme.IsDark ? Color.FromArgb(245, 192, 66) : Color.FromArgb(150, 92, 36),
        _ => Theme.Accent
    };

    internal void SetActivePaneColor(string color)
    {
        activePaneColor = SessionStore.NormalizeActivePaneColor(color);
        activeLine.BackColor = isActive ? ActiveBorderColor : Theme.Border;
        Invalidate();
    }

    private void AddActivePaneColorMenu(ToolStripDropDownItem menu)
    {
        var colors = new ToolStripMenuItem(text.T("Active pane color"))
        {
            AccessibleName = "Active pane color", Font = Font
        };
        foreach (var (color, label) in new[]
        {
            ("default", "Theme default"), ("blue", "Blue"), ("green", "Green"), ("amber", "Amber")
        })
        {
            var item = new ToolStripMenuItem(text.T(label))
            {
                AccessibleName = $"Active pane color {color}", Checked = activePaneColor == color, Font = Font
            };
            item.Click += (_, _) => ActivePaneColorRequested?.Invoke(color);
            colors.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(colors);
    }

    private void UpdateCardPadding()
    {
        int Scaled(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        Padding = new Padding(Scaled(4), Scaled(3), Scaled(4), Scaled(6));
    }

    public void SetActive(bool active)
    {
        activeLine.BackColor = active ? ActiveBorderColor : Theme.Border;
        if (isActive == active) return;
        isActive = active;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var card = Rectangle.FromLTRB(Padding.Left - 1, Padding.Top - 1, Width - Padding.Right, Height - Padding.Bottom);
        if (card.Width <= 2 || card.Height <= 2) return;
        Theme.PaintCard(e.Graphics, card, false, DeviceDpi, Padding.Top + tabStrip.Bottom);
        if (!isActive) return;
        float thickness = (float)Math.Ceiling(ActiveBorderWidth * DeviceDpi / 96d);
        // Child controls clip the inside half of the border. Grow into the existing outer
        // padding instead, without moving the tabs, file area or any other content.
        int outset = (int)Math.Ceiling((thickness - 1) / 2);
        card.Inflate(outset, outset);
        int diameter = Math.Min(2 * (int)Math.Round(CardCornerRadius * DeviceDpi / 96d), Math.Min(card.Width, card.Height));
        using var path = new GraphicsPath();
        path.AddArc(card.Left, card.Top, diameter, diameter, 180, 90);
        path.AddArc(card.Right - diameter, card.Top, diameter, diameter, 270, 90);
        path.AddArc(card.Right - diameter, card.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(card.Left, card.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(ActiveBorderColor, thickness);
        e.Graphics.DrawPath(pen, path);
        e.Graphics.SmoothingMode = smoothing;
    }
    public void SetStatus(string message) { localizedStatus = null; RenderStatus(message); }
    private void RenderStatus(string message)
    {
        statusMessage = message;
        RenderItemCount();
    }
    private void RenderItemCount()
    {
        status.FolderName = ActiveView?.DisplayName ?? "";
        string prefix = status.FolderName.Length > 0 ? status.FolderName + "  |  " : "";
        status.Text = ActiveView is null ? statusMessage : prefix + statusMessage + "  |  " + (ActiveView.ItemCount is int value
            ? text.T("{0} items", value) : text.T("Counting items..."))
            + (folderSize is { } size && size.Path == ActiveView.LocationPath ? "  |  " + text.T("File total: {0}", size.Size) : "")
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
        ? text.T("Pane {0}  |  1 tab", PaneNumber)
        : text.T("Pane {0}  |  {1} tabs", PaneNumber, tabs.Count));
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
        return new()
        {
            Tabs = saved.Select(tab => tab.Host!.LocationPath).ToList(), SelectedTab = Math.Max(0, selected),
            Groups = CaptureGroups(saved), PinnedCount = saved.TakeWhile(tab => tab.Pinned).Count()
        };
    }
    internal int IndexOfTab(FolderTab tab) => tabs.IndexOf(tab);
    internal ToolStrip TabStrip => tabStrip;
    internal ToolStripDropDownButton TabList => tabList;
    internal void SetCanClose(bool enabled) => closePane.Enabled = enabled;
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
            if (groupChips.Count > 0 || PinnedCount > 0)
            {
                LayoutGroupedTabs();
                return;
            }
            int count = tabs.Count;
            float scale = tabStrip.DeviceDpi / 96f;
            int Scaled(int value) => (int)Math.Round(value * scale);
            int chrome = Scaled(70), floor = Scaled(90), margin = 2;
            // Fixed-size items keep the height they were created with; size them from the current font,
            // so a font or DPI change (for example connecting over Remote Desktop) never clips their text.
            int rowHeight = Math.Max(TextRenderer.MeasureText("가Ag", tabStrip.Font).Height, Scaled(16)) + Scaled(8);
            newTab.Size = new Size(Scaled(30), rowHeight);
            closePane.Size = new Size(Scaled(30), rowHeight);
            int[] natural = tabs.Select(tab => Math.Clamp(TextWidth(tab.Button.Title) + chrome,
                Scaled(110), Scaled(count == 1 ? 320 : 240))).ToArray();
            int available = Math.Max(0, tabStrip.ClientSize.Width - tabStrip.Padding.Horizontal
                - newTab.Width - newTab.Margin.Horizontal - closePane.Width - closePane.Margin.Horizontal - Scaled(6));
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
                button.ToolTipText = (tabs[i].Host?.LocationPath ?? text.T("New tab")) + Environment.NewLine
                    + text.T(i == selectedIndex ? "Close current tab (Ctrl+W)" : "Select this tab, then close with Ctrl+W");
                UpdateTabAccessibleName(tabs[i]);
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

    private string FitTitle(string title, int width, bool pinned = false)
    {
        int Measure(string value) => pinned ? PinnedTextWidth(value) : TextWidth(value);
        if (Measure(title) <= width) return title;
        int low = 0, high = title.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Measure(title[..middle] + "\u2026") <= width) low = middle;
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

    private readonly Dictionary<string, int> pinnedTextWidths = [];
    private Font? measuredPinnedFont;

    private int PinnedTextWidth(string value)
    {
        if (!ReferenceEquals(measuredPinnedFont, tabStrip.Font) || pinnedTextWidths.Count > 4096)
        {
            pinnedTextWidths.Clear();
            measuredPinnedFont = tabStrip.Font;
        }
        if (!pinnedTextWidths.TryGetValue(value, out int width))
            pinnedTextWidths[value] = width = TextRenderer.MeasureText(value, tabStrip.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
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
        if (steps != 0) SelectAdjacentTab(steps, wrap: false);
        return true;
    }

    private void ShowTabMenu(int index, bool keyboard = false)
    {
        if (index < 0) return;
        if (!selectedTabs.Contains(tabs[index])) SelectTab(index, focus: false);
        var selection = SelectedTabs;
        var movingTab = tabs[index];
        tabMenu.Items.Clear();
        tabMenu.Items.Add(text.T("New tab"), null, (_, _) => AddBlankTab());
        tabMenu.Items.Add(text.T("Duplicate tab"), null, (_, _) => DuplicateTab(movingTab)).Enabled = movingTab.Host is not null;
        tabMenu.Items.Add(text.T("Reopen closed tab"), null, (_, _) => ReopenTab()).Enabled = CanReopenTab;
        bool pin = selection.Any(tab => !tab.Pinned);
        tabMenu.Items.Add(text.T(pin ? "Pin tab" : "Unpin tab"), null, (_, _) => SetTabsPinned(selection, pin));
        AddGroupTabMenu(tabMenu, tabs[index]);
        var move = new ToolStripMenuItem(text.T(selection.Count > 1 ? "Move tabs to pane" : "Move tab to pane"));
        for (int i = 1; i <= 4; i++)
        {
            int target = i;
            if (target == PaneNumber) continue;
            move.DropDownItems.Add(text.T("Pane {0}", target), null,
                (_, _) => MoveTabsRequested?.Invoke(this, selection, target));
        }
        move.Enabled = ActiveTab is not null;
        tabMenu.Items.Add(move);
        tabMenu.Items.Add(text.T(selection.Count > 1 ? "Move tabs to new window" : "Move tab to new window"), null,
            (_, _) => MoveTabsToWindowRequested?.Invoke(this, selection));
        var split = new ToolStripMenuItem(text.T("Split tab"));
        foreach (SplitEdge edge in Enum.GetValues<SplitEdge>())
        {
            var side = edge;
            split.DropDownItems.Add(text.T(side.ToString()), null, (_, _) =>
            {
                if (ActiveTab is { } tab) SplitTabRequested?.Invoke(this, tab, side);
            });
        }
        tabMenu.Items.Add(split);
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(selection.Count > 1 ? text.T("Close {0} tabs", selection.Count) : text.T("Close tab    Ctrl+W"), null,
            (_, _) => { if (selection.Count > 1) CloseTabs(selection); else CloseTab(); });
        tabMenu.Items.Add(text.T("Close other tabs"), null, (_, _) =>
        {
            var keep = tabs[selectedIndex];
            bool wasSuppressed = SuppressSavedGroupSync;
            SuppressSavedGroupSync = true;
            try
            {
                for (int i = tabs.Count - 1; i >= 0; i--) if (tabs[i] != keep && !tabs[i].Pinned) CloseTab(i);
            }
            finally { SuppressSavedGroupSync = wasSuppressed; }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }).Enabled = tabs.Count > 1;
        tabMenu.Padding = new Padding(6, 6, 6, 8);
        foreach (var item in tabMenu.Items.OfType<ToolStripMenuItem>())
            item.Padding = new Padding(0, (int)Math.Round(5 * DeviceDpi / 96d), 4, (int)Math.Round(5 * DeviceDpi / 96d));
        Theme.Apply(tabMenu);
        if (keyboard) tabMenu.Show(tabStrip, new Point(tabs[index].Button.Bounds.Left, tabs[index].Button.Bounds.Bottom));
        else tabMenu.Show(Cursor.Position);
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
        tabMenu.Items.Add(text.T("Open this folder in the other pane    Ctrl+Alt+O"), null, (_, _) => OpenInOtherPaneRequested?.Invoke(this)).Enabled = ActiveView is not null;
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(text.T("New folder"), null, (_, _) => ActiveView?.InvokeBackgroundVerb("NewFolder")).Enabled = ActiveView is not null;
        tabMenu.Items.Add(text.T("Filter this folder"), null, (_, _) => FocusFolderFilter()).Enabled = ActiveView is not null;
        tabMenu.Items.Add(text.T("Compare with the other pane"), null, (_, _) => CompareRequested?.Invoke(this, EventArgs.Empty));
        var sort = new ToolStripMenuItem(text.T("Sort")) { Enabled = ActiveView is not null };
        BuildSortMenu(sort);
        tabMenu.Items.Add(sort);
        var view = new ToolStripMenuItem(text.T("View"));
        BuildViewMenu(view);
        AddActivePaneColorMenu(view);
        tabMenu.Items.Add(view);
        tabMenu.Items.Add(text.T("Undo tab close in this pane"), null, (_, _) => UndoCloseRequested?.Invoke(this, EventArgs.Empty)).Enabled = undoClose.Enabled;
        tabMenu.Items.Add(text.T("Redo tab close in this pane"), null, (_, _) => RedoCloseRequested?.Invoke(this, EventArgs.Empty)).Enabled = redoClose.Enabled;
        Theme.Apply(tabMenu);
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
        bool wasSuppressed = SuppressSavedGroupSync;
        SuppressSavedGroupSync = true;
        try
        {
            while (tabs.Count > 0) CloseTabCore(tabs.Count - 1, remember: false, selectReplacement: false);
            foreach (string path in state.Tabs) AddTab(path, select: false, countVisit: false);
            RestoreGroups(state.Groups);
            for (int index = 0; index < Math.Clamp(state.PinnedCount, 0, tabs.Count); index++)
            {
                tabs[index].Group = null;
                tabs[index].Pinned = true;
                tabs[index].AccessibleTitle = null;
            }
            SynchronizeGroupStrip();
            if (tabs.Count > 0)
            {
                int selected = Math.Clamp(state.SelectedTab, 0, tabs.Count - 1);
                if (tabs[selected].Group is { Collapsed: true } group)
                {
                    if (!SelectOutsideGroup(group, focus: false))
                    {
                        // A saved full pane may have every group collapsed; do not expand one on restore.
                        ShowEmptyState();
                        LayoutTabs();
                    }
                }
                else SelectTab(selected, focus: false);
            }
        }
        finally { SuppressSavedGroupSync = wasSuppressed; }
        StateChanged?.Invoke(this, EventArgs.Empty);
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
                UpdateBreadcrumbMetrics();
                return;
            }
            foreach (var (label, path) in breadcrumbSegments)
            {
                string target = path;
                var segment = new ToolStripButton(label) { Margin = new Padding(0), Padding = new Padding(2, 0, 2, 0), ToolTipText = target };
                BindFolderNavigationTarget(segment, target);
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
        UpdateBreadcrumbMetrics();
    }

    private void UpdateBreadcrumbMetrics()
    {
        breadcrumb.Font = address.Font;
        // A borderless TextBox fits a text line, but ToolStrip items also reserve
        // vertical content insets. Let the host fit the strip's preferred height.
        if (breadcrumb.Items.Count > 0)
            address.MinimumSize = new Size(0, breadcrumb.Padding.Vertical + breadcrumb.Items.Cast<ToolStripItem>()
                .Max(item => item.GetPreferredSize(Size.Empty).Height + item.Margin.Vertical));
        breadcrumb.Height = Math.Max(address.ClientSize.Height, address.MinimumSize.Height);
        // TextBox.PreferredSize can remain shorter than MinimumSize. Reserve the
        // real host height in the autosizing row so the painted field is not clipped.
        if (address.Parent is TableLayoutPanel navigation)
            navigation.MinimumSize = new Size(0,
                address.MinimumSize.Height + address.Margin.Vertical + navigation.Padding.Vertical);
        for (int i = 0; i < breadcrumbGroups.Count; i++)
        {
            var items = breadcrumbGroups[i].Items;
            breadcrumbGroups[i] = (items, items.Sum(item => item.GetPreferredSize(Size.Empty).Width + item.Margin.Horizontal));
        }
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
                BindFolderNavigationTarget(hidden.DropDownItems.Add(breadcrumbSegments[i].Label), target);
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
                BindFolderNavigationTarget(chevron.DropDownItems.Add(Path.GetFileName(path)), path);
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
        // The recent list shows shortcut targets, so the shortcuts' own sizes would be meaningless.
        string? result = path is null || ShellViewHost.IsRecent(path) ? null : await Task.Run(() => FolderFilesSize(path, 50_000));
        if (generation != folderSizeGeneration || IsDisposed || path != ActiveView?.LocationPath) return;
        folderSize = result is null ? null : (path!, result);
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
            dragHoverTimer.Dispose();
            candidates.Dispose();
            tips.Dispose();
            tabMenu.Dispose();
            folderIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
