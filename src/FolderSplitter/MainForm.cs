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
    private readonly Button treeHeader;
    private readonly Panel treePanel = new() { Dock = DockStyle.Bottom };
    private readonly Splitter treeSplitter = new()
    {
        Dock = DockStyle.Bottom, Height = 6, Cursor = Cursors.HSplit, AccessibleName = "Resize favorites and folder tree"
    };
    private int folderTreeHeight;
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
    private readonly Label versionLabel = new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Muted, BackColor = Theme.Canvas, Padding = new Padding(12, 4, 0, 4),
        AccessibleName = "Application version"
    };
    private readonly ToolStripButton[] layoutButtons = new ToolStripButton[4];
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
    private readonly ToolTip tips = new() { ShowAlways = true, AutoPopDelay = 10000 };
    private int activePane;
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
        IReadOnlyList<string>? treeRoots = null)
    {
        this.search = search;
        this.sessionPath = sessionPath;
        initialState = SessionStore.Normalize(SessionStore.Load(sessionPath));
        colorTheme = initialState.ColorTheme;
        showFolderTree = initialState.ShowFolderTree;
        folderTreeHeight = initialState.FolderTreeHeight;
        Theme.Set(colorTheme);
        text = new UiText(initialState.Language);
        layoutTree = initialState.LayoutTree!;
        visiblePanes = layoutTree.GetPanes().Order().ToArray();
        activePane = initialState.ActivePane;
        favorites = [.. initialState.Favorites];
        deletedFavorites = [.. initialState.DeletedFavorites];
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
        versionLabel.Text = "v" + typeof(MainForm).Assembly.GetName().Version!.ToString(3);
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("QuadExplorer.AppIcon")!)
        using (var sourceIcon = new Icon(iconStream))
            appIcon = (Icon)sourceIcon.Clone();
        Icon = appIcon;

        panes = Enumerable.Range(0, 4).Select(i => new ExplorerPane(i + 1, initialState.Panes[i], text,
            search, SearchRoots)).ToArray();
        foreach (var pane in panes)
        {
            pane.Activated += (_, _) => ActivatePane(Array.IndexOf(panes, pane));
            pane.StateChanged += (_, _) =>
            {
                ScheduleSave();
                if (ready && ReferenceEquals(pane, Active)) SyncTree();
            };
            pane.FavoriteRequested += (_, path) => AddFavorite(path);
            pane.MoveTabRequested += MoveTab;
            pane.TabDragRequested += StartTabDrag;
            pane.SplitTabRequested += (source, tab, edge) => DockTab(source, tab, source.PaneNumber - 1, edge);
        }

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
        workspace.Controls.Add(sidebarSplitter);
        workspace.Controls.Add(sidebar);
        Controls.Add(workspace);
        Controls.Add(versionLabel);
        Controls.Add(toolbar);
        sidebar.Visible = initialState.ShowSidebar;
        sidebarSplitter.Visible = initialState.ShowSidebar;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSession(); };
        Shown += (_, _) =>
        {
            if (initialState.Maximized) WindowState = FormWindowState.Maximized;
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
        RefreshThemeButton();
        for (int i = 0; i < panes.Length; i++) panes[i].SetActive(i == activePane);
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
        activePane = index;
        for (int i = 0; i < panes.Length; i++) panes[i].SetActive(i == index);
        if (ready) SyncTree();
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
        if (!ContainsFocus || tabDragging) return false;
        if (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x100 || m.Msg == 0x104)
        {
            foreach (int i in visiblePanes)
                if (panes[i].IsHandleCreated && (m.HWnd == panes[i].Handle || ShellNative.IsChild(panes[i].Handle, m.HWnd)))
                { if (i != activePane) ActivatePane(i); break; }
        }
        if (m.Msg is 0x100 or 0x104)
        {
            Keys key = (Keys)(int)m.WParam | ModifierKeys;
            if (HandleShortcut(key)) return true;
        }
        if (m.Msg >= 0x100 && m.Msg <= 0x109) return Active.ActiveView?.TranslateKey(ref m) ?? false;
        return false;
    }

    private bool HandleShortcut(Keys key)
    {
        switch (key)
        {
            case Keys.Control | Keys.T: Active.AddTab(Active.CurrentPath); break;
            case Keys.Control | Keys.W: Active.CloseTab(); break;
            case Keys.Control | Keys.Shift | Keys.T: Active.ReopenTab(); break;
            case Keys.Control | Keys.L:
            case Keys.Alt | Keys.D: Active.FocusAddress(); break;
            case Keys.Control | Keys.D: AddFavorite(Active.CurrentPath); break;
            case Keys.Control | Keys.Shift | Keys.E: SetFolderTreeVisible(!showFolderTree); break;
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
                ActivePane = activePane, Panes = panes.Select(p => p.CaptureState()).ToList(),
                Favorites = [.. favorites], ShowSidebar = sidebar.Visible,
                FavoritesExpanded = favoritesExpanded,
                SidebarWidth = sidebarWidth, DeletedFavorites = [.. deletedFavorites],
                ExplorerFavoritesImported = explorerFavoritesImported,
                WindowWidth = bounds.Width, WindowHeight = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized,
                ColumnRatio = layoutTree.Ratio,
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
            Application.RemoveMessageFilter(this);
            saveTimer.Dispose();
            tips.Dispose();
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
