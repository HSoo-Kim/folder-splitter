using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using QuadExplorer.Search;

namespace QuadExplorer;

public sealed class MainForm : Form, IMessageFilter
{
    private readonly ToolStripButton undoClose = new CloseHistoryButton(false) { AutoSize = false },
        redoClose = new CloseHistoryButton(true) { AutoSize = false };
    private string sessionPath => application.SessionPath;
    private readonly FolderSplitterApplicationContext application;
    internal FolderSplitterApplicationContext ApplicationContext => application;
    private readonly UiText text;
    private readonly IPathSearchBackend? search;
    private readonly ShellIconCache placeIcons = new();
    private readonly FolderTree folderTree;
    private readonly ToolStripButton treeButton = new() { AccessibleName = "Folder tree", CheckOnClick = true };
    private readonly ToolStripButton previewButton = new() { AccessibleName = "Preview", CheckOnClick = true };
    private readonly PreviewPane previewPane;
    private readonly Splitter previewSplitter;
    private PreviewWindow? previewWindow;
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 200 };
    private bool showPreview;
    private bool showCommandBar;
    private bool showVerticalSeparators;
    private bool comfortableRows;
    private bool highlightSortedColumn;
    private int previewWidth;
    private readonly Button treeHeader;
    private readonly Panel treePanel = new() { Dock = DockStyle.Bottom };
    private readonly Splitter treeSplitter = new()
    {
        Dock = DockStyle.Bottom, Height = 6, Cursor = Cursors.HSplit, AccessibleName = "Resize favorites and folder tree"
    };
    private int folderTreeHeight;
    private readonly ToolStripMenuItem hotkeyItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem winEItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem startupItem = new() { CheckOnClick = true };
    private bool startWithWindows { get => application.State.StartWithWindows; set => application.ChangeState(s => s with { StartWithWindows = value }); }
    private WinEShortcut winE => application.WinE;
    private bool globalHotkey { get => application.State.GlobalHotkey; set => application.ChangeState(s => s with { GlobalHotkey = value }); }
    private readonly ToolStripMenuItem trayItem = new() { CheckOnClick = true };
    private bool closeToTray => application.State.KeepInTray;
    private readonly ToolStripDropDownButton themeButton = new() { AccessibleName = "Color theme" };
    private string colorTheme => application.State.ColorTheme;
    private string? customAccent => application.State.CustomAccent;
    private string? customBackground => application.State.CustomBackground;
    private List<CustomColorPair> recentCustomColors => application.State.RecentCustomColors;
    private List<string> pickerColors => application.State.PickerColors;
    private string activePaneColor => application.State.ActivePaneColor;
    private bool showFolderTree;
    private readonly List<Action> languageBindings = [];
    private readonly ToolStripComboBox languageSelector = new()
    {
        AccessibleName = "UI language", DropDownStyle = ComboBoxStyle.DropDownList,
        AutoSize = false, Width = 100, Overflow = ToolStripItemOverflow.Never,
        Alignment = ToolStripItemAlignment.Right
    };
    private readonly Icon appIcon;
    private readonly SessionState initialState;
    private readonly ExplorerPane[] panes;
    private readonly Panel paneHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
    private readonly List<(SplitContainer Control, string Path)> splits = [];
    private PaneLayout layoutTree;
    private readonly List<(int Pane, PaneLayout? Layout)> closedItems = [];
    private readonly List<(int Pane, ExplorerPane.FolderTab? Tab, TabGroup? Group, bool Global)> reopenedItems = [];
    private bool redoingClose;
    private int[] visiblePanes;
    private readonly Panel sidebar;
    private readonly Splitter sidebarSplitter;
    private readonly ToolStripButton sidebarButton = new("Sidebar") { AccessibleName = "Sidebar" };
    private bool showSidebar;
    private int sidebarWidth;
    private List<string> deletedFavorites => application.DeletedFavorites;
    private bool explorerFavoritesImported { get => application.State.ExplorerFavoritesImported; set => application.ChangeState(s => s with { ExplorerFavoritesImported = value }); }
    private readonly ListBox places;
    private readonly ListBox favoritePlaces;
    private readonly Button favoritesHeader;
    private bool favoritesExpanded;
    private List<string> favorites => application.Favorites;
    private readonly ToolStrip toolbar = Theme.Strip(outlineItems: true);
    private readonly ToolStripButton toolbarPin = new()
    {
        Name = "GlobalToolbarPin", Overflow = ToolStripItemOverflow.Never,
        DisplayStyle = ToolStripItemDisplayStyle.None, AutoSize = false
    };
    private static readonly GraphicsPath toolbarPinPath = CreateToolbarPinPath();
    private static readonly RectangleF toolbarPinBounds = toolbarPinPath.GetBounds();
    private readonly System.Windows.Forms.Timer toolbarHideTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer toolbarRevealTimer = new() { Interval = 350 };
    private bool globalToolbarPinned;
    private bool toolbarPointerInside;
    private bool toolbarCaptionPointerInside;
    private bool toolbarPinMouseDown;
    private readonly LinkLabel versionLabel = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom, Margin = Padding.Empty, AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Muted, BackColor = Theme.Canvas, Padding = new Padding(12, 4, 0, 4),
        AccessibleName = "Application version", LinkBehavior = LinkBehavior.HoverUnderline
    };
    // The bottom line: version on the left, author on the right.
    // A one-row table sizes itself to the taller label, also after font and DPI changes.
    private readonly TableLayoutPanel footer = new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1,
        BackColor = Theme.Canvas, Margin = Padding.Empty, Padding = Padding.Empty
    };
    private readonly Label authorLink = new()
    {
        Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom, Margin = Padding.Empty, AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Muted, BackColor = Theme.Canvas, Padding = new Padding(0, 4, 12, 4),
        AccessibleName = "Author", Text = "Made by HSoo-Kim (github.com/HSoo-Kim)"
    };
    private readonly ToolStripMenuItem updateItem = new() { CheckOnClick = true };
    private ReleaseInfo? availableUpdate => application.AvailableUpdate;
    private UpdateNotice? updateNotice;
    private bool checkUpdates => application.State.CheckUpdates;
    private readonly ToolStripMenuItem autoUpdateItem = new() { CheckOnClick = true };
    private bool autoUpdate => application.State.AutoUpdate;
    private bool updating => application.Updating;
    private readonly ToolStripButton[] layoutButtons = new ToolStripButton[4];
    private readonly ToolTip tips = new() { ShowAlways = true, AutoPopDelay = 10000 };
    private int activePane;
    private int previousPane = -1;
    private readonly List<(Form Window, PreviewPane Pane)> previewWindows = [];
    private int autoRestartMemory => application.State.AutoRestartMemory;
    private readonly ToolStripMenuItem memoryMenu = new();
    private FolderVisits folderVisits => application.FolderVisits;
    private readonly ToolStripMenuItem forgetVisitsItem = new();
    private (nint Header, nint List, int Column, int StartX, int StartWidth)? columnDrag;
    private readonly BrowserWindowState restoredPresentation;
    private readonly bool useDefaultWindowSize;
    private bool maximizedPresentation;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool StartMinimized { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool StartHidden { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool RestorePresentation { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool RegisterStartupOnLaunch { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal StartupRegistration Startup { get => application.Startup; set => application.Startup = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Func<IReadOnlyList<string>, bool>? RestartLauncher { get => application.RestartLauncher; set => application.RestartLauncher = value; }
    private List<Workspace> workspaces => application.Workspaces;
    private readonly ToolStripDropDownButton workspaceButton = new() { AccessibleName = "Workspaces" };
    internal event Action<bool, string?>? FileOperationFinished;
    private bool ready;
    private bool rebuilding;
    private bool tabDragging;
    private readonly record struct Place(string Label, string Path, bool Favorite = false)
    {
        public override string ToString() => Label;
    }

    public MainForm(string sessionPath, IReadOnlyList<string> folders, IPathSearchBackend? search = null,
        IReadOnlyList<string>? treeRoots = null, Func<CancellationToken, Task<ReleaseInfo?>>? latestRelease = null)
        : this(new FolderSplitterApplicationContext(sessionPath, folders, search, treeRoots, latestRelease, createWindows: false), null, folders, treeRoots) { }

    internal MainForm(FolderSplitterApplicationContext application, BrowserWindowState? state,
        IReadOnlyList<string> folders, IReadOnlyList<string>? treeRoots, bool useDefaultSize = false)
    {
        this.application = application;
        search = application.Search;
        initialState = SessionStore.ProjectWindow(application.State, state ?? application.State.Windows[0]);
        useDefaultWindowSize = useDefaultSize || (state is null && !application.HasSavedSession);
        application.Register(this);
        fontFamily = initialState.FontFamily is { } face && FontInstalled(face) ? face : null;
        textSize = initialState.TextSize;
        globalToolbarPinned = initialState.GlobalToolbarPinned;
        showFolderTree = initialState.ShowFolderTree;
        folderTreeHeight = initialState.FolderTreeHeight;
        text = new UiText(initialState.Language);
        layoutTree = initialState.LayoutTree!;
        visiblePanes = layoutTree.GetPanes().Order().ToArray();
        activePane = initialState.ActivePane;
        sidebarWidth = initialState.SidebarWidth;
        favoritesExpanded = initialState.FavoritesExpanded;
        Text = "Folder Splitter";
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        MinimumSize = new Size(900, 600);
        Size = new Size(initialState.WindowWidth, initialState.WindowHeight);
        StartPosition = FormStartPosition.CenterScreen;
        var restored = state ?? application.State.Windows[0];
        if (restored.WindowX is { } x && restored.WindowY is { } y)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
        }
        maximizedPresentation = restored.Maximized;
        restoredPresentation = restored;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(UiFace, UiPoints);
        Theme.ListFont = ListFontChoice();
        UpdateVersionText();
        languageBindings.Add(UpdateVersionText);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.Controls.Add(versionLabel, 0, 0);
        footer.Controls.Add(authorLink, 1, 0);
        versionLabel.LinkClicked += async (_, _) => await OpenAvailableUpdateAsync();
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("QuadExplorer.AppIcon")!)
        using (var sourceIcon = new Icon(iconStream))
            appIcon = (Icon)sourceIcon.Clone();
        Icon = appIcon;

        panes = Enumerable.Range(0, 4).Select(i => new ExplorerPane(i + 1, initialState.Panes[i], text,
            search, SearchRoots)).ToArray();
        showCommandBar = initialState.ShowCommandBar;
        showVerticalSeparators = initialState.ShowVerticalSeparators;
        comfortableRows = initialState.ComfortableRows;
        highlightSortedColumn = initialState.HighlightSortedColumn;
        foreach (var pane in panes)
        {
            pane.SetActivePaneColor(activePaneColor);
            pane.ActivePaneColorRequested += SetActivePaneColor;
            pane.SetCommandBarVisible(showCommandBar);
            pane.SetDetailsAppearance(showVerticalSeparators, comfortableRows, highlightSortedColumn);
            pane.CommandBarToggled += (_, _) =>
            {
                application.ChangeSettings(s => s with { ShowCommandBar = !s.ShowCommandBar });
            };
            pane.DetailsAppearanceRequested += (separators, comfortable, highlight) =>
            {
                application.ChangeSettings(s => s with { ShowVerticalSeparators = separators,
                    ComfortableRows = comfortable, HighlightSortedColumn = highlight });
            };
            pane.Activated += (_, _) => ActivatePane(Array.IndexOf(panes, pane));
            pane.TabClosed += (_, _) => RememberClosed(pane.PaneNumber - 1, null);
            pane.ClosePaneRequested += (_, _) => ClosePane(pane.PaneNumber - 1);
            pane.UndoCloseRequested += (_, _) => pane.ReopenTab();
            pane.RedoCloseRequested += (_, _) => RedoClosedItem(pane.PaneNumber - 1);
            pane.TabReopened += (_, _) =>
            {
                int index = closedItems.FindLastIndex(item => item.Pane == pane.PaneNumber - 1 && item.Layout is null);
                if (index >= 0) closedItems.RemoveAt(index);
                reopenedItems.Add((pane.PaneNumber - 1, pane.ActiveTab, pane.LastReopenedGroup, true));
                RefreshCloseHistory();
            };
            pane.StateChanged += (_, _) =>
            {
                ScheduleSave();
                if (ready && ReferenceEquals(pane, Active)) SyncTree();
                if (ReferenceEquals(pane, Active)) SchedulePreview();
                RefreshCloseHistory();
            };
            pane.SelectionChanged += (_, _) => { if (ReferenceEquals(pane, Active)) SchedulePreview(); };
            pane.FavoriteRequested += (_, path) => AddFavorite(path);
            pane.MoveTabRequested += MoveTab;
            pane.TabDragRequested += StartTabDrag;
            pane.SplitTabRequested += (source, tab, edge) => DockTab(source, tab, source.PaneNumber - 1, edge);
            pane.MoveGroupRequested += (source, group, number) => DockGroup(source, group, number - 1, null);
            pane.GroupMoveDisabledReason = (source, group, number) =>
                CanDockGroup(source, group, number - 1, null, out string reason) ? null : reason;
            pane.GroupDragRequested += StartGroupDrag;
            pane.TransferRequested += (source, move) => { ActivatePane(Array.IndexOf(panes, source)); TransferSelection(move); };
            pane.OpenInOtherPaneRequested += source => { ActivatePane(Array.IndexOf(panes, source)); OpenFolderInOtherPane(); };
            pane.FolderVisited += (_, path) => { if (ready) { folderVisits.Record(path); ScheduleSave(); } };
            pane.BulkRenameRequested += (_, _) => OpenBulkRename(pane);
            pane.CompareRequested += (_, _) => OpenCompare(pane);
            pane.DetailsPaneToggled += (_, _) => SetDetailsPaneVisible(!(showDetails && showPreview));
            pane.ShellSettingsChanged += (_, _) => application.RefreshShellViews();
            pane.DetailsPaneShown = () => showDetails && showPreview;
        }

        showPreview = initialState.ShowPreview;
        showDetails = initialState.ShowDetails;
        previewWidth = initialState.PreviewWidth > 0 ? Math.Clamp(initialState.PreviewWidth, 200, 1600) : 380;
        previewPane = new PreviewPane(text) { Dock = DockStyle.Right, Width = previewWidth, Visible = showPreview };
        previewPane.SetDetailsVisible(showDetails);
        previewSplitter = new Splitter
        {
            Dock = DockStyle.Right, Width = 6, MinSize = 200, MinExtra = 300, Visible = showPreview,
            BackColor = Theme.Border, AccessibleName = "Resize preview"
        };
        previewSplitter.SplitterMoved += (_, _) => { previewWidth = previewPane.Width; ScheduleSave(); };
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); UpdatePreview(); };
        previewPane.DragRequested += point =>
        {
            SetPreviewFloating(true);
            previewWindow!.BeginMove(point);
        };
        previewPane.DetachRequested += (_, _) => SetPreviewFloating(true);
        previewPane.NewWindowRequested += OpenPreviewWindow;
        previewPane.PinChanged += (_, _) => { if (!previewPane.Pinned) UpdatePreview(); };
        VisibleChanged += (_, _) =>
        {
            foreach (var (window, _) in previewWindows)
                if (Visible) window.Show(this);
                else window.Hide();
            if (previewWindow is null) return;
            if (Visible && showPreview) previewWindow.Show(this);
            else previewWindow.Hide();
        };
        languageBindings.Add(() =>
        {
            foreach (var (window, pane) in previewWindows)
            {
                pane.RefreshLanguage();
                if (pane.CurrentPath is { } path) window.Text = Path.GetFileName(path) + " - " + text.T("Preview");
            }
        });
        languageBindings.Add(previewPane.RefreshLanguage);
        languageBindings.Add(() => previewWindow?.RefreshLanguage());
        sidebar = new Panel { Dock = DockStyle.Left, Width = sidebarWidth, Padding = new Padding(10, 12, 8, 8), BackColor = Theme.Canvas };
        sidebarSplitter = new Splitter
        {
            Dock = DockStyle.Left, Width = 6, MinSize = 140, MinExtra = 300,
            BackColor = Theme.Canvas, AccessibleName = "Resize quick access" // an invisible handle: the cards already separate
        };
        sidebarSplitter.SplitterMoved += (_, _) =>
        {
            sidebarWidth = Math.Clamp((int)Math.Round(sidebar.Width * 96d / DeviceDpi), 140, 480);
            sidebar.Width = (int)Math.Round(sidebarWidth * DeviceDpi / 96d);
            UpdateMinimumSize();
            ScheduleSave();
        };
        sidebarSplitter.DoubleClick += (_, _) => SetSidebarVisible(false);
        var sideTitle = new Label
        {
            Text = "Quick access", Dock = DockStyle.Top, Height = 28,
            Font = HeaderFont(), ForeColor = Theme.Muted,
            Padding = new Padding(7, 0, 0, 0)
        };
        BindText(sideTitle, "Quick access");
        sideTitleLabel = sideTitle;
        places = new ListBox
        {
            Dock = DockStyle.Top, Height = 216, BorderStyle = BorderStyle.None, BackColor = Theme.Canvas,
            ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 32,
            IntegralHeight = false, AccessibleName = "Quick access"
        };
        places.DrawItem += DrawPlace;
        TrackHover(places);
        // An owner-drawn list keeps old pixels when it only gets narrower, leaving tails of long names; repaint it all.
        places.Resize += (_, _) => places.Invalidate();
        places.MouseClick += (_, _) => OpenPlace();
        places.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenPlace(); e.Handled = true; }
        };
        var placeMenu = new ContextMenuStrip();
        BindText(placeMenu.Items.Add("Open in new tab", null, (_, _) =>
        {
            if (places.SelectedItem is Place place) Active.AddTab(place.Path);
        }), "Open in new tab");
        BindText(placeMenu.Items.Add("Remove favorite", null, (_, _) =>
        {
            if (places.SelectedItem is Place { Favorite: true } place)
            RemoveFavorite(place.Path);
        }), "Remove favorite");
        placeMenu.Opening += (_, _) => placeMenu.Items[1].Enabled = places.SelectedItem is Place { Favorite: true };
        places.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) places.SelectedIndex = places.IndexFromPoint(e.Location); };
        places.ContextMenuStrip = placeMenu;
        favoritePlaces = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Canvas,
            ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 32,
            IntegralHeight = false, AccessibleName = "Favorites"
        };
        favoritePlaces.DrawItem += DrawPlace;
        TrackHover(favoritePlaces);
        favoritePlaces.Resize += (_, _) => favoritePlaces.Invalidate();
        favoritePlaces.MouseClick += (_, _) => { if (favoritePlaces.SelectedItem is Place p) Active.Navigate(p.Path); };
        favoritePlaces.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && favoritePlaces.SelectedItem is Place p)
            { Active.Navigate(p.Path); e.Handled = true; }
        };
        var favoriteMenu = new ContextMenuStrip();
        BindText(favoriteMenu.Items.Add("Open in new tab", null, (_, _) =>
        { if (favoritePlaces.SelectedItem is Place p) Active.AddTab(p.Path); }), "Open in new tab");
        BindText(favoriteMenu.Items.Add("Remove favorite", null, (_, _) =>
        {
            if (favoritePlaces.SelectedItem is Place p)
            RemoveFavorite(p.Path);
        }), "Remove favorite");
        favoriteMenu.Items.Add(new ToolStripSeparator());
        BindText(favoriteMenu.Items.Add("Import from File Explorer", null, (_, _) => ImportExplorerFavorites()), "Import from File Explorer");
        BindText(favoriteMenu.Items.Add("Remove all favorites", null, (_, _) => ClearFavorites()), "Remove all favorites");
        BindText(favoriteMenu.Items.Add("Restore favorites...", null, (_, _) => ShowRestoreFavorites()), "Restore favorites...");
        favoritePlaces.ContextMenuStrip = favoriteMenu;
        favoritePlaces.MouseDown += (_, e) =>
        { if (e.Button == MouseButtons.Right) favoritePlaces.SelectedIndex = favoritePlaces.IndexFromPoint(e.Location); };
        favoritesHeader = Theme.Button("", "Collapse favorites", ToggleFavorites, quiet: true);
        favoritesHeader.Dock = DockStyle.Top;
        favoritesHeader.Height = 32;
        favoritesHeader.Font = HeaderFont();
        favoritesHeader.TextAlign = ContentAlignment.MiddleLeft;
        favoritesHeader.ForeColor = Theme.Muted;
        favoritesHeader.FlatAppearance.BorderSize = 0;
        favoritesHeader.BackColor = Theme.Canvas;
        BindTip(favoritesHeader, "Collapse / expand favorites");
        BindTip(sidebarSplitter, "Drag to resize quick access; double-click to hide");
        favoritesHeader.ContextMenuStrip = favoriteMenu;
        var manage = Theme.Button("Manage...", "Manage favorites", () => favoriteMenu.Show(Cursor.Position), quiet: true);
        manage.Dock = DockStyle.Bottom;
        manage.BackColor = Theme.Canvas;
        manage.TextAlign = ContentAlignment.MiddleLeft;
        manage.Padding = new Padding(6, 0, 0, 0);
        manage.AutoSize = true;
        BindText(manage, "Manage...");
        BindTip(manage, "Manage favorites: import, remove all, restore");
        var pin = Theme.Button("+  Pin folder", "Pin current folder to favorites", () => AddFavorite(Active.CurrentPath), 152, quiet: true);
        pin.Dock = DockStyle.Bottom;
        pin.Height = 36;
        pin.BackColor = Theme.Canvas;
        pin.TextAlign = ContentAlignment.MiddleLeft;
        pin.Padding = new Padding(6, 0, 0, 0);
        BindText(pin, "+  Pin folder");
        BindTip(pin, "Pin current folder to favorites (Ctrl+D)");
        sidebar.Controls.Add(favoritePlaces);
        sidebar.Controls.Add(favoritesHeader);
        sidebar.Controls.Add(places);
        sidebar.Controls.Add(pin);
        sidebar.Controls.Add(manage);
        sidebar.Controls.Add(sideTitle);
        ReloadPlaces();
        folderTree = new FolderTree(text, treeRoots)
        {
            Dock = DockStyle.Fill, AccessibleName = "Folder tree", Visible = showFolderTree
        };
        folderTree.OpenRequested += OpenTreeFolder;
        // The splitter resizes this bottom panel; the favorites list fills the space left above it.
        treeHeader = Theme.Button("", "Collapse folder tree", () => SetFolderTreeVisible(!showFolderTree), quiet: true);
        treeHeader.Dock = DockStyle.Top;
        treeHeader.Height = 32;
        treeHeader.Font = favoritesHeader.Font;
        treeHeader.TextAlign = ContentAlignment.MiddleLeft;
        treeHeader.ForeColor = Theme.Muted;
        treeHeader.FlatAppearance.BorderSize = 0;
        treeHeader.BackColor = Theme.Canvas;
        BindTip(treeHeader, "Collapse / expand folder tree (Ctrl+Shift+E)");
        languageBindings.Add(UpdateTreeHeader);
        UpdateTreeHeader();
        treePanel.Controls.Add(folderTree);
        treePanel.Controls.Add(treeHeader);
        treeSplitter.Visible = showFolderTree;
        treeSplitter.SplitterMoved += (_, _) =>
        {
            folderTreeHeight = (int)Math.Round((treePanel.Height - treeHeader.Height) * 96d / DeviceDpi);
            ScheduleSave();
        };
        BindTip(treeSplitter, "Drag to resize favorites and the folder tree");
        sidebar.Controls.Add(treeSplitter);
        sidebar.Controls.Add(treePanel);
        sidebar.SizeChanged += (_, _) => UpdateSidebarContent();
        UpdateSidebarContent();

        BuildToolbar();
        // Adjacent top buttons touch, so each run reads as one rounded group.
        foreach (ToolStripItem item in toolbar.Items)
            if (item is ToolStripButton or ToolStripDropDownButton) item.Margin = new Padding(0, 1, 0, 2);
        languageBindings.Add(RefreshGlobalToolbar);
        toolbar.DpiChangedAfterParent += (_, _) => RefreshGlobalToolbar();
        RefreshGlobalToolbar();
        var workspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 8) };
        workspace.Controls.Add(paneHost);
        workspace.Controls.Add(previewSplitter);
        workspace.Controls.Add(previewPane);
        workspace.Controls.Add(sidebarSplitter);
        workspace.Controls.Add(sidebar);
        // Choose the initial dock mode before parenting the strip. Switching an
        // attached strip to an anchored overlay during construction can lay out
        // controls before the form has finished initializing.
        if (!globalToolbarPinned)
        {
            toolbar.Dock = DockStyle.None;
            toolbar.AutoSize = false;
            toolbar.Visible = false;
        }
        Controls.Add(workspace);
        Controls.Add(footer);
        Controls.Add(toolbar);
        ConfigureGlobalToolbar();
        toolbar.MouseMove += (_, _) => { toolbarPointerInside = true; toolbarHideTimer.Stop(); };
        toolbar.MouseEnter += (_, _) => { toolbarPointerInside = true; toolbarHideTimer.Stop(); };
        toolbar.MouseLeave += (_, _) => { toolbarPointerInside = false; ScheduleToolbarHide(); };
        toolbar.Leave += (_, _) => ScheduleToolbarHide();
        languageSelector.ComboBox.MouseEnter += (_, _) => { toolbarPointerInside = true; toolbarHideTimer.Stop(); };
        languageSelector.ComboBox.MouseLeave += (_, _) => { toolbarPointerInside = false; ScheduleToolbarHide(); };
        languageSelector.DropDownClosed += (_, _) => ScheduleToolbarHide();
        foreach (var item in toolbar.Items.OfType<ToolStripDropDownItem>())
            item.DropDownClosed += (_, _) => ScheduleToolbarHide();
        toolbar.OverflowButton.DropDownClosed += (_, _) => ScheduleToolbarHide();
        toolbarHideTimer.Tick += (_, _) => HideGlobalToolbarIfIdle();
        toolbarRevealTimer.Tick += (_, _) =>
        {
            toolbarRevealTimer.Stop();
            if (toolbarCaptionPointerInside) ShowGlobalToolbarOverlay();
        };
        ClientSizeChanged += (_, _) =>
        {
            if (!globalToolbarPinned) HideGlobalToolbarOverlay();
            PositionGlobalToolbarOverlay();
        };
        Deactivate += (_, _) => { if (!globalToolbarPinned) HideGlobalToolbarOverlay(); };
        DpiChanged += (_, _) => { RefreshGlobalToolbar(); UpdateMinimumSize(); };
        showSidebar = initialState.ShowSidebar;
        sidebar.Visible = initialState.ShowSidebar;
        sidebarSplitter.Visible = initialState.ShowSidebar;
        Load += (_, _) =>
        {
            UpdateMinimumSize();
            // Load runs after the first DPI scale, but before the HWND is visible. Saved bounds are pixels.
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Bounds = BrowserWindowGeometry.Restore(restoredPresentation, work, DeviceDpi, useDefaultWindowSize);
            if (initialState.Maximized) WindowState = FormWindowState.Maximized;
            if (StartMinimized || StartHidden || (RestorePresentation && (restoredPresentation.Minimized || restoredPresentation.Hidden)))
                WindowState = FormWindowState.Minimized;
        };
        Shown += (_, _) =>
        {
            if ((StartHidden || (!StartMinimized && RestorePresentation && restoredPresentation.Hidden)) && closeToTray) BeginInvoke(Hide);
            rebuilding = true;
            try { ApplySplitRatios(); }
            finally { rebuilding = false; }
            ready = true;
            ApplyAppearance();
            if (!explorerFavoritesImported) ImportExplorerFavorites();
            ActivatePane(activePane);
            if (folders.Count > 0 && !File.Exists(sessionPath)) Active.CloseTab(remember: false);
            foreach (string folder in folders) Active.AddTab(folder);
            Active.FocusFiles();
            application.WindowShown(this, RegisterStartupOnLaunch);
        };
        ResizeEnd += (_, _) => ScheduleSave();
        SizeChanged += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) maximizedPresentation = WindowState == FormWindowState.Maximized;
            ScheduleSave();
        };
        RenderLayout(layoutTree);
        Application.AddMessageFilter(this);
        ApplyAppearance();
    }

    private ExplorerPane Active => panes[activePane];
    private IReadOnlyList<string> SearchRoots() => new[] { Active.CurrentPath }
        .Concat(panes.SelectMany(p => p.CaptureState().Tabs)).Concat(favorites)
        .Concat(panes.SelectMany(p => p.RecentPaths))
        .Concat(folderVisits.Frequent())
        .Where(Path.IsPathRooted).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    internal PaneLayout CurrentLayout => layoutTree;
    internal IReadOnlyList<FolderVisit> FolderVisitEntries => folderVisits.Entries;
    internal ExplorerPane GetPane(int id) => panes[id];
    internal string Language => text.Language;

    internal void SetLanguage(string language)
    {
        application.ChangeSettings(s => s with { Language = SessionStore.NormalizeLanguage(language) });
    }

    private void BindText(Control control, string key)
    {
        void Refresh() => control.Text = text.T(key);
        languageBindings.Add(Refresh);
        Refresh();
    }

    private void BindText(ToolStripItem item, string key, string? tip = null)
    {
        void Refresh()
        {
            item.Text = text.T(key);
            if (tip is not null) item.ToolTipText = text.T(tip);
        }
        languageBindings.Add(Refresh);
        Refresh();
    }

    // Familiar actions (settings, help, language) show their standard glyph; the words move to the tooltip.
    private void BindIcon(ToolStripItem item, string glyph, string tip)
    {
        item.Text = glyph;
        item.Font = new Font(Theme.IconFont, 10.5f);
        void Refresh() => item.ToolTipText = text.T(tip);
        languageBindings.Add(Refresh);
        Refresh();
    }

    private readonly Dictionary<ListBox, int> hoverRows = [];

    // Sidebar rows answer the pointer with a soft wash, like every other control.
    private void TrackHover(ListBox list)
    {
        void Redraw(int index) { if (index >= 0 && index < list.Items.Count) list.Invalidate(list.GetItemRectangle(index)); }
        void Set(int index)
        {
            int old = hoverRows.GetValueOrDefault(list, -1);
            if (old == index) return;
            hoverRows[list] = index;
            Redraw(old);
            Redraw(index);
        }
        list.MouseMove += (_, e) => { int index = list.IndexFromPoint(e.Location); Set(index < list.Items.Count ? index : -1); };
        list.MouseLeave += (_, _) => Set(-1);
    }

    private void BindTip(Control control, string key)
    {
        void Refresh() => tips.SetToolTip(control, text.T(key));
        languageBindings.Add(Refresh);
        Refresh();
    }

    private void BuildToolbar()
    {
        toolbar.Padding = new Padding(10, 7, 10, 7);
        toolbar.AccessibleName = "Global toolbar";
        toolbarPin.Paint += (_, e) => PaintToolbarPin(e.Graphics);
        toolbarPin.MouseDown += (_, _) => toolbarPinMouseDown = true;
        toolbarPin.MouseLeave += (_, _) => toolbarPinMouseDown = false;
        toolbarPin.Click += (_, _) =>
        {
            bool mouse = toolbarPinMouseDown;
            toolbarPinMouseDown = false;
            SetGlobalToolbarPinned(!globalToolbarPinned, mouse);
        };
        toolbar.Items.Add(toolbarPin);
        sidebarButton.CheckOnClick = true;
        sidebarButton.Checked = initialState.ShowSidebar;
        BindText(sidebarButton, "Sidebar", "Show / hide quick access sidebar");
        sidebarButton.Click += (_, _) => SetSidebarVisible(sidebarButton.Checked);
        toolbar.Items.Add(sidebarButton);
        treeButton.Checked = showFolderTree;
        BindText(treeButton, "Folder tree", "Show / hide folder tree (Ctrl+Shift+E)");
        treeButton.Click += (_, _) => SetFolderTreeVisible(treeButton.Checked);
        toolbar.Items.Add(treeButton);
        previewButton.Checked = showPreview;
        BindText(previewButton, "Preview", "Show / hide preview pane (Alt+P)");
        previewButton.Click += (_, _) => SetPreviewVisible(previewButton.Checked);
        toolbar.Items.Add(previewButton);
        toolbar.Items.Add(new ToolStripSeparator());
        foreach (var (button, name, tip) in new[]
        {
            (undoClose, "Undo close", "Undo tab or pane close in any pane (Ctrl+Shift+T)"),
            (redoClose, "Redo close", "Redo tab or pane close in any pane")
        })
        {
            button.AccessibleName = name;
            button.Alignment = ToolStripItemAlignment.Right;
            button.Overflow = ToolStripItemOverflow.Never;
            button.Enabled = false;
            void Refresh() => button.ToolTipText = text.T(tip);
            languageBindings.Add(Refresh);
            Refresh();
        }
        undoClose.Click += (_, _) => ReopenClosedItem();
        redoClose.Click += (_, _) => RedoClosedItem();
        int[] counts = [1, 2, 3, 4];
        for (int i = 0; i < counts.Length; i++)
        {
            int count = counts[i];
            var button = new ToolStripButton($"{count} pane{(count == 1 ? "" : "s")}")
            {
                AutoSize = true,
                AccessibleName = $"{count} pane layout"
            };
            void Refresh()
            {
                button.Text = count == 1 ? text.T("1 pane") : text.T("{0} panes", count);
                button.ToolTipText = text.T("Split into {0} panes (Ctrl+{0})", count);
            }
            languageBindings.Add(Refresh);
            Refresh();
            button.Click += (_, _) => ApplyLayout(count);
            layoutButtons[i] = button;
            toolbar.Items.Add(button);
            if (count == 3)
            {
                var arrangements = new ToolStripDropDownButton("\uE70D")
                {
                    Font = new Font(Theme.IconFont, 8),
                    AccessibleName = "Three pane arrangements",
                    ToolTipText = text.T("Choose three-pane arrangement: left/right or top/bottom 2:1"),
                    ShowDropDownArrow = false
                };
                arrangements.DropDownOpening += (_, _) => arrangements.DropDown.Font = Font;
                languageBindings.Add(() => arrangements.ToolTipText = text.T("Choose three-pane arrangement: left/right or top/bottom 2:1"));
                BindText(arrangements.DropDownItems.Add("Two left, one right", null, (_, _) => ApplyLayout(3)), "Two left, one right");
                BindText(arrangements.DropDownItems.Add("One left, two right", null, (_, _) => ApplyLayout(3, false)), "One left, two right");
                BindText(arrangements.DropDownItems.Add("Two above, one below", null, (_, _) => ApplyLayout(3, true, false)), "Two above, one below");
                BindText(arrangements.DropDownItems.Add("One above, two below", null, (_, _) => ApplyLayout(3, false, false)), "One above, two below");
                toolbar.Items.Add(arrangements);
            }
        }
        toolbar.Items.Add(new ToolStripSeparator());
        var equalize = new ToolStripButton { AccessibleName = "Equal size" };
        BindText(equalize, "Equal size", "Equalize pane sizes");
        equalize.Click += (_, _) => EqualizeSplits();
        toolbar.Items.Add(equalize);
        toolbar.Items.Add(new ToolStripSeparator());
        BindText(workspaceButton, "Workspaces", "Save or open a named set of panes and tabs");
        workspaceButton.DropDownItems.Add("\u2026");
        workspaceButton.DropDownOpening += (_, _) => BuildWorkspaceMenu();
        toolbar.Items.Add(workspaceButton);
        languageBindings.Add(RefreshThemeButton);
        RefreshThemeButton();
        themeButton.DropDownItems.Add("\u2026");
        themeButton.DropDownOpening += (_, _) =>
        {
            foreach (var old in themeButton.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
            foreach (string mode in SessionStore.ColorThemes)
            {
                if (mode is "solarized" or "everforest-dark") themeButton.DropDownItems.Add(new ToolStripSeparator());
                var item = new ToolStripMenuItem(ThemeLabel(mode)) { Checked = mode == colorTheme };
                item.Click += (_, _) => SetColorTheme(mode);
                themeButton.DropDownItems.Add(item);
            }
            themeButton.DropDownItems.Add(new ToolStripSeparator());
            bool activeListed = false;
            foreach (var pair in recentCustomColors)
            {
                Color background = SessionStore.ParseColor(pair.Background!);
                Color accent = SessionStore.ParseColor(pair.Accent!);
                var image = CreateCustomColorSwatch(background, accent, toolbar.DeviceDpi);
                var item = new ToolStripMenuItem(pair.Background + " · " + pair.Accent, image)
                {
                    AccessibleName = "Custom colors " + pair.Background + " " + pair.Accent,
                    ImageScaling = ToolStripItemImageScaling.None,
                    Checked = colorTheme == SessionStore.CustomTheme
                        && pair.Background == customBackground && pair.Accent == customAccent
                };
                activeListed |= item.Checked;
                item.Click += (_, _) => SetCustomColors(accent, background);
                item.Disposed += (_, _) => image.Dispose();
                themeButton.DropDownItems.Add(item);
            }
            // The active custom theme is checked on its own combination above; the dialog entry only
            // carries the check when that combination is not listed.
            var custom = new ToolStripMenuItem(text.T("Custom colors…"))
            {
                AccessibleName = "Custom colors", Checked = colorTheme == SessionStore.CustomTheme && !activeListed
            };
            custom.Click += (_, _) => ShowCustomColors();
            themeButton.DropDownItems.Add(custom);
            Theme.Apply(toolbar);
        };
        themeButton.Alignment = ToolStripItemAlignment.Right;
        themeButton.Overflow = ToolStripItemOverflow.Never;
        var help = new ToolStripButton
        {
            Alignment = ToolStripItemAlignment.Right, AccessibleName = "Shortcuts",
            Overflow = ToolStripItemOverflow.Never
        };
        BindIcon(help, "\uE897", "Show shortcuts and help (F1)");
        help.Click += (_, _) => ShowHelp();
        toolbar.Items.Add(help);
        var close = new ToolStripButton
        {
            Alignment = ToolStripItemAlignment.Right, AccessibleName = "Close tab",
            Overflow = ToolStripItemOverflow.Never
        };
        BindText(close, "Close tab", "Close current tab (Ctrl+W)");
        close.Click += (_, _) => Active.CloseTab();
        toolbar.Items.Add(close);
        var languageLabel = new ToolStripLabel
        {
            Alignment = ToolStripItemAlignment.Right, AccessibleName = "Language",
            Overflow = ToolStripItemOverflow.Never
        };
        BindIcon(languageLabel, "\uE774", "Language");
        languageSelector.Items.AddRange(["한국어", "English", "简体中文", "日本語"]);
        languageSelector.SelectedIndex = Array.IndexOf(new[] { "ko", "en", "zh-CN", "ja" }, text.Language);
        languageSelector.SelectedIndexChanged += (_, _) =>
        {
            if (languageSelector.SelectedIndex >= 0)
                SetLanguage(new[] { "ko", "en", "zh-CN", "ja" }[languageSelector.SelectedIndex]);
        };
        languageBindings.Add(() => languageSelector.ToolTipText = text.T("Choose UI language"));
        languageSelector.ToolTipText = text.T("Choose UI language");
        // Drawn by the app, so the box follows the theme's colors instead of staying a white system box.
        languageSelector.ComboBox.DrawMode = DrawMode.OwnerDrawFixed;
        Theme.ThemeComboBox(languageSelector.ComboBox);
        languageSelector.ComboBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool highlighted = e.State.HasFlag(DrawItemState.Selected) && !e.State.HasFlag(DrawItemState.ComboBoxEdit);
            using var back = new SolidBrush(highlighted ? Theme.Selected : Theme.Surface);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, languageSelector.Items[e.Index]?.ToString(), e.Font ?? Font, e.Bounds, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        toolbar.Items.Add(languageSelector);
        toolbar.Items.Add(languageLabel);
        toolbar.Items.Add(themeButton); // Right-aligned items run right to left: theme sits left of the language label.
        var settings = new ToolStripDropDownButton
        {
            Alignment = ToolStripItemAlignment.Right, AccessibleName = "Settings", Overflow = ToolStripItemOverflow.Never
        };
        BindIcon(settings, "\uE713", "Shortcuts and Windows integration");
        BindText(hotkeyItem, "Win+Alt+E brings Folder Splitter to the front");
        BindText(winEItem, "Open Folder Splitter with Win+E instead of File Explorer");
        BindText(startupItem, "Start Folder Splitter with Windows");
        hotkeyItem.Checked = globalHotkey;
        hotkeyItem.CheckedChanged += (_, _) => SetGlobalHotkey(hotkeyItem.Checked);
        winEItem.Click += (_, _) => SetWinEShortcut(winEItem.Checked);
        startupItem.Click += (_, _) => SetStartWithWindows(startupItem.Checked);
        settings.DropDownOpening += (_, _) =>
        {
            winEItem.Checked = winE.IsEnabled;
            startupItem.Checked = Startup.IsEnabled;
            startupItem.Enabled = Startup.IsSupported;
        };
        settings.DropDownItems.Add(hotkeyItem);
        settings.DropDownItems.Add(winEItem);
        settings.DropDownItems.Add(startupItem);
        BindText(trayItem, "Keep running in the notification area when closed");
        trayItem.Checked = closeToTray;
        trayItem.CheckedChanged += (_, _) => SetCloseToTray(trayItem.Checked);
        settings.DropDownItems.Add(trayItem);
        BindText(updateItem, "Check for new versions");
        updateItem.Checked = checkUpdates;
        updateItem.CheckedChanged += (_, _) => SetCheckUpdates(updateItem.Checked);
        settings.DropDownItems.Add(updateItem);
        BindText(autoUpdateItem, "Install automatically when Update is clicked");
        autoUpdateItem.Checked = autoUpdate;
        autoUpdateItem.CheckedChanged += (_, _) => SetAutoUpdate(autoUpdateItem.Checked);
        settings.DropDownItems.Add(autoUpdateItem);
        BindText(memoryMenu, "Memory cleanup");
        memoryMenu.DropDownItems.Add("\u2026");
        memoryMenu.DropDownOpening += (_, _) => BuildMemoryMenu();
        settings.DropDownItems.Add(memoryMenu);
        BindText(fontMenu, "Font and text size");
        fontMenu.DropDownItems.Add("\u2026");
        fontMenu.DropDownOpening += (_, _) => BuildFontMenu();
        settings.DropDownItems.Add(fontMenu);
        BindText(forgetVisitsItem, "Forget frequently opened folders");
        forgetVisitsItem.Click += (_, _) =>
        {
            application.ClearVisits();
            Active.SetLocalizedStatus("Frequently opened folders were cleared.");
        };
        settings.DropDownItems.Add(forgetVisitsItem);
        settings.DropDownItems.Add(new ToolStripSeparator());
        BindText(settings.DropDownItems.Add("", null, (_, _) => application.CreateWindowFromActive(this)), "New window");
        BindText(settings.DropDownItems.Add("", null, (_, _) => ExitApplication()), "Exit");
        toolbar.Items.Add(settings);
        toolbar.Items.Add(redoClose);
        toolbar.Items.Add(undoClose);
        toolbar.Layout += (_, _) =>
        {
            int target = (int)Math.Round(24 * toolbar.DeviceDpi / 96d);
            undoClose.Size = redoClose.Size = new Size(target, target);
            redoClose.Margin = new Padding(0, 1, (int)Math.Round(12 * toolbar.DeviceDpi / 96d), 2);
            int width = Math.Max(100, languageSelector.Items.Cast<string>()
                .Max(name => TextRenderer.MeasureText(name, languageSelector.Font).Width)
                + (int)Math.Ceiling(32 * toolbar.DeviceDpi / 96d));
            if (languageSelector.Width != width) languageSelector.Width = width;
            languageSelector.DropDownWidth = width;
        };
    }

    private void SetGlobalToolbarPinned(bool pinned, bool mouseClick = false)
    {
        if (globalToolbarPinned == pinned) return;
        bool keepOverlay = !pinned && (mouseClick || toolbarPointerInside || ToolbarHasInteraction());
        // Resizing a SplitContainer raises SplitterMoved even without a user drag.
        // Keep the saved ratios while the toolbar changes the available height.
        bool wasRebuilding = rebuilding;
        rebuilding = true;
        try
        {
            globalToolbarPinned = pinned;
            SuspendLayout();
            try
            {
                RefreshGlobalToolbar();
                ConfigureGlobalToolbar(keepOverlay);
            }
            finally { ResumeLayout(true); }
            UpdateMinimumSize();
            ApplySplitRatios();
        }
        finally { rebuilding = wasRebuilding; }
        // A mouse click should not leave incidental ToolStrip focus trapping the overlay.
        // Keyboard users keep their focus and can continue navigating the toolbar.
        if (!pinned && mouseClick && toolbar.ContainsFocus) Active.FocusFiles();
        if (!pinned && mouseClick) toolbarPointerInside = true;
        if (!pinned) ScheduleToolbarHide();
        ScheduleSave();
        application.ChangeSettings(s => s with { GlobalToolbarPinned = pinned });
    }

    private static GraphicsPath CreateToolbarPinPath()
    {
        // Keep the solid E840 pin, using the rounder Fluent outline when available.
        // Windows 10 falls back to the same glyph in Segoe MDL2 Assets.
        using var family = new FontFamily(Theme.IconFont);
        using var glyph = new GraphicsPath();
        glyph.AddString("\uE840", family, (int)FontStyle.Regular, 1000, PointF.Empty, StringFormat.GenericTypographic);
        PointF[] points = glyph.PathPoints;
        byte[] types = glyph.PathTypes;
        int count = Array.FindIndex(types, type => (type & (byte)PathPointType.CloseSubpath) != 0) + 1;
        return new GraphicsPath(points[..count], types[..count]);
    }

    private void PaintToolbarPin(Graphics graphics)
    {
        float size = LogicalToDeviceUnits(14);
        float scale = size / Math.Max(toolbarPinBounds.Width, toolbarPinBounds.Height);
        float x = (toolbarPin.Width - toolbarPinBounds.Width * scale) / 2 - toolbarPinBounds.X * scale;
        float y = (toolbarPin.Height - toolbarPinBounds.Height * scale) / 2 - toolbarPinBounds.Y * scale;
        GraphicsState state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(x, y);
            graphics.ScaleTransform(scale, scale);
            using var brush = new SolidBrush(Theme.Text);
            graphics.FillPath(brush, toolbarPinPath);
        }
        finally { graphics.Restore(state); }
    }

    private void RefreshGlobalToolbar()
    {
        toolbarPin.AccessibleName = text.T(globalToolbarPinned ? "Unpin global toolbar" : "Pin global toolbar");
        toolbarPin.ToolTipText = text.T(globalToolbarPinned
            ? "Unpin global toolbar (Ctrl+Shift+F11)"
            : "Pin global toolbar (Ctrl+Shift+F11)");
        toolbarPin.AccessibleDescription = toolbarPin.ToolTipText;
        toolbarPin.Size = new Size(LogicalToDeviceUnits(26), LogicalToDeviceUnits(24));
        toolbarPin.Checked = globalToolbarPinned;
        toolbar.Padding = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(7),
            LogicalToDeviceUnits(10), LogicalToDeviceUnits(7));
        PositionGlobalToolbarOverlay();
    }

    private void ConfigureGlobalToolbar(bool showOverlay = false)
    {
        toolbarHideTimer.Stop();
        toolbarRevealTimer.Stop();
        if (globalToolbarPinned)
        {
            toolbarCaptionPointerInside = false;
            toolbar.AutoSize = true;
            toolbar.Dock = DockStyle.Top;
            toolbar.Visible = true;
            toolbar.SendToBack();
        }
        else
        {
            if (!showOverlay) toolbar.Visible = false;
            toolbar.Dock = DockStyle.None;
            toolbar.AutoSize = false;
            PositionGlobalToolbarOverlay();
            toolbar.Visible = showOverlay;
            if (showOverlay) toolbar.BringToFront();
        }
    }

    private void PositionGlobalToolbarOverlay()
    {
        if (!globalToolbarPinned && toolbar.Dock == DockStyle.None)
            toolbar.SetBounds(0, 0, ClientSize.Width, toolbar.GetPreferredSize(new Size(ClientSize.Width, 0)).Height);
    }

    private void ShowGlobalToolbarOverlay()
    {
        if (globalToolbarPinned) return;
        toolbarHideTimer.Stop();
        if (toolbar.Visible) return;
        PositionGlobalToolbarOverlay();
        toolbar.Visible = true;
        toolbar.BringToFront();
    }

    private void ScheduleToolbarHide()
    {
        if (globalToolbarPinned) return;
        toolbarHideTimer.Stop();
        toolbarHideTimer.Start();
    }

    private bool ToolbarHasInteraction() =>
        toolbar.ContainsFocus || languageSelector.DroppedDown || toolbar.OverflowButton.DropDown.Visible ||
        toolbar.Items.OfType<ToolStripDropDownItem>().Any(item => item.DropDown.Visible);

    private void HideGlobalToolbarIfIdle()
    {
        toolbarHideTimer.Stop();
        if (globalToolbarPinned || toolbarPointerInside || toolbarCaptionPointerInside || ToolbarHasInteraction()) return;
        HideGlobalToolbarOverlay();
    }

    private void HideGlobalToolbarOverlay()
    {
        if (globalToolbarPinned) return;
        toolbarHideTimer.Stop();
        toolbarRevealTimer.Stop();
        toolbarPointerInside = false;
        toolbarCaptionPointerInside = false;
        languageSelector.DroppedDown = false;
        toolbar.OverflowButton.HideDropDown();
        foreach (var item in toolbar.Items.OfType<ToolStripDropDownItem>()) item.HideDropDown();
        toolbar.Visible = false;
    }

    private void ReloadPlaces()
    {
        string? selectedPlace = (places.SelectedItem as Place?)?.Path;
        string? selectedFavorite = (favoritePlaces.SelectedItem as Place?)?.Path;
        places.Items.Clear();
        places.Items.Add(new Place(text.T("Home"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        places.Items.Add(new Place(text.T("Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        places.Items.Add(new Place(text.T("Downloads"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
        places.Items.Add(new Place(text.T("Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        if (ShellNative.Recent.Length > 0) places.Items.Add(new Place(text.T("Recent items"), ShellNative.Recent));
        places.Items.Add(new Place(text.T("This PC"), ShellNative.ThisPc));
        places.Items.Add(new Place(text.T("Recycle Bin"), ShellNative.RecycleBin));
        favoritePlaces.Items.Clear();
        foreach (string path in favorites) favoritePlaces.Items.Add(new Place(ShellViewHost.TitleFor(path, text), path, true));
        favoritePlaces.Visible = favoritesExpanded;
        for (int i = 0; i < places.Items.Count; i++)
            if (((Place)places.Items[i]).Path == selectedPlace)
            { places.SelectedIndex = i; break; }
        for (int i = 0; i < favoritePlaces.Items.Count; i++)
            if (((Place)favoritePlaces.Items[i]).Path == selectedFavorite)
            { favoritePlaces.SelectedIndex = i; break; }
        favoritesHeader.Text = $"{(favoritesExpanded ? "\u25BE" : "\u25B8")} {text.T("Favorites")}";
        favoritesHeader.AccessibleName = favoritesExpanded ? "Collapse favorites" : "Expand favorites";
    }

    internal bool FavoritesExpanded => favoritesExpanded;
    internal string ColorTheme => colorTheme;
    internal IReadOnlyList<CustomColorPair> RecentCustomColors => recentCustomColors;
    internal string ActivePaneColor => activePaneColor;
    internal bool FolderTreeVisible => showFolderTree;
    internal FolderTree FolderTree => folderTree;

    internal void SetColorTheme(string mode)
    {
        application.ChangeSettings(s => s with { ColorTheme = string.Equals(mode?.Trim(), SessionStore.CustomTheme, StringComparison.OrdinalIgnoreCase)
            && customAccent is not null && customBackground is not null ? SessionStore.CustomTheme : SessionStore.NormalizeColorTheme(mode) }, theme: true);
    }


    internal void SetCustomColors(Color accent, Color background)
    {
        application.ChangeSettings(s => s with { CustomAccent = SessionStore.FormatColor(accent),
            CustomBackground = SessionStore.FormatColor(background),
            RecentCustomColors = SessionStore.RememberCustomColors(s.RecentCustomColors, background, accent),
            ColorTheme = SessionStore.CustomTheme }, theme: true);
    }

    private void ShowCustomColors()
    {
        using var dialog = new CustomColorsDialog(text,
            customAccent is null ? Theme.Accent : SessionStore.ParseColor(customAccent),
            customBackground is null ? Theme.Canvas : SessionStore.ParseColor(customBackground), this,
            recentCustomColors.Select(pair => (SessionStore.ParseColor(pair.Background!), SessionStore.ParseColor(pair.Accent!))).ToArray(),
            pickerColors);
        var result = dialog.ShowDialog(this);
        application.ChangeState(s => s with { PickerColors = [.. dialog.PickerColors] });
        if (result == DialogResult.OK) SetCustomColors(dialog.Accent, dialog.Background);
        // Picker slots are a separate tool: cancelling the theme preview must not discard them.
        SaveSession();
    }

    private static Bitmap CreateCustomColorSwatch(Color background, Color accent, int dpi)
    {
        int width = (int)Math.Round(24 * dpi / 96d);
        int height = (int)Math.Round(16 * dpi / 96d);
        var image = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(image);
        using var backgroundFill = new SolidBrush(background);
        using var accentFill = new SolidBrush(accent);
        using var border = new Pen(Theme.Muted, Math.Max(1, dpi / 96f));
        graphics.FillRectangle(backgroundFill, 0, 0, width / 2, height);
        graphics.FillRectangle(accentFill, width / 2, 0, width - width / 2, height);
        graphics.DrawRectangle(border, 0, 0, width - 1, height - 1);
        return image;
    }

    internal void SetActivePaneColor(string color)
    {
        application.ChangeSettings(s => s with { ActivePaneColor = SessionStore.NormalizeActivePaneColor(color) });
    }

    private void RefreshThemeButton()
    {
        themeButton.Text = ThemeLabel(colorTheme);
        themeButton.ToolTipText = text.T("Choose a color theme");
    }

    // Palettes named after their design source keep that name in every language.
    private string ThemeLabel(string mode) => mode switch
    {
        SessionStore.CustomTheme => text.T("Custom colors"),
        "dark" => text.T("Dark mode"), "beige" => text.T("Beige"), "green" => text.T("Soft green"), "sky" => text.T("Sky blue"), "pastel" => text.T("Pastel blue"), "dim" => text.T("Soft dark"),
        "solarized" => "Solarized Light", "everforest" => "Everforest Light", "gruvbox" => "Gruvbox Light",
        "rosepine" => "Rosé Pine Dawn", "latte" => "Catppuccin Latte", "everforest-dark" => "Everforest Dark",
        "nord" => "Nord", "mocha" => "Catppuccin Mocha", _ => text.T("Light mode")
    };

    private readonly List<Control> sidebarSurfaces = [];
    private string? fontFamily;
    private string textSize = "normal";
    private Label? sideTitleLabel;
    private readonly ToolStripMenuItem fontMenu = new();
    private static readonly string[] FontCandidates =
    [
        "Malgun Gothic", "Pretendard", "Noto Sans KR", "NanumGothic", "NanumBarunGothic", "NanumSquare",
        "Spoqa Han Sans Neo", "IBM Plex Sans KR", "SUIT", "Arial", "Calibri", "Tahoma", "Verdana"
    ];
    private string UiFace => fontFamily ?? Theme.UiFace;
    private float UiPoints => textSize switch { "small" => 9f, "large" => 10.5f, _ => 9.5f };
    private Font HeaderFont() => fontFamily is null ? new(Theme.TitleFace, UiPoints - 0.5f) : new(fontFamily, UiPoints - 0.5f, FontStyle.Bold);
    internal string? UiFontFamily => fontFamily;
    internal string TextSize => textSize;

    private (string, float)? ListFontChoice() => fontFamily is null && textSize == "normal" ? null
        : (fontFamily ?? SystemFonts.IconTitleFont!.Name,
            SystemFonts.IconTitleFont!.SizeInPoints + textSize switch { "small" => -0.75f, "large" => 1.5f, _ => 0f });

    private static bool FontInstalled(string face)
    {
        try { using var family = new FontFamily(face); return true; }
        catch (ArgumentException) { return false; }
    }

    // A family that is not installed falls back to the standard font; the choice applies to every pane at once.
    internal void SetUiFont(string? family, string size)
    {
        application.ChangeSettings(s => s with { FontFamily = family is not null && FontInstalled(family) ? family : null,
            TextSize = size is "small" or "large" ? size : "normal" }, theme: true);
    }

    private void BuildFontMenu()
    {
        foreach (var old in fontMenu.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
        var standard = new ToolStripMenuItem(text.T("Standard ({0})", Theme.UiFace)) { Checked = fontFamily is null };
        standard.Click += (_, _) => SetUiFont(null, textSize);
        fontMenu.DropDownItems.Add(standard);
        foreach (string face in FontCandidates.Where(FontInstalled))
        {
            using var family = new FontFamily(face);
            var item = new ToolStripMenuItem(family.Name) { Checked = fontFamily == face };
            item.Click += (_, _) => SetUiFont(face, textSize);
            fontMenu.DropDownItems.Add(item);
        }
        fontMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (size, key) in new[] { ("small", "Small text"), ("normal", "Normal text"), ("large", "Large text") })
        {
            var item = new ToolStripMenuItem(text.T(key)) { Checked = textSize == size };
            item.Click += (_, _) => SetUiFont(fontFamily, size);
            fontMenu.DropDownItems.Add(item);
        }
        Theme.Apply(toolbar);
    }

    private void ApplyAppearance()
    {
        Theme.Apply(this);
        // The sidebar may have its own color: everything there that uses the window background follows it.
        if (sidebarSurfaces.Count == 0)
        {
            void Collect(Control control)
            {
                if (control.BackColor == Theme.Canvas) sidebarSurfaces.Add(control);
                foreach (Control child in control.Controls) if (child is not QuadExplorer.FolderTree) Collect(child);
            }
            Collect(sidebar);
        }
        foreach (var control in sidebarSurfaces) control.BackColor = Theme.Sidebar;
        foreach (var pane in panes) pane.RefreshTheme();
        folderTree.RefreshTheme();
        previewPane.ApplyColors();
        previewWindow?.RefreshTheme();
        if (updateNotice is { IsDisposed: false } notice)
        {
            notice.Font = Font;
            Theme.Apply(notice);
        }
        foreach (var (window, pane) in previewWindows)
        {
            Theme.Apply(window);
            pane.ApplyColors();
        }
        previewSplitter.BackColor = Theme.Border;
        RefreshThemeButton();
        for (int i = 0; i < panes.Length; i++) panes[i].SetActive(i == activePane);
    }

    // The other pane is the one used just before the active pane, or else the first other visible pane (lowest number).
    internal ExplorerPane? OtherPane()
    {
        if (previousPane >= 0 && previousPane != activePane && visiblePanes.Contains(previousPane)) return panes[previousPane];
        int other = Array.FindIndex(visiblePanes, index => index != activePane);
        return other >= 0 ? panes[visiblePanes[other]] : null;
    }

    internal void TransferSelection(bool move)
    {
        var source = Active;
        if (source.ActiveView is not { } view) return;
        // The recent list is not a folder: files copied into the Recent folder would never show up there.
        if (OtherPane() is not { ActiveView: { } destinationView } target || !Directory.Exists(destinationView.LocationPath)
            || ShellViewHost.IsRecent(destinationView.LocationPath))
        {
            source.SetLocalizedStatus("Open a folder in another pane first.");
            return;
        }
        var (paths, count) = view.SelectedPaths();
        if (count == 0 || paths.Count == 0)
        {
            source.SetLocalizedStatus(count == 0 ? "Select files or folders first." : "Items without a file path cannot be copied or moved.");
            return;
        }
        string destination = destinationView.LocationPath;
        if (paths.All(path => string.Equals(Path.GetDirectoryName(path.TrimEnd('\\')), destination.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase)))
        {
            source.SetLocalizedStatus("The other pane shows the same folder.");
            return;
        }
        int number = target.PaneNumber, items = paths.Count;
        FileOperations.Start(paths, destination, move, Handle, (done, error) =>
        {
            FileOperations.PostCompletion(this, () =>
            {
                if (error is not null) source.SetLocalizedStatus("Could not copy or move: {0}", error);
                else if (!done) source.SetLocalizedStatus("Copy or move was cancelled.");
                else source.SetLocalizedStatus(move ? "Moved {0} items to pane {1}." : "Copied {0} items to pane {1}.", items, number);
                FileOperationFinished?.Invoke(done, error);
            });
        });
    }

    // F6 and Shift+F6 walk the visible panes in order, wrapping around.
    private void CyclePane(int step)
    {
        int count = visiblePanes.Length;
        int at = Math.Max(0, Array.IndexOf(visiblePanes, activePane));
        FocusPane(visiblePanes[((at + step) % count + count) % count]);
    }

    // Alt+1 to Alt+4 go straight to a pane; one that is not open says so instead of doing nothing.
    private void FocusPane(int index)
    {
        if (!visiblePanes.Contains(index))
        {
            Active.SetLocalizedStatus("Pane {0} is not open.", index + 1);
            return;
        }
        ActivatePane(index);
        Active.FocusFiles();
    }

    // Opens the active pane's folder in the other pane (see OtherPane) and moves there.
    internal void OpenFolderInOtherPane()
    {
        var source = Active;
        string path = source.CurrentPath;
        if (path.Length == 0 || source.ActiveView is null) { source.SetLocalizedStatus("Open a folder first."); return; }
        if (OtherPane() is not { } target) { source.SetLocalizedStatus("There is no other pane. Choose 2, 3 or 4 panes first."); return; }
        int number = target.PaneNumber;
        switch (target.ShowFolder(path))
        {
            case ExplorerPane.ShowResult.Full: source.SetLocalizedStatus("Pane {0} already has 32 tabs. Close a tab first.", number); break;
            case ExplorerPane.ShowResult.AlreadyOpen: source.SetLocalizedStatus("Pane {0} already shows this folder.", number); break;
            default: source.SetLocalizedStatus("Opened this folder in pane {0}.", number); break;
        }
    }

    internal IReadOnlyList<Workspace> Workspaces => workspaces;

    internal void SaveWorkspace(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var snapshot = SessionStore.NormalizeWorkspace(new Workspace
        {
            Name = name, LayoutTree = layoutTree, Panes = panes.Select(pane => pane.CaptureState()).ToList(), ActivePane = activePane
        });
        int existing = workspaces.FindIndex(workspace => string.Equals(workspace.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) workspaces[existing] = snapshot;
        else if (workspaces.Count >= 20) { Active.SetLocalizedStatus("Up to 20 workspaces can be saved."); return; }
        else workspaces.Add(snapshot);
        Active.SetLocalizedStatus("Workspace saved: {0}", name);
        ScheduleSave();
    }

    internal void LoadWorkspace(string name)
    {
        var workspace = workspaces.Find(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (workspace is null) return;
        for (int i = 0; i < panes.Length; i++) panes[i].ReplaceTabs(workspace.Panes[i]);
        activePane = workspace.ActivePane;
        RenderLayout(workspace.LayoutTree!);
        Active.SetLocalizedStatus("Workspace opened: {0}", workspace.Name);
        Active.FocusFiles();
    }

    internal void DeleteWorkspace(string name)
    {
        if (workspaces.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0) ScheduleSave();
    }

    private void BuildWorkspaceMenu()
    {
        foreach (var old in workspaceButton.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
        workspaceButton.DropDownItems.Add(text.T("Save current workspace..."), null, (_, _) =>
        {
            if (PromptWorkspaceName() is { } name) SaveWorkspace(name);
        });
        if (workspaces.Count > 0)
        {
            workspaceButton.DropDownItems.Add(new ToolStripSeparator());
            foreach (var workspace in workspaces)
            {
                string name = workspace.Name;
                workspaceButton.DropDownItems.Add(name, null, (_, _) => LoadWorkspace(name));
            }
            var delete = new ToolStripMenuItem(text.T("Delete workspace"));
            foreach (var workspace in workspaces)
            {
                string name = workspace.Name;
                delete.DropDownItems.Add(name, null, (_, _) => DeleteWorkspace(name));
            }
            workspaceButton.DropDownItems.Add(new ToolStripSeparator());
            workspaceButton.DropDownItems.Add(delete);
        }
        Theme.Apply(toolbar);
    }

    private string? PromptWorkspaceName()
    {
        using var dialog = new Form
        {
            Text = text.T("Save current workspace..."), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Font = Font
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var label = new Label { Text = text.T("Workspace name"), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var input = new TextBox { Width = 280, Text = text.T("Workspace {0}", workspaces.Count + 1) };
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
        return dialog.ShowDialog(this) == DialogResult.OK && input.Text.Trim().Length > 0 ? input.Text.Trim() : null;
    }

    internal bool PreviewVisible => showPreview;
    internal PreviewPane Preview => previewPane;
    internal PreviewWindow? FloatingPreview => previewWindow;

    internal void SetPreviewVisible(bool visible)
    {
        showPreview = visible;
        previewPane.Visible = visible;
        previewSplitter.Visible = visible && previewWindow is null;
        if (previewWindow is not null)
        {
            if (visible) previewWindow.Show(this);
            else previewWindow.Hide();
        }
        previewButton.Checked = visible;
        previewTimer.Stop();
        if (!visible) previewPane.SetPinned(false);
        if (visible) UpdatePreview();
        else previewPane.Unload();
        ScheduleSave();
    }

    internal void SetPreviewFloating(bool floating)
    {
        if (floating == (previewWindow is not null)) return;
        previewPane.SetFloating(floating);
        if (floating)
        {
            previewWidth = previewPane.Width;
            previewSplitter.Visible = false;
            var window = new PreviewWindow(text)
            {
                Icon = Icon, Size = new Size(Math.Max(320, previewWidth), Math.Max(400, previewPane.Height)),
                Location = previewPane.PointToScreen(Point.Empty)
            };
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            window.Size = new Size(Math.Min(window.Width, screen.Width), Math.Min(window.Height, screen.Height));
            window.Location = new Point(
                Math.Clamp(window.Left, screen.Left, Math.Max(screen.Left, screen.Right - window.Width)),
                Math.Clamp(window.Top, screen.Top, Math.Max(screen.Top, screen.Bottom - window.Height)));
            previewWindow = window;
            window.DockRequested += (_, _) => SetPreviewFloating(false);
            window.MoveFinished += TryDockPreview;
            window.FormClosing += (_, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                SetPreviewVisible(false);
            };
            window.AddPreview(previewPane);
            window.RefreshTheme();
            previewPane.ApplyColors();
            if (showPreview) window.Show(this);
        }
        else
        {
            var window = previewWindow!;
            previewWindow = null;
            var workspace = paneHost.Parent!;
            workspace.Controls.Add(previewPane);
            previewPane.Dock = DockStyle.Right;
            previewPane.Width = previewWidth;
            workspace.Controls.SetChildIndex(previewPane, workspace.Controls.GetChildIndex(previewSplitter) + 1);
            previewSplitter.Visible = showPreview;
            previewPane.Visible = showPreview;
            window.Dispose();
            if (Visible && showPreview) Active.FocusFiles();
        }
        ScheduleSave();
    }

    internal void TryDockPreview(Point point)
    {
        if (previewWindow is null || !Visible || WindowState == FormWindowState.Minimized) return;
        var bounds = paneHost.Parent!.RectangleToScreen(paneHost.Parent.ClientRectangle);
        var dropZone = new Rectangle(bounds.Right - Math.Min(previewWidth, bounds.Width / 3),
            bounds.Top, Math.Min(previewWidth, bounds.Width / 3), bounds.Height);
        if (dropZone.Contains(point)) SetPreviewFloating(false);
    }

    internal IReadOnlyList<PreviewPane> PreviewWindows => previewWindows.Select(entry => entry.Pane).ToList();

    // Each extra window previews one file and stays put while other files are selected.
    internal void OpenPreviewWindow(string path)
    {
        if (previewWindows.Count >= 8) { Active.SetLocalizedStatus("Up to 8 preview windows can be open."); return; }
        var pane = new PreviewPane(text) { Dock = DockStyle.Fill };
        pane.SetStandalone();
        var offset = 40 * (previewWindows.Count + 1);
        var window = new Form
        {
            Text = Path.GetFileName(path) + " - " + text.T("Preview"), Icon = Icon, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, AutoScaleMode = AutoScaleMode.Dpi, Font = Font,
            Size = new Size(520, 700), MinimumSize = new Size(260, 260)
        };
        // Open beside the main window when there is room, so the new window never hides the preview it came from.
        Rectangle screen = Screen.FromControl(this).WorkingArea;
        window.Height = Math.Min(Math.Max(window.Height, Height), screen.Height);
        int left = Right + 8 + offset - 40 <= screen.Right - window.Width ? Right + 8 + offset - 40
            : Left - window.Width - 8 - offset + 40 >= screen.Left ? Left - window.Width - 8 - offset + 40
            : Left + offset;
        window.Location = new Point(Math.Clamp(left, screen.Left, Math.Max(screen.Left, screen.Right - window.Width)),
            Math.Clamp(Top + offset - 40, screen.Top, Math.Max(screen.Top, screen.Bottom - window.Height)));
        window.Controls.Add(pane);
        window.FormClosed += (_, _) =>
        {
            previewWindows.RemoveAll(entry => entry.Window == window);
            pane.Dispose();
        };
        previewWindows.Add((window, pane));
        Theme.Apply(window);
        pane.ApplyColors();
        window.Show(this);
        pane.ShowFile(path);
    }

    private void SchedulePreview()
    {
        if (!showPreview) return;
        previewTimer.Stop();
        previewTimer.Start();
    }

    // Follows the active pane's selection: one file is previewed; folders, several items or nothing show a hint.
    internal void UpdatePreview()
    {
        if (!showPreview || IsDisposed || previewPane.Pinned) return;
        var (path, count) = Active.ActiveView?.SelectionInfo() ?? (null, 0);
        if (showDetails) _ = previewPane.ShowDetailsAsync(count == 1 ? path : null);
        if (count == 0) previewPane.ShowMessage("Select a file to preview it.");
        else if (count > 1) previewPane.ShowMessage("{0} items selected", count);
        else if (path is null || Directory.Exists(path)) previewPane.ShowMessage("Folders are not previewed.");
        else previewPane.ShowFile(path);
    }

    private bool showDetails;
    internal bool DetailsPaneVisible => showDetails && showPreview;

    // The details pane lives under the preview, so showing it also shows the preview area.
    internal void SetDetailsPaneVisible(bool visible)
    {
        showDetails = visible;
        previewPane.SetDetailsVisible(visible);
        if (visible && !showPreview) SetPreviewVisible(true);
        else if (visible) UpdatePreview();
        ScheduleSave();
    }

    internal BulkRenameDialog? OpenBulkRename(ExplorerPane pane, bool modal = true)
    {
        if (pane.ActiveView is not { } view) return null;
        var (paths, _) = view.SelectedPaths();
        if (paths.Count < 2) { view.StartRename(); return null; }
        var dialog = new BulkRenameDialog(paths, text);
        dialog.Completed += (done, error) =>
        {
            if (error is not null) pane.SetStatus(error);
            else if (done) pane.SetLocalizedStatus("Renamed {0} items.", paths.Count);
        };
        if (modal) { using (dialog) dialog.ShowDialog(this); return null; }
        return dialog;
    }

    internal CompareDialog? OpenCompare(ExplorerPane pane)
    {
        var other = OtherPane();
        if (other is null || pane.ActiveView is null || other.ActiveView is null || pane.CurrentPath.Length == 0 || other.CurrentPath.Length == 0
            || !Directory.Exists(pane.CurrentPath) || !Directory.Exists(other.CurrentPath)
            || ShellViewHost.IsRecent(pane.CurrentPath) || ShellViewHost.IsRecent(other.CurrentPath))
        {
            pane.SetLocalizedStatus("Open another pane to compare.");
            return null;
        }
        var dialog = new CompareDialog(pane.CurrentPath, pane.PaneNumber, other.CurrentPath, other.PaneNumber, text,
            paths => pane.ActiveView?.SelectPaths(paths), paths => other.ActiveView?.SelectPaths(paths));
        dialog.Show(this);
        return dialog;
    }

    internal void SetFolderTreeVisible(bool visible)
    {
        showFolderTree = visible;
        folderTree.Visible = treeSplitter.Visible = visible;
        treeButton.Checked = visible;
        UpdateTreeHeader();
        if (visible && !sidebar.Visible) SetSidebarVisible(true);
        UpdateSidebarContent();
        if (visible && ready) SyncTree();
        ScheduleSave();
    }

    internal bool GlobalHotkeyEnabled => globalHotkey;

    internal void SetGlobalHotkey(bool enabled)
    {
        application.SetGlobalHotkey(enabled);
    }

    internal bool TrayIconVisible => application.TrayIconVisible;
    internal string VersionText => versionLabel.Text;
    internal bool UpdateLinkShown => versionLabel.LinkArea.Length > 0;

    internal bool AutoUpdateEnabled => autoUpdate;

    internal void SetAutoUpdate(bool enabled)
    {
        application.ChangeSettings(s => s with { AutoUpdate = enabled });
    }

    private bool CanInstallUpdate(ReleaseInfo update) => autoUpdate && update.PackageUrl is not null && update.PackageSha256 is not null;

    private async Task OpenAvailableUpdateAsync()
    {
        await application.OpenAvailableUpdateAsync(this);
    }


    internal void SetCheckUpdates(bool enabled)
    {
        application.SetCheckUpdates(enabled);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal DateTime LastUpdateCheck { get => application.LastUpdateCheck; set => application.LastUpdateCheck = value; }
    internal TimeSpan UpdateCheckInterval => application.UpdateCheckInterval;
    internal Task CheckForStaleUpdateAsync() => application.CheckForStaleUpdateAsync();

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        application.Activated(this);
        if (ready) { ShowPendingUpdateNotice(); _ = CheckForStaleUpdateAsync(); }
    }

    // Offline, blocked or rate-limited networks simply leave the version label unchanged.
    internal Task CheckForUpdateAsync() => application.CheckForUpdateAsync();

    private void UpdateVersionText()
    {
        string current = "v" + UpdateChecker.Current.ToString(3);
        if (availableUpdate is null)
        {
            updateNotice?.Close();
            versionLabel.Text = current;
            versionLabel.LinkArea = new LinkArea(0, 0);
            return;
        }
        string notice = updating ? text.T("Updating to {0}…", availableUpdate.Tag)
            : CanInstallUpdate(availableUpdate) ? text.T("New version {0} available · Update", availableUpdate.Tag)
            : text.T("New version {0} available · Download", availableUpdate.Tag);
        versionLabel.Text = current + "   " + notice;
        versionLabel.LinkArea = new LinkArea(current.Length + 3, notice.Length);
        versionLabel.LinkColor = versionLabel.ActiveLinkColor = versionLabel.VisitedLinkColor = Theme.Accent;
        updateNotice?.RefreshNotice(availableUpdate, CanInstallUpdate(availableUpdate));
    }

    private void ShowPendingUpdateNotice()
    {
        if (!ready || !Visible || WindowState == FormWindowState.Minimized || updating ||
            availableUpdate is not { } update || application.NotifiedUpdateVersion == update.Version ||
            !UpdateNotice.OwnerVisibleAtNotice(this)) return;
        updateNotice?.Close();
        application.NotifiedUpdateVersion = update.Version;
        var notice = new UpdateNotice(text, Font, update, CanInstallUpdate(update));
        updateNotice = notice;
        notice.UpdateRequested += async (_, _) => await OpenAvailableUpdateAsync();
        notice.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(updateNotice, notice)) updateNotice = null;
            if ((notice.Deferred || !Visible || WindowState == FormWindowState.Minimized) && application.NotifiedUpdateVersion == update.Version)
                application.NotifiedUpdateVersion = null;
        };
        notice.Present(this);
    }
    internal bool TrayNoticeShown => application.State.TrayNoticeShown;

    internal void SetCloseToTray(bool enabled)
    {
        application.SetCloseToTray(enabled);
    }

    internal void ExitApplication()
    {
        application.ExitApplication();
    }

    private void SetWinEShortcut(bool enabled)
    {
        application.SetWinEShortcut(enabled, this);
    }

    internal void SetStartWithWindows(bool enabled)
    {
        application.SetStartWithWindows(enabled, this);
    }

    // The application toggles its peer windows together, rather than changing the next hotkey target.
    internal void ToggleFromHotkey() => application.ToggleFromHotkey();

    internal bool IsForegroundBrowser => Visible && WindowState != FormWindowState.Minimized && GetForegroundWindow() == Handle;

    // Hiding keeps the app (and the hotkey) running in the notification area; without it, minimize instead.
    internal void DismissFromHotkey()
    {
        if (closeToTray)
        {
            Hide();
            SaveSession();
        }
        else WindowState = FormWindowState.Minimized;
    }

    internal void Reveal()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) ShowWindow(Handle, 9); // SW_RESTORE keeps a maximized window maximized
    }

    internal void Summon()
    {
        Reveal();
        Activate();
        SetForegroundWindow(Handle);
        Active.FocusFiles();
    }


    protected override void WndProc(ref Message m)
    {
        if (ready && !globalToolbarPinned && m.Msg == 0x00A0) // WM_NCMOUSEMOVE
        {
            if (m.WParam == 2) // HTCAPTION: the draggable title bar, excluding window buttons
            {
                if (!toolbarCaptionPointerInside)
                {
                    toolbarCaptionPointerInside = true;
                    if (!toolbar.Visible) toolbarRevealTimer.Start();
                }
                toolbarHideTimer.Stop();
                var track = new MouseTrack { Size = Marshal.SizeOf<MouseTrack>(), Flags = 0x12, Window = m.HWnd };
                TrackMouseEvent(ref track); // TME_LEAVE | TME_NONCLIENT
            }
            else if (toolbarCaptionPointerInside)
            {
                toolbarCaptionPointerInside = false;
                toolbarRevealTimer.Stop();
                ScheduleToolbarHide();
            }
        }
        else if (m.Msg == 0x02A2 && toolbarCaptionPointerInside) // WM_NCMOUSELEAVE
        {
            toolbarCaptionPointerInside = false;
            toolbarRevealTimer.Stop();
            ScheduleToolbarHide();
        }
        base.WndProc(ref m);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseTrack
    {
        public int Size, Flags;
        public nint Window;
        public int HoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref MouseTrack track);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    private void UpdateTreeHeader()
    {
        treeHeader.Text = $"{(showFolderTree ? "\u25BE" : "\u25B8")} {text.T("Folder tree")}";
        treeHeader.AccessibleName = showFolderTree ? "Collapse folder tree" : "Expand folder tree";
    }

    private void UpdateSidebarContent()
    {
        double scale = DeviceDpi / 96d;
        double height = sidebar.ClientSize.Height / scale;
        // Rows follow the screen scale; quick access is as tall as its items, within a third of the sidebar.
        int row = (int)Math.Round(28 * scale);
        if (places.ItemHeight != row) places.ItemHeight = row;
        if (favoritePlaces.ItemHeight != row) favoritePlaces.ItemHeight = row;
        places.Height = Math.Min(Math.Max(1, places.Items.Count) * row, (int)Math.Round(Math.Clamp(height / 3, 108, 256) * scale));
        double tree = folderTreeHeight > 0 ? folderTreeHeight : Math.Clamp(height / 3, 120, 280);
        double room = Math.Max(80, height - places.Height / scale - 200);
        treePanel.Height = treeHeader.Height + (showFolderTree ? (int)Math.Round(Math.Clamp(tree, 80, room) * scale) : 0);
        treeSplitter.MinSize = treeHeader.Height + (int)Math.Round(80 * scale);
        treeSplitter.MinExtra = (int)Math.Round(60 * scale);
    }

    private async void SyncTree()
    {
        if (!IsDisposed && showFolderTree && folderTree.Visible)
            await folderTree.FollowPathAsync(Active.CurrentPath);
    }

    private void OpenTreeFolder(string path, FolderTreeTarget target)
    {
        if (!Directory.Exists(path)) { Active.SetLocalizedStatus("No longer available: {0}", path); return; }
        switch (target)
        {
            case FolderTreeTarget.CurrentPane: Active.Navigate(path); break;
            case FolderTreeTarget.NewTab: Active.AddRelatedTab(path); break;
            case FolderTreeTarget.NewPane:
                if (visiblePanes.Length == 4)
                { Active.SetLocalizedStatus("Maximum 4 panes. Close a pane first."); return; }
                int destination = Enumerable.Range(0, 4).First(id => !visiblePanes.Contains(id));
                if (panes[destination].TabCount == 32)
                { Active.SetLocalizedStatus("Destination has 32 tabs.\nClose a tab first."); return; }
                RenderLayout(layoutTree.Split(activePane, destination, SplitEdge.Right));
                panes[destination].AddTab(path);
                ActivatePane(destination);
                break;
            case FolderTreeTarget.ExplorerWindow:
                try
                {
                    var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                    start.ArgumentList.Add(path);
                    Process.Start(start);
                }
                catch (Win32Exception error) { Active.SetStatus(error.Message); }
                break;
        }
    }
    internal int SidebarWidth => sidebarWidth;
    internal void SetSidebarWidth(int width)
    {
        sidebarWidth = Math.Clamp(width, 140, 480);
        sidebar.Width = (int)Math.Round(sidebarWidth * DeviceDpi / 96d);
        UpdateMinimumSize();
        ScheduleSave();
    }

    internal void SetSidebarVisible(bool visible)
    {
        showSidebar = visible;
        sidebar.Visible = visible;
        sidebarSplitter.Visible = visible;
        sidebarButton.Checked = visible;
        UpdateMinimumSize();
        ScheduleSave();
    }

    internal IReadOnlyList<string> FavoritePaths => favorites;
    internal IReadOnlyList<string> DeletedFavoritePaths => deletedFavorites;
    private void ImportExplorerFavorites()
    {
        var result = ExplorerFavorites.Read();
        int added = 0;
        foreach (string path in result.Paths)
        {
            if (favorites.Count >= 100) break;
            if (favorites.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            favorites.Add(path);
            deletedFavorites.RemoveAll(p => StringComparer.OrdinalIgnoreCase.Equals(p, path));
            added++;
        }
        explorerFavoritesImported = result.Warning is null || result.Paths.Count > 0;
        application.RefreshFavorites();
        Active.SetLocalizedStatus(() => text.T("Imported {0} favorites from File Explorer.", added)
            + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings.Select(w => text.T(w))) : ""));
        ScheduleSave();
    }

    private void RemoveFavorite(string path)
    {
        if (!favorites.Remove(path)) return;
        deletedFavorites.RemoveAll(p => StringComparer.OrdinalIgnoreCase.Equals(p, path));
        deletedFavorites.Insert(0, path);
        if (deletedFavorites.Count > 100) deletedFavorites.RemoveAt(100);
        application.RefreshFavorites();
        ScheduleSave();
    }

    internal void ClearFavorites()
    {
        foreach (string path in favorites.ToArray()) RemoveFavorite(path);
    }

    internal void RestoreFavorite(string path) => AddFavorite(path);

    private void ShowRestoreFavorites()
    {
        using var dialog = new Form
        {
            Text = text.T("Restore favorites"), StartPosition = FormStartPosition.CenterParent,
            Size = new Size(650, 430), MinimumSize = new Size(450, 300),
            Font = Font, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false
        };
        var list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true };
        foreach (string path in deletedFavorites)
            list.Items.Add(new Place($"{ShellViewHost.TitleFor(path, text)} — {path}", path));
        foreach (Place place in places.Items)
            if (!favorites.Contains(place.Path, StringComparer.OrdinalIgnoreCase)
                && !deletedFavorites.Contains(place.Path, StringComparer.OrdinalIgnoreCase))
                list.Items.Add(place);
        var restore = Theme.Button(text.T("Restore selected"), "Restore selected favorites", () =>
        {
            foreach (Place item in list.SelectedItems.Cast<Place>().ToArray()) RestoreFavorite(item.Path);
            dialog.Close();
        });
        restore.Dock = DockStyle.Bottom;
        restore.AutoSize = true;
        var label = new Label
        {
            Text = text.T("Removed favorites and standard locations (including Recycle Bin). Select items to restore."),
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(600, 0)
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(restore);
        dialog.Controls.Add(label);
        Theme.Apply(dialog);
        dialog.ShowDialog(this);
    }

    internal void ToggleFavorites()
    {
        favoritesExpanded = !favoritesExpanded;
        ReloadPlaces();
        ScheduleSave();
    }

    private void DrawPlace(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = e.State.HasFlag(DrawItemState.Selected);
        using var background = new SolidBrush(Theme.Sidebar);
        e.Graphics.FillRectangle(background, e.Bounds);
        // The selected row is a soft rounded wash, inset from the list edges.
        if (selected) Theme.FillRounded(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), Theme.Selected, DeviceDpi);
        else if (sender is ListBox owner && hoverRows.GetValueOrDefault(owner, -1) == e.Index)
            Theme.FillRounded(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), Theme.Hover, DeviceDpi);
        var place = (Place)((ListBox)sender!).Items[e.Index];
        int iconSize = (int)Math.Round(16 * DeviceDpi / 96d);
        int gap = (int)Math.Round(10 * DeviceDpi / 96d);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(placeIcons.Get(place.Path),
            new Rectangle(e.Bounds.Left + gap, e.Bounds.Top + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize));
        var rect = new Rectangle(e.Bounds.Left + iconSize + gap * 2, e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - iconSize - gap * 3), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, place.Label, Font, rect,
            selected ? Theme.Accent : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void OpenPlace()
    {
        if (places.SelectedItem is Place place) Active.Navigate(place.Path);
    }

    private void AddFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (favorites.Contains(path, StringComparer.OrdinalIgnoreCase)) { Active.SetLocalizedStatus("Already in favorites."); return; }
        if (favorites.Count >= 100) { Active.SetLocalizedStatus("Maximum 100 favorites."); return; }
        favorites.Add(path);
        deletedFavorites.RemoveAll(p => StringComparer.OrdinalIgnoreCase.Equals(p, path));
        application.RefreshFavorites();
        Active.SetLocalizedStatus("Added to favorites.");
        ScheduleSave();
    }

    private void MoveTab(ExplorerPane source, string path, int number)
    {
        if (source.ActiveTab is not { } tab) return;
        if (!visiblePanes.Contains(number - 1)) ApplyLayout(Math.Max(visiblePanes.Length, number));
        DockTab(source, tab, number - 1, null);
    }

    internal void ApplyLayout(int count, bool splitFirst = true, bool vertical = true)
    {
        if (count == 1)
        {
            int keep = Active.TabCount > 0 ? activePane
                : layoutTree.GetPanes().FirstOrDefault(i => panes[i].TabCount > 0, activePane);
            RenderLayout(PaneLayout.Leaf(keep));
            return;
        }
        RenderLayout(PaneLayout.Preset(count, splitFirst, vertical));
    }

    private void RenderLayout(PaneLayout tree, bool preserveClosedPanes = false)
    {
        // An explicit new arrangement supersedes snapshots of the old arrangement.
        if (!preserveClosedPanes)
        {
            closedItems.RemoveAll(item => item.Layout is not null);
            InvalidateRedo();
        }
        rebuilding = true;
        paneHost.SuspendLayout();
        try
        {
            layoutTree = tree;
            visiblePanes = tree.GetPanes().Order().ToArray();
            foreach (var pane in panes)
            {
                pane.SetCanClose(visiblePanes.Length > 1);
                pane.Visible = false;
                pane.Parent?.Controls.Remove(pane);
            }
            foreach (Control old in paneHost.Controls.Cast<Control>().ToArray()) old.Dispose();
            splits.Clear();
            UpdateMinimumSize();
            paneHost.Controls.Add(BuildNode(tree, ""));
            if (!visiblePanes.Contains(activePane)) activePane = visiblePanes[0];
            for (int i = 0; i < layoutButtons.Length; i++)
                layoutButtons[i].Checked = visiblePanes.Length == i + 1;
        }
        finally
        {
            paneHost.ResumeLayout(true);
            ApplySplitRatios();
            rebuilding = false;
        }
        ActivatePane(activePane);
        RefreshCloseHistory();
        ScheduleSave();

        Control BuildNode(PaneLayout node, string path)
        {
            if (node.IsLeaf)
            {
                panes[node.Pane].Visible = true;
                return panes[node.Pane];
            }
            Size minimum = MinimumContentSize(node);
            Size first = MinimumContentSize(node.First!);
            Size second = MinimumContentSize(node.Second!);
            var split = new SplitContainer
            {
                Size = new Size(Math.Max(700, minimum.Width + 20), Math.Max(500, minimum.Height + 20)),
                Dock = DockStyle.Fill,
                Orientation = node.Vertical ? Orientation.Vertical : Orientation.Horizontal,
                SplitterWidth = 6, BackColor = Theme.Canvas,
                Panel1MinSize = node.Vertical ? first.Width : first.Height,
                Panel2MinSize = node.Vertical ? second.Width : second.Height
            };
            splits.Add((split, path));
            split.Panel1.Controls.Add(BuildNode(node.First!, path + "0"));
            split.Panel2.Controls.Add(BuildNode(node.Second!, path + "1"));
            split.SplitterMoved += (_, _) =>
            {
                if (rebuilding || !ready) return;
                layoutTree = layoutTree.SetRatio(path, SplitRatio(split));
                ScheduleSave();
            };
            split.DoubleClick += (_, _) => EqualizeSplits();
            return split;
        }
    }

    private Size MinimumContentSize(PaneLayout node)
    {
        if (node.IsLeaf) return new Size((int)Math.Ceiling(260 * DeviceDpi / 96d), (int)Math.Ceiling(180 * DeviceDpi / 96d));
        Size first = MinimumContentSize(node.First!);
        Size second = MinimumContentSize(node.Second!);
        return node.Vertical
            ? new Size(first.Width + second.Width + 6, Math.Max(first.Height, second.Height))
            : new Size(Math.Max(first.Width, second.Width), first.Height + second.Height + 6);
    }

    private void UpdateMinimumSize()
    {
        Size minimum = MinimumContentSize(layoutTree);
        Size scaledMinimum = BrowserWindowGeometry.MinimumSize(DeviceDpi);
        MinimumSize = new Size(Math.Max(scaledMinimum.Width, minimum.Width + (showSidebar ? sidebar.Width : 0) + 8 + Width - ClientSize.Width),
            Math.Max(scaledMinimum.Height, minimum.Height + (globalToolbarPinned ? toolbar.Height : 0) + footer.Height + 12 + Height - ClientSize.Height));
    }

    private void ApplySplitRatios()
    {
        foreach (var (split, path) in splits)
        {
            var node = layoutTree;
            foreach (char step in path) node = step == '0' ? node.First! : node.Second!;
            SetSplitRatio(split, node.Ratio);
        }
    }

    private void ActivatePane(int index)
    {
        if (!visiblePanes.Contains(index)) return;
        if (index != activePane) previousPane = activePane;
        activePane = index;
        for (int i = 0; i < panes.Length; i++) panes[i].SetActive(i == index);
        if (ready) SyncTree();
        SchedulePreview();
        ScheduleSave();
    }

    private void EqualizeSplits()
    {
        layoutTree = layoutTree.Equalize();
        rebuilding = true;
        try { ApplySplitRatios(); }
        finally { rebuilding = false; }
        ScheduleSave();
    }

    private void StartTabDrag(ExplorerPane source, ExplorerPane.FolderTab tab)
    {
        if (tabDragging || source.IndexOfTab(tab) < 0) return;
        source.SelectTab(source.IndexOfTab(tab), focus: false);
        StartStripDrag(source, tab, null);
    }

    private void StartGroupDrag(ExplorerPane source, TabGroup group)
    {
        if (tabDragging || !source.Groups.Contains(group)) return;
        StartStripDrag(source, null, group);
    }

    private void StartStripDrag(ExplorerPane source, ExplorerPane.FolderTab? tab, TabGroup? group)
    {
        ActivatePane(source.PaneNumber - 1);
        Rectangle bounds = paneHost.RectangleToScreen(paneHost.ClientRectangle);
        Rectangle[] regions = visiblePanes.Select(i => panes[i].RectangleToScreen(panes[i].ClientRectangle)).ToArray();
        using var overlay = new TabDockOverlay(bounds, regions,
            point => group is null ? FindDropPlan(point, source, tab!) : FindGroupDropPlan(point, source, group), text);
        var data = new DataObject();
        data.SetData(TabDockOverlay.DragFormat, false, group is null ? "tab" : "group");
        tabDragging = true;
        DragDropEffects effect;
        try
        {
            overlay.Show(this);
            effect = DoDragDrop(data, DragDropEffects.Move);
        }
        finally
        {
            overlay.Hide();
            tabDragging = false;
        }
        if (effect != DragDropEffects.Move || overlay.Result is not { } drop || IsDisposed) return;
        if (drop.InsertionIndex is { } insertion)
        {
            var receiving = panes[drop.TargetPane];
            if (ReferenceEquals(receiving, source))
            {
                if (group is not null) source.ReorderGroup(group, insertion);
                else if (tab is not null) source.ReorderTab(tab, insertion, drop.JoinGroup);
            }
            else if (group is not null) DockGroup(source, group, drop.TargetPane, null, insertion);
            else if (tab is not null) DockTab(source, tab, drop.TargetPane, null, insertion, drop.JoinGroup);
        }
        else if (group is not null) DockGroup(source, group, drop.TargetPane, drop.Edge);
        else if (tab is not null) DockTab(source, tab, drop.TargetPane, drop.Edge);
    }

    protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
        base.OnQueryContinueDrag(e);
        if (tabDragging && e.EscapePressed) e.Action = DragAction.Cancel;
    }

    private TabDropPlan? FindDropPlan(Point screen, ExplorerPane source, ExplorerPane.FolderTab tab) =>
        FindStripOrDockPlan(screen, source, tab, null);

    private TabDropPlan? FindGroupDropPlan(Point screen, ExplorerPane source, TabGroup group) =>
        FindStripOrDockPlan(screen, source, null, group);

    private TabDropPlan? FindStripOrDockPlan(Point screen, ExplorerPane source, ExplorerPane.FolderTab? tab, TabGroup? group)
    {
        foreach (int target in visiblePanes)
        {
            if (panes[target].FindStripDropPlan(screen, tab, group) is not { } strip) continue;
            if (ReferenceEquals(panes[target], source)) return strip;
            string reason;
            bool allowed = group is not null
                ? CanDockGroup(source, group, target, null, out reason)
                : CanDockTab(source, tab!, target, null, out reason);
            return allowed ? strip : strip with { Allowed = false, Label = text.T(reason) };
        }
        foreach (int target in visiblePanes)
        {
            Rectangle full = panes[target].RectangleToScreen(panes[target].ClientRectangle);
            if (!full.Contains(screen)) continue;
            SplitEdge? edge = null;
            Rectangle files = panes[target].FileAreaOnScreen;
            if (files.Contains(screen))
            {
                double left = (double)(screen.X - files.Left) / files.Width;
                double right = (double)(files.Right - screen.X) / files.Width;
                double top = (double)(screen.Y - files.Top) / files.Height;
                double bottom = (double)(files.Bottom - screen.Y) / files.Height;
                var nearest = new[] { (left, SplitEdge.Left), (right, SplitEdge.Right), (top, SplitEdge.Top), (bottom, SplitEdge.Bottom) }
                    .MinBy(pair => pair.Item1);
                if (nearest.Item1 <= .2) edge = nearest.Item2;
            }
            Rectangle preview = full;
            if (edge is SplitEdge.Left or SplitEdge.Right)
            {
                preview.Width /= 2;
                if (edge == SplitEdge.Right) preview.X += full.Width - preview.Width;
            }
            else if (edge is SplitEdge.Top or SplitEdge.Bottom)
            {
                preview.Height /= 2;
                if (edge == SplitEdge.Bottom) preview.Y += full.Height - preview.Height;
            }
            string reason;
            bool allowed = group is not null
                ? CanDockGroup(source, group, target, edge, out reason)
                : CanDockTab(source, tab!, target, edge, out reason);
            string label = allowed ? edge is { } side ? text.T($"Split {side.ToString().ToLowerInvariant()}")
                : text.T(group is null ? "Move tab to pane {0}" : "Move group to pane {0}", target + 1) : text.T(reason);
            return new TabDropPlan(target, edge, preview, label, allowed);
        }
        return null;
    }

    private bool CanDockTab(ExplorerPane source, ExplorerPane.FolderTab tab, int target, SplitEdge? edge, out string reason)
    {
        reason = "";
        if (source.IndexOfTab(tab) < 0 || !visiblePanes.Contains(source.PaneNumber - 1) || !visiblePanes.Contains(target))
        { reason = "This tab is no longer available."; return false; }
        int sourceId = source.PaneNumber - 1;
        if (edge is null && target == sourceId)
        { reason = "Drop near a file-list edge to split.\nEsc cancels."; return false; }
        bool relocatePane = edge is not null && target != sourceId && source.TabCount == 1;
        if (edge is not null && !relocatePane && visiblePanes.Length == 4)
        { reason = "Maximum 4 panes.\nDrop in a pane's center to move the tab instead."; return false; }
        int destination = edge is null ? target : relocatePane ? sourceId : Enumerable.Range(0, 4).First(i => !visiblePanes.Contains(i));
        if (!relocatePane && panes[destination].TabCount >= 32)
        { reason = "Destination has 32 tabs.\nClose a tab first."; return false; }
        return true;
    }

    internal bool DockTab(ExplorerPane source, ExplorerPane.FolderTab tab, int target, SplitEdge? edge,
        int? insertionIndex = null, TabGroup? joinGroup = null)
    {
        if (!CanDockTab(source, tab, target, edge, out string reason))
        { source.SetLocalizedStatus(() => text.T(reason).Replace("\n", " ")); return false; }
        InvalidateRedo();
        int sourceId = source.PaneNumber - 1;
        if (edge is { } side && sourceId != target && source.TabCount == 1)
        {
            RenderLayout(RemovePane(layoutTree, sourceId)!.Split(target, sourceId, side));
            ActivatePane(sourceId);
            source.FocusFiles();
            return true;
        }

        string? path = tab.Host?.LocationPath;
        int destination = target;
        if (edge is { } splitEdge)
        {
            destination = Enumerable.Range(0, 4).First(i => !visiblePanes.Contains(i));
            RenderLayout(layoutTree.Split(target, destination, splitEdge));
        }
        var receiving = panes[destination];
        if (insertionIndex is { } insertion)
        {
            if (!receiving.InsertDroppedTab(tab, insertion, joinGroup)) return false;
        }
        else if (path is null) receiving.AddBlankTab();
        else receiving.AddTab(path, countVisit: false);
        bool emptyPane = source.TabCount == 1;
        source.CloseTab(source.IndexOfTab(tab), remember: false);
        if (emptyPane && edge is null) RenderLayout(RemovePane(layoutTree, sourceId)!);
        ActivatePane(destination);
        receiving.FocusFiles();
        ScheduleSave();
        return true;
    }

    private bool CanDockGroup(ExplorerPane source, TabGroup group, int target, SplitEdge? edge, out string reason)
    {
        reason = "";
        int sourceId = Array.IndexOf(panes, source);
        int count = source.GroupMembers(group).Count;
        if (sourceId < 0 || !visiblePanes.Contains(sourceId) || count == 0
            || target < 0 || target >= panes.Length || (edge is not null && !visiblePanes.Contains(target)))
        { reason = "This group is no longer available."; return false; }
        if (edge is null && target == sourceId)
        { reason = "Drop near a file-list edge to split, in the center to move, or on the tab strip to reorder.\nEsc cancels."; return false; }
        bool relocatePane = edge is not null && target != sourceId && source.TabCount == count;
        if (edge is not null && !relocatePane && visiblePanes.Length == panes.Length)
        { reason = "Maximum 4 panes.\nDrop in a pane's center to move the group instead."; return false; }
        int destination = edge is null ? target : relocatePane ? sourceId
            : Enumerable.Range(0, panes.Length).First(i => !visiblePanes.Contains(i));
        if (!relocatePane && panes[destination].TabCount + count > 32)
        { reason = "Destination cannot hold this group.\nClose tabs first."; return false; }
        return true;
    }

    internal bool DockGroup(ExplorerPane source, TabGroup group, int target, SplitEdge? edge, int? insertionIndex = null)
    {
        if (!CanDockGroup(source, group, target, edge, out string reason))
        { source.SetLocalizedStatus(() => text.T(reason).Replace("\n", " ")); return false; }
        int sourceId = source.PaneNumber - 1;
        var members = source.GroupMembers(group);
        bool wholePane = source.TabCount == members.Count;
        bool wasRebuilding = rebuilding;
        rebuilding = true;
        try
        {
            if (edge is { } side && target != sourceId && wholePane)
            {
                InvalidateRedo();
                source.SetGroupCollapsed(group, false);
                RenderLayout(RemovePane(layoutTree, sourceId)!.Split(target, sourceId, side));
                ActivatePane(sourceId);
                source.FocusFiles();
            }
            else
            {
                int destination = edge is null ? target
                    : Enumerable.Range(0, panes.Length).First(i => !visiblePanes.Contains(i));
                var receiving = panes[destination];
                int selected = 0;
                for (int i = 0; i < members.Count; i++)
                    if (ReferenceEquals(members[i], source.ActiveTab)) { selected = i; break; }
                var paths = members.Select(tab => tab.Host?.LocationPath).ToArray();
                var moved = receiving.AddGroup(paths, group.Name, group.Color, collapsed: false, selectedMember: selected);
                if (moved is null) return false;
                if (insertionIndex is { } insertion) receiving.ReorderGroup(moved, insertion);
                InvalidateRedo();
                source.CloseGroup(group, remember: false);
                var nextLayout = layoutTree;
                if (edge is { } splitEdge) nextLayout = nextLayout.Split(target, destination, splitEdge);
                else
                {
                    if (!visiblePanes.Contains(destination)) nextLayout = nextLayout.Split(sourceId, destination, SplitEdge.Right);
                    if (wholePane) nextLayout = RemovePane(nextLayout, sourceId)!;
                }
                if (!ReferenceEquals(nextLayout, layoutTree)) RenderLayout(nextLayout);
                ActivatePane(destination);
                receiving.FocusFiles();
            }
        }
        finally { rebuilding = wasRebuilding; }
        ScheduleSave();
        return true;
    }

    private static PaneLayout? RemovePane(PaneLayout node, int id)
    {
        if (node.IsLeaf) return node.Pane == id ? null : node;
        var first = RemovePane(node.First!, id);
        var second = RemovePane(node.Second!, id);
        if (first is null) return second;
        if (second is null) return first;
        return node with { First = first, Second = second };
    }

    private static void SetSplitRatio(SplitContainer split, double ratio)
    {
        int size = (split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height) - split.SplitterWidth;
        if (size >= split.Panel1MinSize + split.Panel2MinSize)
            split.SplitterDistance = Math.Clamp((int)(size * ratio), split.Panel1MinSize, size - split.Panel2MinSize);
    }

    private static double SplitRatio(SplitContainer split)
    {
        int size = (split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height) - split.SplitterWidth;
        return size > 0 ? (double)split.SplitterDistance / size : .5;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (TrackColumnDivider(ref m)) return true;
        if ((!ContainsFocus && previewWindow?.ContainsFocus != true && !previewWindows.Any(entry => entry.Window.ContainsFocus))
            || tabDragging) return false;
        // Preview windows are owned by this form and always sit above it, so they get the wheel first.
        foreach (var (window, pane) in previewWindows)
            if (window.Visible && pane.RouteMouseWheel(ref m)) return true;
        if (showPreview && previewPane.RouteMouseWheel(ref m)) return true;
        if (m.Msg is 0x20A or 0x20E)
            foreach (int i in visiblePanes)
                if (panes[i].HandleTabWheel(ref m)) return true;
        if (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x20B || m.Msg == 0x100 || m.Msg == 0x104)
        {
            foreach (int i in visiblePanes)
                if (panes[i].IsHandleCreated && (m.HWnd == panes[i].Handle || ShellNative.IsChild(panes[i].Handle, m.HWnd)))
                { if (i != activePane) ActivatePane(i); break; }
        }
        // Mouse back/forward (X1/X2) buttons travel the history of the pane under the pointer.
        // Down is swallowed too, so the Shell view does not also act on the click.
        if (m.Msg is 0x20B or 0x20C)
        {
            nint window = m.HWnd;
            int target = Array.FindIndex(visiblePanes, i => panes[i].IsHandleCreated &&
                (window == panes[i].Handle || ShellNative.IsChild(panes[i].Handle, window)));
            if (target < 0) return false;
            int button = (int)(((long)m.WParam >> 16) & 0xFFFF);
            if (m.Msg == 0x20C && button is 1 or 2)
                panes[visiblePanes[target]].ActiveView?.Travel(button == 1 ? 0x4000u : 0x8000u);
            return button is 1 or 2;
        }
        if (m.Msg is 0x100 or 0x104)
        {
            Keys key = (Keys)(int)m.WParam | ModifierKeys;
            if (key != (Keys.Alt | Keys.P) && key != (Keys.Control | Keys.Shift | Keys.F11) &&
                (previewWindow?.ContainsFocus == true || ShellNative.IsChild(previewPane.Handle, m.HWnd))) return false;
            if (Active.HandleTabStripKey(key)) return true;
            if (!AltGrDown(key) && HandleShortcut(key)) return true;
        }
        if (toolbar.ContainsFocus || Active.TabStrip.ContainsFocus) return false; // Let native strips handle navigation.
        if (m.Msg >= 0x100 && m.Msg <= 0x109) return Active.ActiveView?.TranslateKey(ref m) ?? false;
        return false;
    }

    // Layouts with an AltGr key (Polish, US-International and others) report AltGr as Ctrl+Alt, so AltGr+O is the
    // letter "o" with an acute accent and not the app's Ctrl+Alt+O. The right Alt key being down tells them apart.
    private static bool AltGrDown(Keys key) =>
        (key & (Keys.Control | Keys.Alt)) == (Keys.Control | Keys.Alt) && GetKeyState(0xA5) < 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    // The hosted file list waits until the button is released to resize a column. Track the divider
    // here instead and set the width on every mouse move, as File Explorer does.
    private bool TrackColumnDivider(ref Message m)
    {
        if (columnDrag is { } drag)
        {
            int x = unchecked((short)(long)m.LParam);
            switch (m.Msg)
            {
                case 0x200 when m.HWnd == drag.Header: // WM_MOUSEMOVE
                    int minimum = (int)Math.Round(24 * DeviceDpi / 96d);
                    SendMessage(drag.List, 0x101E, drag.Column, Math.Max(minimum, drag.StartWidth + x - drag.StartX)); // LVM_SETCOLUMNWIDTH
                    // The list defers its own repaint, and continuous mouse input keeps it waiting; paint it now.
                    RedrawWindow(drag.List, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0100); // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW
                    Cursor.Current = Cursors.VSplit;
                    return true;
                case 0x202 when m.HWnd == drag.Header: // WM_LBUTTONUP
                    columnDrag = null;
                    ReleaseCapture();
                    return true;
                case 0x100 when (Keys)(int)m.WParam == Keys.Escape:
                    SendMessage(drag.List, 0x101E, drag.Column, drag.StartWidth);
                    columnDrag = null;
                    ReleaseCapture();
                    return true;
                case 0x215: // WM_CAPTURECHANGED
                    columnDrag = null;
                    return false;
            }
            return false;
        }
        if (m.Msg != 0x201 || !IsColumnHeader(m.HWnd)) return false; // WM_LBUTTONDOWN
        var hit = new HeaderHit { X = unchecked((short)(long)m.LParam), Y = unchecked((short)((long)m.LParam >> 16)) };
        SendMessage(m.HWnd, 0x1206, 0, ref hit); // HDM_HITTEST
        if ((hit.Flags & 0x4) == 0 || hit.Item < 0) return false; // HHT_ONDIVIDER
        nint list = GetParent(m.HWnd);
        columnDrag = (m.HWnd, list, hit.Item, hit.X, (int)SendMessage(list, 0x101D, hit.Item, 0)); // LVM_GETCOLUMNWIDTH
        SetCapture(m.HWnd);
        Cursor.Current = Cursors.VSplit;
        return true;
    }

    internal bool ColumnDividerDragging => columnDrag is not null;

    [StructLayout(LayoutKind.Sequential)]
    private struct HeaderHit { public int X, Y; public uint Flags; public int Item; }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, ref HeaderHit hit);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint window);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    // Memory inside Windows' hosted file list grows with every folder visited and is only returned when the
    // process ends. Once it grew past the chosen amount, restart quietly while the window is out of the way.
    internal static bool ShouldRestartForMemory(long growth, int thresholdMb, TimeSpan uptime, bool unobtrusive) =>
        thresholdMb > 0 && uptime >= TimeSpan.FromMinutes(30) && unobtrusive && growth > thresholdMb * 1024L * 1024L;


    internal int AutoRestartMemory => autoRestartMemory;

    internal void SetAutoRestartMemory(int megabytes)
    {
        application.ChangeSettings(s => s with { AutoRestartMemory = megabytes is 0 or 100 or 200 or 300 or 500 ? megabytes : 100 });
    }

    private void BuildMemoryMenu()
    {
        foreach (var old in memoryMenu.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
        memoryMenu.DropDownItems.Add(text.T("Clean up memory now (restart)"), null, (_, _) => RestartApplication());
        memoryMenu.DropDownItems.Add(new ToolStripSeparator());
        memoryMenu.DropDownItems.Add(new ToolStripMenuItem(text.T("Restart automatically when hidden or minimized")) { Enabled = false });
        foreach (int megabytes in new[] { 100, 200, 300, 500, 0 })
        {
            int choice = megabytes;
            var item = new ToolStripMenuItem(choice == 0 ? text.T("Off") : text.T("After {0} MB more memory", choice))
            {
                Checked = autoRestartMemory == choice
            };
            item.Click += (_, _) => SetAutoRestartMemory(choice);
            memoryMenu.DropDownItems.Add(item);
        }
        Theme.Apply(toolbar);
    }

    // Saves the session and starts a fresh instance in the same state (hidden, minimized or shown).
    internal void RestartApplication()
    {
        application.RestartApplication(this);
    }

    private nint lastColumnHeader;

    private bool IsColumnHeader(nint window)
    {
        if (!IsHandleCreated || !ShellNative.IsChild(Handle, window)) return false;
        if (window == lastColumnHeader) return true;
        var name = new System.Text.StringBuilder(16);
        if (GetClassName(window, name, name.Capacity) == 0 || name.ToString() != "SysHeader32") return false;
        lastColumnHeader = window;
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int length);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);


    private void RememberClosed(int pane, PaneLayout? layout)
    {
        if (!redoingClose) InvalidateRedo(pane);
        closedItems.Add((pane, layout));
        if (closedItems.Count > 32) closedItems.RemoveAt(0);
        RefreshCloseHistory();
    }

    private void InvalidateRedo(int? pane = null)
    {
        // A new action forks global redo without discarding other panes' local tab redo.
        for (int i = reopenedItems.Count - 1; i >= 0; i--)
        {
            var item = reopenedItems[i];
            if ((item.Tab is null && item.Group is null) || item.Pane == pane) reopenedItems.RemoveAt(i);
            else reopenedItems[i] = (item.Pane, item.Tab, item.Group, false);
        }
    }

    private int RedoIndex(int? pane = null) => reopenedItems.FindLastIndex(item =>
        (pane is null ? item.Global : item.Pane == pane && (item.Tab is not null || item.Group is not null))
        && visiblePanes.Contains(item.Pane) && (item.Group is { } group
            ? panes[item.Pane].Groups.Contains(group) : item.Tab is { } tab
                ? panes[item.Pane].IndexOfTab(tab) >= 0 : visiblePanes.Length > 1));

    private void RefreshCloseHistory()
    {
        bool canUndo = closedItems.Count > 0
            ? closedItems[^1].Layout is not null || panes[closedItems[^1].Pane].CanReopenTab
            : Active.CanReopenTab;
        undoClose.Enabled = canUndo;
        redoClose.Enabled = RedoIndex() >= 0;
        foreach (var pane in panes)
            pane.SetCloseHistory(pane.CanReopenTab, RedoIndex(pane.PaneNumber - 1) >= 0);
    }

    private void RedoClosedItem(int? scope = null)
    {
        int index = RedoIndex(scope);
        if (index < 0) return;
        var item = reopenedItems[index];
        reopenedItems.RemoveAt(index);
        redoingClose = true;
        try
        {
            if (item.Tab is null && item.Group is null) ClosePane(item.Pane);
            else
            {
                var pane = panes[item.Pane];
                if (item.Group is { } group) pane.CloseGroup(group);
                else pane.CloseTab(pane.IndexOfTab(item.Tab!));
                ActivatePane(item.Pane);
                pane.FocusFiles();
            }
        }
        finally { redoingClose = false; RefreshCloseHistory(); }
    }

    private void ClosePane(int number)
    {
        if (visiblePanes.Length <= 1 || !visiblePanes.Contains(number)) return;
        RememberClosed(number, layoutTree);
        RenderLayout(RemovePane(layoutTree, number)!, preserveClosedPanes: true);
        Active.FocusFiles();
    }

    private void ReopenClosedItem()
    {
        if (closedItems.Count == 0) { Active.ReopenTab(); return; }
        var item = closedItems[^1];
        if (item.Layout is { } layout)
        {
            closedItems.RemoveAt(closedItems.Count - 1);
            RenderLayout(layout, preserveClosedPanes: true);
            reopenedItems.Add((item.Pane, null, null, true));
            ActivatePane(item.Pane);
            Active.FocusFiles();
            RefreshCloseHistory();
        }
        else
        {
            var pane = panes[item.Pane];
            if (!pane.CanReopenTab) return;
            if (!visiblePanes.Contains(item.Pane))
                RenderLayout(layoutTree.Split(activePane, item.Pane, SplitEdge.Right), preserveClosedPanes: true);
            pane.ReopenTab();
        }
    }

    private bool HandleShortcut(Keys key)
    {
        switch (key)
        {
            case Keys.Control | Keys.N: application.CreateWindowFromActive(this); break;
            case Keys.Control | Keys.T: Active.AddBlankTab(); break;
            case Keys.Control | Keys.F: Active.FocusFolderFilter(); break;
            case Keys.Control | Keys.Shift | Keys.C: Active.CopySelectionPaths(); break;
            case Keys.Control | Keys.Alt | Keys.C: TransferSelection(move: false); break;
            case Keys.Control | Keys.Alt | Keys.X: TransferSelection(move: true); break;
            case Keys.Control | Keys.W:
                if (Active.TabCount > 0) Active.CloseTab();
                else ClosePane(activePane);
                break;
            case Keys.Control | Keys.Shift | Keys.T: ReopenClosedItem(); break;
            case Keys.Control | Keys.L:
            case Keys.Alt | Keys.D: Active.FocusAddress(); break;
            case Keys.Control | Keys.D: AddFavorite(Active.CurrentPath); break;
            case Keys.Control | Keys.Shift | Keys.E: SetFolderTreeVisible(!showFolderTree); break;
            case Keys.Alt | Keys.P: SetPreviewVisible(!showPreview); break;
            case Keys.Control | Keys.Shift | Keys.F11: SetGlobalToolbarPinned(!globalToolbarPinned); break;
            case Keys.Control | Keys.Tab: Active.SelectAdjacentTab(1); break;
            case Keys.Control | Keys.Shift | Keys.Tab: Active.SelectAdjacentTab(-1); break;
            case Keys.Control | Keys.Shift | Keys.PageUp: Active.MoveCurrentTab(-1); break;
            case Keys.Control | Keys.Shift | Keys.PageDown: Active.MoveCurrentTab(1); break;
            case Keys.Control | Keys.Shift | Keys.F6: Active.FocusTabStrip(); break;
            case Keys.Control | Keys.D1: ApplyLayout(1); break;
            case Keys.Control | Keys.D2: ApplyLayout(2); break;
            case Keys.Control | Keys.D3: ApplyLayout(3); break;
            case Keys.Control | Keys.Shift | Keys.D3: ApplyLayout(3, false); break;
            case Keys.Control | Keys.D4: ApplyLayout(4); break;
            case Keys.F6: CyclePane(1); break;
            case Keys.Shift | Keys.F6: CyclePane(-1); break;
            case Keys.Alt | Keys.D1: FocusPane(0); break;
            case Keys.Alt | Keys.D2: FocusPane(1); break;
            case Keys.Alt | Keys.D3: FocusPane(2); break;
            case Keys.Alt | Keys.D4: FocusPane(3); break;
            case Keys.Control | Keys.Alt | Keys.O: OpenFolderInOtherPane(); break;
            case Keys.F5: Active.ActiveView?.RefreshFolder(); break;
            case Keys.Alt | Keys.Left: Active.ActiveView?.Travel(0x4000); break;
            case Keys.Alt | Keys.Right: Active.ActiveView?.Travel(0x8000); break;
            case Keys.Alt | Keys.Up: Active.ActiveView?.Travel(0x2000); break;
            case Keys.F1: ShowHelp(); break;
            default: return false;
        }
        return true;
    }

    private void ScheduleSave()
    {
        if (ready && !rebuilding) application.ScheduleSave();
    }

    private void SaveSession() => application.SaveSession();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        application.Closing(this, e);
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ready = false;
            rebuilding = true;
            previewWindow?.Dispose();
            previewWindow = null;
            updateNotice?.Dispose();
            Application.RemoveMessageFilter(this);
            previewTimer.Dispose();
            toolbarHideTimer.Dispose();
            toolbarRevealTimer.Dispose();
            tips.Dispose();
            places.ContextMenuStrip?.Dispose();
            favoritePlaces.ContextMenuStrip?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var pane in panes) if (!pane.IsDisposed) pane.Dispose();
            application.Unregister(this);
            placeIcons.Dispose();
            appIcon.Dispose();
        }
    }

    internal string ActiveFolder => Active.CurrentPath;
    internal bool IsUnobtrusive => (!Visible || WindowState == FormWindowState.Minimized)
        && previewWindows.Count == 0 && !tabDragging && columnDrag is null
        && OwnedForms.All(form => !form.Visible);

    internal BrowserWindowState CaptureWindowState()
    {
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        return new BrowserWindowState
        {
            Layout = visiblePanes.Length, LayoutTree = layoutTree,
            Panes = panes.Select(pane => pane.CaptureState()).ToList(), ActivePane = activePane,
            WindowX = bounds.X, WindowY = bounds.Y, WindowWidth = bounds.Width, WindowHeight = bounds.Height,
            Maximized = maximizedPresentation, Hidden = !Visible, Minimized = WindowState == FormWindowState.Minimized,
            ShowFolderTree = showFolderTree, FolderTreeHeight = folderTreeHeight,
            ShowPreview = showPreview, ShowDetails = showDetails, PreviewWidth = previewWidth,
            ShowSidebar = showSidebar, SidebarWidth = sidebarWidth, FavoritesExpanded = favoritesExpanded,
            ColumnRatio = layoutTree.Ratio, LeftRowRatio = layoutTree.First?.Ratio ?? .5,
            RightRowRatio = layoutTree.Second?.Ratio ?? .5
        };
    }

    internal void RefreshFavorites() => ReloadPlaces();
    internal void RefreshShellViews() { foreach (var pane in panes) pane.RefreshViews(); }
    internal void SetApplicationStatus(string key, params object[] values) => Active.SetLocalizedStatus(key, values);
    internal void RefreshUpdate() { UpdateVersionText(); ShowPendingUpdateNotice(); }

    internal void RefreshSharedSettings(SessionState? previous = null, bool theme = false)
    {
        if (!ready) return;
        var state = application.State;
        bool languageChanged = text.Language != state.Language;
        text.SetLanguage(state.Language);
        int index = Array.IndexOf(new[] { "ko", "en", "zh-CN", "ja" }, text.Language);
        if (languageSelector.SelectedIndex != index) languageSelector.SelectedIndex = index;
        hotkeyItem.Checked = state.GlobalHotkey;
        trayItem.Checked = state.KeepInTray;
        startupItem.Checked = Startup.IsEnabled;
        winEItem.Checked = winE.IsEnabled;
        updateItem.Checked = state.CheckUpdates;
        autoUpdateItem.Checked = state.AutoUpdate;
        bool commandsChanged = showCommandBar != state.ShowCommandBar;
        bool detailsChanged = showVerticalSeparators != state.ShowVerticalSeparators
            || comfortableRows != state.ComfortableRows || highlightSortedColumn != state.HighlightSortedColumn;
        showCommandBar = state.ShowCommandBar;
        showVerticalSeparators = state.ShowVerticalSeparators;
        comfortableRows = state.ComfortableRows;
        highlightSortedColumn = state.HighlightSortedColumn;
        bool fontChanged = fontFamily != state.FontFamily || textSize != state.TextSize;
        fontFamily = state.FontFamily;
        textSize = state.TextSize;
        if (fontChanged)
        {
            Font = new Font(UiFace, UiPoints);
            favoritesHeader.Font = treeHeader.Font = HeaderFont();
            if (sideTitleLabel is not null) sideTitleLabel.Font = HeaderFont();
        }
        if (globalToolbarPinned != state.GlobalToolbarPinned) SetGlobalToolbarPinned(state.GlobalToolbarPinned);
        foreach (var pane in panes)
        {
            if (previous?.ActivePaneColor != state.ActivePaneColor) pane.SetActivePaneColor(state.ActivePaneColor);
            if (commandsChanged) pane.SetCommandBarVisible(showCommandBar);
            if (detailsChanged) pane.SetDetailsAppearance(showVerticalSeparators, comfortableRows, highlightSortedColumn);
            if (languageChanged) pane.RefreshLanguage();
        }
        if (languageChanged)
        {
            foreach (var refresh in languageBindings) refresh();
            folderTree.RefreshLanguage();
            ReloadPlaces();
        }
        if (theme || fontChanged) ApplyAppearance();
        if (previous?.CheckUpdates != state.CheckUpdates || previous?.AutoUpdate != state.AutoUpdate) UpdateVersionText();
    }

    private void ShowHelp()
    {
        using var dialog = new ShortcutHelpDialog(text, Font);
        dialog.ShowDialog(this);
    }
}
