using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using QuadExplorer.Search;

namespace QuadExplorer;

public sealed class MainForm : Form, IMessageFilter
{
    private readonly string sessionPath;
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
    private int previewWidth;
    private readonly Button treeHeader;
    private readonly Panel treePanel = new() { Dock = DockStyle.Bottom };
    private readonly Splitter treeSplitter = new()
    {
        Dock = DockStyle.Bottom, Height = 6, Cursor = Cursors.HSplit, AccessibleName = "Resize favorites and folder tree"
    };
    private int folderTreeHeight;
    private const int HotkeyId = 0x4653; // "FS"
    private readonly uint activationMessage;
    private readonly ToolStripMenuItem hotkeyItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem winEItem = new() { CheckOnClick = true };
    private readonly WinEShortcut winE = WinEShortcut.ForCurrentUser();
    private bool globalHotkey;
    private readonly NotifyIcon trayIcon = new() { Text = "Folder Splitter" };
    private readonly ToolStripMenuItem trayItem = new() { CheckOnClick = true };
    private bool closeToTray;
    private bool exiting;
    private bool trayNoticeShown;
    private readonly ToolStripButton themeButton = new() { AccessibleName = "Color theme" };
    private string colorTheme;
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
    private int[] visiblePanes;
    private readonly Panel sidebar;
    private readonly Splitter sidebarSplitter;
    private readonly ToolStripButton sidebarButton = new("Sidebar") { AccessibleName = "Sidebar" };
    private int sidebarWidth;
    private readonly List<string> deletedFavorites;
    private bool explorerFavoritesImported;
    private readonly ListBox places;
    private readonly ListBox favoritePlaces;
    private readonly Button favoritesHeader;
    private bool favoritesExpanded;
    private readonly List<string> favorites;
    private readonly ToolStrip toolbar = Theme.Strip();
    private readonly LinkLabel versionLabel = new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Muted, BackColor = Theme.Canvas, Padding = new Padding(12, 4, 0, 4),
        AccessibleName = "Application version", LinkBehavior = LinkBehavior.HoverUnderline
    };
    private readonly Func<CancellationToken, Task<ReleaseInfo?>>? latestRelease;
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 24 * 60 * 60 * 1000 };
    private readonly CancellationTokenSource updateCancellation = new();
    private readonly ToolStripMenuItem updateItem = new() { CheckOnClick = true };
    private ReleaseInfo? availableUpdate;
    private bool checkUpdates;
    private readonly ToolStripMenuItem autoUpdateItem = new() { CheckOnClick = true };
    private bool autoUpdate;
    private bool updating;
    private readonly ToolStripButton[] layoutButtons = new ToolStripButton[4];
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
    private readonly ToolTip tips = new() { ShowAlways = true, AutoPopDelay = 10000 };
    private int activePane;
    private int previousPane = -1;
    private readonly List<(Form Window, PreviewPane Pane)> previewWindows = [];
    private int autoRestartMemory;
    private long memoryBaseline;
    private bool restarting;
    private readonly DateTime startedAt = DateTime.UtcNow;
    private readonly System.Windows.Forms.Timer memoryTimer = new() { Interval = 5 * 60 * 1000 };
    private readonly ToolStripMenuItem memoryMenu = new();
    private (nint Header, nint List, int Column, int StartX, int StartWidth)? columnDrag;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool StartMinimized { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool StartHidden { get; init; }
    // Tests replace the process launch; it receives the arguments and reports whether a new instance started.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Func<IReadOnlyList<string>, bool>? RestartLauncher { get; set; }
    private readonly List<Workspace> workspaces;
    private readonly ToolStripDropDownButton workspaceButton = new() { AccessibleName = "Workspaces" };
    internal event Action<bool, string?>? FileOperationFinished;
    private bool ready;
    private bool saving;
    private bool rebuilding;
    private bool tabDragging;
    private string? saveError;
    private readonly record struct Place(string Label, string Path, bool Favorite = false)
    {
        public override string ToString() => Label;
    }

    public MainForm(string sessionPath, IReadOnlyList<string> folders, IPathSearchBackend? search = null,
        IReadOnlyList<string>? treeRoots = null, Func<CancellationToken, Task<ReleaseInfo?>>? latestRelease = null)
    {
        this.search = search;
        this.latestRelease = latestRelease;
        this.sessionPath = sessionPath;
        initialState = SessionStore.Normalize(SessionStore.Load(sessionPath));
        colorTheme = initialState.ColorTheme;
        showFolderTree = initialState.ShowFolderTree;
        folderTreeHeight = initialState.FolderTreeHeight;
        globalHotkey = initialState.GlobalHotkey;
        closeToTray = initialState.KeepInTray;
        trayNoticeShown = initialState.TrayNoticeShown;
        checkUpdates = initialState.CheckUpdates;
        autoUpdate = initialState.AutoUpdate;
        activationMessage = Program.ActivationMessage(sessionPath);
        Theme.Set(colorTheme);
        text = new UiText(initialState.Language);
        layoutTree = initialState.LayoutTree!;
        visiblePanes = layoutTree.GetPanes().Order().ToArray();
        activePane = initialState.ActivePane;
        favorites = [.. initialState.Favorites];
        deletedFavorites = [.. initialState.DeletedFavorites];
        workspaces = [.. initialState.Workspaces];
        autoRestartMemory = initialState.AutoRestartMemory;
        memoryTimer.Tick += (_, _) => CheckMemory();
        explorerFavoritesImported = initialState.ExplorerFavoritesImported;
        sidebarWidth = initialState.SidebarWidth;
        favoritesExpanded = initialState.FavoritesExpanded;
        Text = "Folder Splitter";
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        MinimumSize = new Size(900, 600);
        Size = new Size(initialState.WindowWidth, initialState.WindowHeight);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        UpdateVersionText();
        languageBindings.Add(UpdateVersionText);
        versionLabel.LinkClicked += async (_, _) =>
        {
            if (availableUpdate is not { } update || updating) return;
            if (CanInstallUpdate(update) && UpdateInstaller.InstallablePath() is { } exe) await InstallUpdateAsync(update, exe);
            else OpenReleasePage(update);
        };
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("QuadExplorer.AppIcon")!)
        using (var sourceIcon = new Icon(iconStream))
            appIcon = (Icon)sourceIcon.Clone();
        Icon = appIcon;
        var trayMenu = new ContextMenuStrip();
        BindText(trayMenu.Items.Add("", null, (_, _) => Summon()), "Open Folder Splitter");
        BindText(trayMenu.Items.Add("", null, (_, _) => ExitApplication()), "Exit");
        trayIcon.Icon = appIcon;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Summon(); };
        trayIcon.Visible = closeToTray;

        panes = Enumerable.Range(0, 4).Select(i => new ExplorerPane(i + 1, initialState.Panes[i], text,
            search, SearchRoots)).ToArray();
        foreach (var pane in panes)
        {
            pane.Activated += (_, _) => ActivatePane(Array.IndexOf(panes, pane));
            pane.StateChanged += (_, _) =>
            {
                ScheduleSave();
                if (ready && ReferenceEquals(pane, Active)) SyncTree();
                if (ReferenceEquals(pane, Active)) SchedulePreview();
            };
            pane.SelectionChanged += (_, _) => { if (ReferenceEquals(pane, Active)) SchedulePreview(); };
            pane.FavoriteRequested += (_, path) => AddFavorite(path);
            pane.MoveTabRequested += MoveTab;
            pane.TabDragRequested += StartTabDrag;
            pane.SplitTabRequested += (source, tab, edge) => DockTab(source, tab, source.PaneNumber - 1, edge);
            pane.TransferRequested += (source, move) => { ActivatePane(Array.IndexOf(panes, source)); TransferSelection(move); };
        }

        showPreview = initialState.ShowPreview;
        previewWidth = initialState.PreviewWidth > 0 ? Math.Clamp(initialState.PreviewWidth, 200, 1600) : 380;
        previewPane = new PreviewPane(text) { Dock = DockStyle.Right, Width = previewWidth, Visible = showPreview };
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
            BackColor = Theme.Border, AccessibleName = "Resize quick access"
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
            Text = "QUICK ACCESS", Dock = DockStyle.Top, Height = 28,
            Font = new Font(Font.FontFamily, 8, FontStyle.Bold), ForeColor = Theme.Muted,
            Padding = new Padding(7, 0, 0, 0)
        };
        BindText(sideTitle, "QUICK ACCESS");
        places = new ListBox
        {
            Dock = DockStyle.Top, Height = 216, BorderStyle = BorderStyle.None, BackColor = Theme.Canvas,
            ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36,
            IntegralHeight = false, AccessibleName = "Quick access"
        };
        places.DrawItem += DrawPlace;
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
            ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36,
            IntegralHeight = false, AccessibleName = "Favorites"
        };
        favoritePlaces.DrawItem += DrawPlace;
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
        favoritesHeader = Theme.Button("", "Collapse favorites", ToggleFavorites);
        favoritesHeader.Dock = DockStyle.Top;
        favoritesHeader.Height = 32;
        favoritesHeader.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        favoritesHeader.TextAlign = ContentAlignment.MiddleLeft;
        favoritesHeader.ForeColor = Theme.Muted;
        favoritesHeader.FlatAppearance.BorderSize = 0;
        BindTip(favoritesHeader, "Collapse / expand favorites");
        BindTip(sidebarSplitter, "Drag to resize quick access; double-click to hide");
        favoritesHeader.ContextMenuStrip = favoriteMenu;
        var manage = Theme.Button("Manage...", "Manage favorites", () => favoriteMenu.Show(Cursor.Position));
        manage.Dock = DockStyle.Bottom;
        manage.AutoSize = true;
        BindText(manage, "Manage...");
        BindTip(manage, "Manage favorites: import, remove all, restore");
        var pin = Theme.Button("+  Pin folder", "Pin current folder to favorites", () => AddFavorite(Active.CurrentPath), 152);
        pin.Dock = DockStyle.Bottom;
        pin.Height = 36;
        pin.BackColor = Theme.Canvas;
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
        treeHeader = Theme.Button("", "Collapse folder tree", () => SetFolderTreeVisible(!showFolderTree));
        treeHeader.Dock = DockStyle.Top;
        treeHeader.Height = 32;
        treeHeader.Font = favoritesHeader.Font;
        treeHeader.TextAlign = ContentAlignment.MiddleLeft;
        treeHeader.ForeColor = Theme.Muted;
        treeHeader.FlatAppearance.BorderSize = 0;
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
        var workspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 8) };
        workspace.Controls.Add(paneHost);
        workspace.Controls.Add(previewSplitter);
        workspace.Controls.Add(previewPane);
        workspace.Controls.Add(sidebarSplitter);
        workspace.Controls.Add(sidebar);
        Controls.Add(workspace);
        Controls.Add(versionLabel);
        Controls.Add(toolbar);
        sidebar.Visible = initialState.ShowSidebar;
        sidebarSplitter.Visible = initialState.ShowSidebar;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSession(); };
        Load += (_, _) => { if (StartMinimized) WindowState = FormWindowState.Minimized; };
        Shown += (_, _) =>
        {
            if (StartHidden && closeToTray) BeginInvoke(Hide);
            memoryTimer.Start();
            if (initialState.Maximized && !StartMinimized) WindowState = FormWindowState.Maximized;
            UpdateMinimumSize();
            if (WindowState == FormWindowState.Normal)
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                int width = Math.Min(Width, work.Width);
                int height = Math.Min(Height, work.Height);
                Bounds = new Rectangle(Math.Clamp(Left, work.Left, work.Right - width),
                    Math.Clamp(Top, work.Top, work.Bottom - height), width, height);
            }
            rebuilding = true;
            try { ApplySplitRatios(); }
            finally { rebuilding = false; }
            ready = true;
            ApplyAppearance();
            if (!explorerFavoritesImported) ImportExplorerFavorites();
            ActivatePane(activePane);
            if (folders.Count > 0 && !File.Exists(sessionPath)) Active.CloseTab();
            foreach (string folder in folders) Active.AddTab(folder);
            Active.FocusFiles();
            updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
            updateTimer.Enabled = checkUpdates && latestRelease is not null;
            _ = CheckForUpdateAsync();
        };
        ResizeEnd += (_, _) => ScheduleSave();
        RenderLayout(layoutTree);
        Application.AddMessageFilter(this);
        ApplyAppearance();
    }

    private ExplorerPane Active => panes[activePane];
    private IReadOnlyList<string> SearchRoots() => new[] { Active.CurrentPath }
        .Concat(panes.SelectMany(p => p.CaptureState().Tabs)).Concat(favorites)
        .Concat(panes.SelectMany(p => p.RecentPaths))
        .Where(Path.IsPathRooted).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    internal PaneLayout CurrentLayout => layoutTree;
    internal ExplorerPane GetPane(int id) => panes[id];
    internal string Language => text.Language;

    internal void SetLanguage(string language)
    {
        text.SetLanguage(language);
        int index = Array.IndexOf(new[] { "ko", "en", "zh-CN", "ja" }, text.Language);
        if (languageSelector.SelectedIndex != index) languageSelector.SelectedIndex = index;
        foreach (var refresh in languageBindings) refresh();
        foreach (var pane in panes) pane.RefreshLanguage();
        folderTree.RefreshLanguage();
        ReloadPlaces();
        ScheduleSave();
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

    private void BindTip(Control control, string key)
    {
        void Refresh() => tips.SetToolTip(control, text.T(key));
        languageBindings.Add(Refresh);
        Refresh();
    }

    private void BuildToolbar()
    {
        toolbar.Padding = new Padding(10, 7, 10, 7);
        var brand = new ToolStripLabel("Folder Splitter") { Font = new Font(Font, FontStyle.Bold), Margin = new Padding(5, 0, 18, 0) };
        toolbar.Items.Add(brand);
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
                var arrangements = new ToolStripDropDownButton("\u25BE")
                {
                    AccessibleName = "Three pane arrangements",
                    ToolTipText = text.T("Choose three-pane arrangement: left/right or top/bottom 2:1"),
                    AutoSize = false, Width = 22
                };
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
        themeButton.Click += (_, _) => SetColorTheme(colorTheme == "dark" ? "light" : "dark");
        themeButton.Alignment = ToolStripItemAlignment.Right;
        themeButton.Overflow = ToolStripItemOverflow.Never;
        var help = new ToolStripButton
        {
            Alignment = ToolStripItemAlignment.Right, AccessibleName = "Shortcuts",
            Overflow = ToolStripItemOverflow.Never
        };
        BindText(help, "Shortcuts", "Show shortcuts and help (F1)");
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
        BindText(languageLabel, "Language");
        languageSelector.Items.AddRange(["한국어", "English", "简体中文", "日本語"]);
        languageSelector.SelectedIndex = Array.IndexOf(new[] { "ko", "en", "zh-CN", "ja" }, text.Language);
        languageSelector.SelectedIndexChanged += (_, _) =>
        {
            if (languageSelector.SelectedIndex >= 0)
                SetLanguage(new[] { "ko", "en", "zh-CN", "ja" }[languageSelector.SelectedIndex]);
        };
        languageBindings.Add(() => languageSelector.ToolTipText = text.T("Choose UI language"));
        languageSelector.ToolTipText = text.T("Choose UI language");
        toolbar.Items.Add(languageSelector);
        toolbar.Items.Add(languageLabel);
        toolbar.Items.Add(themeButton); // Right-aligned items run right to left: theme sits left of the language label.
        var settings = new ToolStripDropDownButton
        {
            Alignment = ToolStripItemAlignment.Right, AccessibleName = "Settings", Overflow = ToolStripItemOverflow.Never
        };
        BindText(settings, "Settings", "Shortcuts and Windows integration");
        BindText(hotkeyItem, "Win+Alt+E brings Folder Splitter to the front");
        BindText(winEItem, "Open Folder Splitter with Win+E instead of File Explorer");
        hotkeyItem.Checked = globalHotkey;
        hotkeyItem.CheckedChanged += (_, _) => SetGlobalHotkey(hotkeyItem.Checked);
        winEItem.Click += (_, _) => SetWinEShortcut(winEItem.Checked);
        settings.DropDownOpening += (_, _) => winEItem.Checked = winE.IsEnabled;
        try { winE.UpgradeLegacy(); }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        settings.DropDownItems.Add(hotkeyItem);
        settings.DropDownItems.Add(winEItem);
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
        toolbar.Items.Add(settings);
        toolbar.Layout += (_, _) =>
        {
            int width = Math.Max(100, languageSelector.Items.Cast<string>()
                .Max(name => TextRenderer.MeasureText(name, languageSelector.Font).Width)
                + (int)Math.Ceiling(32 * toolbar.DeviceDpi / 96d));
            if (languageSelector.Width != width) languageSelector.Width = width;
            languageSelector.DropDownWidth = width;
        };
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
    internal bool FolderTreeVisible => showFolderTree;
    internal FolderTree FolderTree => folderTree;

    internal void SetColorTheme(string mode)
    {
        colorTheme = SessionStore.NormalizeColorTheme(mode);
        Theme.Set(colorTheme);
        ApplyAppearance();
        ScheduleSave();
    }

    private void RefreshThemeButton()
    {
        themeButton.Text = text.T(colorTheme == "dark" ? "Dark mode" : "Light mode");
        themeButton.ToolTipText = text.T(colorTheme == "dark" ? "Switch to light mode" : "Switch to dark mode");
        themeButton.Checked = colorTheme == "dark";
    }

    private void ApplyAppearance()
    {
        Theme.Apply(this);
        foreach (var pane in panes) pane.RefreshTheme();
        folderTree.RefreshTheme();
        previewPane.ApplyColors();
        previewWindow?.RefreshTheme();
        foreach (var (window, pane) in previewWindows)
        {
            Theme.Apply(window);
            pane.ApplyColors();
        }
        previewSplitter.BackColor = Theme.Border;
        RefreshThemeButton();
        for (int i = 0; i < panes.Length; i++) panes[i].SetActive(i == activePane);
    }

    // The other pane is the one used just before the active pane, or else the next visible pane.
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
        if (OtherPane() is not { ActiveView: { } destinationView } target || !Directory.Exists(destinationView.LocationPath))
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
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                if (error is not null) source.SetLocalizedStatus("Could not copy or move: {0}", error);
                else if (!done) source.SetLocalizedStatus("Copy or move was cancelled.");
                else source.SetLocalizedStatus(move ? "Moved {0} items to pane {1}." : "Copied {0} items to pane {1}.", items, number);
                FileOperationFinished?.Invoke(done, error);
            });
        });
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
        if (count == 0) previewPane.ShowMessage("Select a file to preview it.");
        else if (count > 1) previewPane.ShowMessage("{0} items selected", count);
        else if (path is null || Directory.Exists(path)) previewPane.ShowMessage("Folders are not previewed.");
        else previewPane.ShowFile(path);
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
        globalHotkey = enabled;
        hotkeyItem.Checked = enabled;
        if (IsHandleCreated)
        {
            UnregisterHotKey(Handle, HotkeyId);
            if (enabled && !RegisterHotKey(Handle, HotkeyId, 0x4000 | 0x0008 | 0x0001, (uint)Keys.E)) // NOREPEAT|WIN|ALT
                Active.SetLocalizedStatus("Win+Alt+E is already used by another program.");
        }
        ScheduleSave();
    }

    internal bool TrayIconVisible => trayIcon.Visible;
    internal string VersionText => versionLabel.Text;
    internal bool UpdateLinkShown => versionLabel.LinkArea.Length > 0;

    internal bool AutoUpdateEnabled => autoUpdate;

    internal void SetAutoUpdate(bool enabled)
    {
        autoUpdate = enabled;
        autoUpdateItem.Checked = enabled;
        UpdateVersionText();
        ScheduleSave();
    }

    private bool CanInstallUpdate(ReleaseInfo update) => autoUpdate && update.PackageUrl is not null && update.PackageSha256 is not null;

    private void OpenReleasePage(ReleaseInfo update)
    {
        try { Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true }); }
        catch (Win32Exception error) { Active.SetStatus(error.Message); }
    }

    // The new process waits for this one to exit before it takes the single-instance lock and the search index.
    private async Task InstallUpdateAsync(ReleaseInfo update, string exe)
    {
        updating = true;
        UpdateVersionText();
        try { await UpdateInstaller.InstallAsync(update, exe, UpdateInstaller.DownloadAsync, updateCancellation.Token); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException
            or UnauthorizedAccessException or InvalidDataException)
        {
            updating = false;
            UpdateVersionText();
            Active.SetLocalizedStatus("Automatic update failed: {0}", error.Message);
            OpenReleasePage(update);
            return;
        }
        saveTimer.Stop();
        SaveSession();
        var restart = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        restart.ArgumentList.Add("--wait-pid");
        restart.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!sessionPath.Equals(SessionStore.DefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            restart.ArgumentList.Add("--session");
            restart.ArgumentList.Add(sessionPath);
        }
        try { Process.Start(restart); }
        catch (Win32Exception)
        {
            updating = false;
            availableUpdate = null;
            UpdateVersionText();
            Active.SetLocalizedStatus("Updated. Restart Folder Splitter to finish.");
            return;
        }
        ExitApplication();
    }

    internal void SetCheckUpdates(bool enabled)
    {
        checkUpdates = enabled;
        updateItem.Checked = enabled;
        updateTimer.Enabled = enabled && latestRelease is not null;
        if (!enabled) availableUpdate = null;
        UpdateVersionText();
        if (enabled) _ = CheckForUpdateAsync();
        ScheduleSave();
    }

    // Offline, blocked or rate-limited networks simply leave the version label unchanged.
    internal async Task CheckForUpdateAsync()
    {
        if (!checkUpdates || latestRelease is null || updateCancellation.IsCancellationRequested) return;
        ReleaseInfo? latest;
        try { latest = await latestRelease(updateCancellation.Token); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException
            or System.Text.Json.JsonException or IOException or InvalidOperationException or KeyNotFoundException) { return; }
        if (IsDisposed || !checkUpdates) return;
        availableUpdate = latest is not null && latest.Version > UpdateChecker.Current ? latest : null;
        UpdateVersionText();
    }

    private void UpdateVersionText()
    {
        string current = "v" + UpdateChecker.Current.ToString(3);
        if (availableUpdate is null)
        {
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
    }
    internal bool TrayNoticeShown => trayNoticeShown;

    internal void SetCloseToTray(bool enabled)
    {
        closeToTray = enabled;
        trayItem.Checked = enabled;
        trayIcon.Visible = enabled;
        ScheduleSave();
    }

    internal void ExitApplication()
    {
        exiting = true;
        Close();
    }

    private void SetWinEShortcut(bool enabled)
    {
        try
        {
            if (enabled) winE.Enable();
            else winE.Disable();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Active.SetLocalizedStatus("Could not change the Win+E setting: {0}", error.Message);
        }
        winEItem.Checked = winE.IsEnabled;
    }

    // Win+Alt+E and Win+E bring this window forward and put it away when it is already in front.
    internal void ToggleFromHotkey()
    {
        if (Visible && WindowState != FormWindowState.Minimized && GetForegroundWindow() == Handle) DismissFromHotkey();
        else Summon();
    }

    // Hiding keeps the app (and the hotkey) running in the notification area; without it, minimize instead.
    internal void DismissFromHotkey()
    {
        if (closeToTray)
        {
            saveTimer.Stop();
            SaveSession();
            Hide();
        }
        else WindowState = FormWindowState.Minimized;
    }

    internal void Summon()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) ShowWindow(Handle, 9); // SW_RESTORE keeps a maximized window maximized
        Activate();
        SetForegroundWindow(Handle);
        Active.FocusFiles();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (globalHotkey) RegisterHotKey(Handle, HotkeyId, 0x4000 | 0x0008 | 0x0001, (uint)Keys.E);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam == HotkeyId) { ToggleFromHotkey(); return; } // WM_HOTKEY
        if (activationMessage != 0 && m.Msg == (int)activationMessage) // wParam 1: Win+E toggles like Win+Alt+E
        {
            if (m.WParam == 1) ToggleFromHotkey();
            else Summon();
            return;
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
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
        places.Height = (int)Math.Round(Math.Clamp(height / 4, 108, 216) * scale);
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
            case FolderTreeTarget.NewTab: Active.AddTab(path); break;
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
        ReloadPlaces();
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
        ReloadPlaces();
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
        using var background = new SolidBrush(selected ? Theme.Selected : Theme.Canvas);
        e.Graphics.FillRectangle(background, e.Bounds);
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
        e.DrawFocusRectangle();
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
        ReloadPlaces();
        Active.SetLocalizedStatus("Added to favorites.");
        ScheduleSave();
    }

    private void MoveTab(ExplorerPane source, string path, int number)
    {
        if (source.ActiveView is not { } tab) return;
        if (!visiblePanes.Contains(number - 1)) ApplyLayout(Math.Max(visiblePanes.Length, number));
        DockTab(source, tab, number - 1, null);
    }

    internal void ApplyLayout(int count, bool splitFirst = true, bool vertical = true)
    {
        RenderLayout(PaneLayout.Preset(count, splitFirst, vertical));
    }

    private void RenderLayout(PaneLayout tree)
    {
        rebuilding = true;
        paneHost.SuspendLayout();
        try
        {
            layoutTree = tree;
            visiblePanes = tree.GetPanes().Order().ToArray();
            foreach (var pane in panes)
            {
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
        MinimumSize = new Size(Math.Max(900, minimum.Width + (sidebar.Visible ? sidebar.Width : 0) + 8 + Width - ClientSize.Width),
            Math.Max(600, minimum.Height + toolbar.Height + versionLabel.Height + 12 + Height - ClientSize.Height));
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

    private void StartTabDrag(ExplorerPane source, ShellViewHost tab)
    {
        if (tabDragging || source.IndexOfTab(tab) < 0) return;
        source.SelectTab(source.IndexOfTab(tab), focus: false);
        ActivatePane(source.PaneNumber - 1);
        Rectangle bounds = paneHost.RectangleToScreen(paneHost.ClientRectangle);
        Rectangle[] regions = visiblePanes.Select(i => panes[i].RectangleToScreen(panes[i].ClientRectangle)).ToArray();
        using var overlay = new TabDockOverlay(bounds, regions, point => FindDropPlan(point, source, tab), text);
        var data = new DataObject();
        data.SetData(TabDockOverlay.DragFormat, false, "tab");
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
        if (effect == DragDropEffects.Move && overlay.Result is { } drop && !IsDisposed)
            DockTab(source, tab, drop.TargetPane, drop.Edge);
    }

    private TabDropPlan? FindDropPlan(Point screen, ExplorerPane source, ShellViewHost tab)
    {
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
            bool allowed = CanDockTab(source, tab, target, edge, out string reason);
            string label = allowed ? edge is { } side ? text.T($"Split {side.ToString().ToLowerInvariant()}") : text.T("Move tab to pane {0}", target + 1) : text.T(reason);
            return new TabDropPlan(target, edge, preview, label, allowed);
        }
        return null;
    }

    private bool CanDockTab(ExplorerPane source, ShellViewHost tab, int target, SplitEdge? edge, out string reason)
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

    internal bool DockTab(ExplorerPane source, ShellViewHost tab, int target, SplitEdge? edge)
    {
        if (!CanDockTab(source, tab, target, edge, out string reason))
        { source.SetLocalizedStatus(() => text.T(reason).Replace("\n", " ")); return false; }
        int sourceId = source.PaneNumber - 1;
        if (edge is { } side && sourceId != target && source.TabCount == 1)
        {
            RenderLayout(RemovePane(layoutTree, sourceId)!.Split(target, sourceId, side));
            ActivatePane(sourceId);
            source.FocusFiles();
            return true;
        }

        string path = tab.LocationPath;
        int destination = target;
        if (edge is { } splitEdge)
        {
            destination = Enumerable.Range(0, 4).First(i => !visiblePanes.Contains(i));
            RenderLayout(layoutTree.Split(target, destination, splitEdge));
        }
        var receiving = panes[destination];
        receiving.AddTab(path);
        bool emptyPane = source.TabCount == 1;
        source.CloseTab(source.IndexOfTab(tab));
        if (emptyPane && edge is null) RenderLayout(RemovePane(layoutTree, sourceId)!);
        ActivatePane(destination);
        receiving.FocusFiles();
        ScheduleSave();
        return true;

        static PaneLayout? RemovePane(PaneLayout node, int id)
        {
            if (node.IsLeaf) return node.Pane == id ? null : node;
            var first = RemovePane(node.First!, id);
            var second = RemovePane(node.Second!, id);
            if (first is null) return second;
            if (second is null) return first;
            return node with { First = first, Second = second };
        }
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
            if (key != (Keys.Alt | Keys.P) &&
                (previewWindow?.ContainsFocus == true || ShellNative.IsChild(previewPane.Handle, m.HWnd))) return false;
            if (HandleShortcut(key)) return true;
        }
        if (m.Msg >= 0x100 && m.Msg <= 0x109) return Active.ActiveView?.TranslateKey(ref m) ?? false;
        return false;
    }

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
                    UpdateWindow(drag.List);
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
    private static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint window);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    // Memory inside Windows' hosted file list grows with every folder visited and is only returned when the
    // process ends. Once it grew past the chosen amount, restart quietly while the window is out of the way.
    internal static bool ShouldRestartForMemory(long growth, int thresholdMb, TimeSpan uptime, bool unobtrusive) =>
        thresholdMb > 0 && uptime >= TimeSpan.FromMinutes(30) && unobtrusive && growth > thresholdMb * 1024L * 1024L;

    private void CheckMemory()
    {
        if (restarting || updating || autoRestartMemory == 0) return;
        long current;
        using (var process = Process.GetCurrentProcess()) current = process.PrivateMemorySize64;
        var uptime = DateTime.UtcNow - startedAt;
        if (memoryBaseline == 0)
        {
            // The search index fills memory once at startup; measure growth from after that.
            if (uptime >= TimeSpan.FromMinutes(5) && search?.IsReady != false) memoryBaseline = current;
            return;
        }
        bool unobtrusive = (!Visible || WindowState == FormWindowState.Minimized) && previewWindows.Count == 0
            && !tabDragging && columnDrag is null && OwnedForms.All(form => !form.Visible || form == previewWindow);
        if (ShouldRestartForMemory(current - memoryBaseline, autoRestartMemory, uptime, unobtrusive)) RestartApplication();
    }

    internal int AutoRestartMemory => autoRestartMemory;

    internal void SetAutoRestartMemory(int megabytes)
    {
        autoRestartMemory = megabytes is 0 or 100 or 200 or 300 or 500 ? megabytes : 100;
        ScheduleSave();
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
        if (restarting || updating) return;
        var arguments = new List<string> { "--wait-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (!sessionPath.Equals(SessionStore.DefaultPath, StringComparison.OrdinalIgnoreCase)) arguments.AddRange(["--session", sessionPath]);
        if (!Visible) arguments.Add("--hidden");
        else if (WindowState == FormWindowState.Minimized) arguments.Add("--minimized");
        saveTimer.Stop();
        SaveSession();
        bool started;
        if (RestartLauncher is { } launch) started = launch(arguments);
        else
        {
            try
            {
                string exe = Environment.ProcessPath!;
                var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
                foreach (string argument in arguments) info.ArgumentList.Add(argument);
                started = Process.Start(info) is not null;
            }
            catch (Win32Exception) { started = false; }
        }
        if (!started) { Active.SetLocalizedStatus("Could not restart Folder Splitter."); return; }
        restarting = true;
        ExitApplication();
    }

    private nint lastColumnHeader;

    private bool IsColumnHeader(nint window)
    {
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


    private bool HandleShortcut(Keys key)
    {
        switch (key)
        {
            case Keys.Control | Keys.T: Active.AddBlankTab(); break;
            case Keys.Control | Keys.Shift | Keys.C: Active.CopySelectionPaths(); break;
            case Keys.Control | Keys.Alt | Keys.C: TransferSelection(move: false); break;
            case Keys.Control | Keys.Alt | Keys.X: TransferSelection(move: true); break;
            case Keys.Control | Keys.W: Active.CloseTab(); break;
            case Keys.Control | Keys.Shift | Keys.T: Active.ReopenTab(); break;
            case Keys.Control | Keys.L:
            case Keys.Alt | Keys.D: Active.FocusAddress(); break;
            case Keys.Control | Keys.D: AddFavorite(Active.CurrentPath); break;
            case Keys.Control | Keys.Shift | Keys.E: SetFolderTreeVisible(!showFolderTree); break;
            case Keys.Alt | Keys.P: SetPreviewVisible(!showPreview); break;
            case Keys.Control | Keys.Tab: if (Active.TabCount > 0) Active.SelectTab((Active.SelectedIndex + 1) % Active.TabCount); break;
            case Keys.Control | Keys.Shift | Keys.Tab: if (Active.TabCount > 0) Active.SelectTab((Active.SelectedIndex + Active.TabCount - 1) % Active.TabCount); break;
            case Keys.Control | Keys.D1: ApplyLayout(1); break;
            case Keys.Control | Keys.D2: ApplyLayout(2); break;
            case Keys.Control | Keys.D3: ApplyLayout(3); break;
            case Keys.Control | Keys.Shift | Keys.D3: ApplyLayout(3, false); break;
            case Keys.Control | Keys.D4: ApplyLayout(4); break;
            case Keys.F6: ActivatePane(visiblePanes[(Array.IndexOf(visiblePanes, activePane) + 1) % visiblePanes.Length]); Active.FocusFiles(); break;
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
        if (!ready || saving || rebuilding) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void SaveSession()
    {
        if (!ready) return;
        saving = true;
        try
        {
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            SessionStore.Save(sessionPath, new SessionState
            {
                Layout = visiblePanes.Length, LayoutTree = layoutTree,
                Language = text.Language,
                ColorTheme = colorTheme, ShowFolderTree = showFolderTree, FolderTreeHeight = folderTreeHeight,
                GlobalHotkey = globalHotkey, KeepInTray = closeToTray, ShowPreview = showPreview, PreviewWidth = previewWidth, TrayNoticeShown = trayNoticeShown,
                CheckUpdates = checkUpdates, AutoUpdate = autoUpdate,
                ActivePane = activePane, Panes = panes.Select(p => p.CaptureState()).ToList(),
                Favorites = [.. favorites], ShowSidebar = sidebar.Visible, Workspaces = [.. workspaces],
                FavoritesExpanded = favoritesExpanded,
                SidebarWidth = sidebarWidth, DeletedFavorites = [.. deletedFavorites],
                ExplorerFavoritesImported = explorerFavoritesImported,
                WindowWidth = bounds.Width, WindowHeight = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized,
                ColumnRatio = layoutTree.Ratio, AutoRestartMemory = autoRestartMemory,
                LeftRowRatio = layoutTree.First?.Ratio ?? .5, RightRowRatio = layoutTree.Second?.Ratio ?? .5
            });
            saveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveError = ex.Message;
            Active.SetLocalizedStatus("Session could not be saved: {0}", ex.Message);
        }
        finally { saving = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        saveTimer.Stop();
        SaveSession();
        // Closing the window keeps the app (and Win+Alt+E) alive in the notification area when enabled.
        if (closeToTray && !exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!trayNoticeShown)
            {
                trayNoticeShown = true;
                trayIcon.ShowBalloonTip(8000, "Folder Splitter", text.T("Folder Splitter keeps running in the notification area. Press Win+Alt+E or click its icon to reopen it; right-click the icon and choose Exit to quit. You can turn this off in Settings."), ToolTipIcon.Info);
                SaveSession();
            }
            base.OnFormClosing(e);
            return;
        }
        if (saveError is not null && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = MessageBox.Show(this, text.T("Your session could not be saved:\n\n{0}\n\nClose anyway?", saveError),
                "Folder Splitter", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes;
        }
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
            Application.RemoveMessageFilter(this);
            saveTimer.Dispose();
            previewTimer.Dispose();
            memoryTimer.Dispose();
            tips.Dispose();
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();
            updateCancellation.Cancel();
            updateTimer.Dispose();
            places.ContextMenuStrip?.Dispose();
            favoritePlaces.ContextMenuStrip?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var pane in panes) if (!pane.IsDisposed) pane.Dispose();
            search?.Dispose();
            placeIcons.Dispose();
            appIcon.Dispose();
        }
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            text.Help, text.T("Folder Splitter shortcuts"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
