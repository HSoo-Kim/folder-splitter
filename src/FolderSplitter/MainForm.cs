using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed class MainForm : Form, IMessageFilter
{
    private readonly string sessionPath;
    private readonly Icon appIcon;
    private readonly SessionState initialState;
    private readonly ExplorerPane[] panes;
    private readonly Panel paneHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
    private readonly List<(SplitContainer Control, string Path)> splits = [];
    private PaneLayout layoutTree;
    private int[] visiblePanes;
    private readonly Panel sidebar;
    private readonly Splitter sidebarSplitter;
    private readonly ToolStripButton sidebarButton = new("Sidebar");
    private int sidebarWidth;
    private readonly List<string> deletedFavorites;
    private bool explorerFavoritesImported;
    private readonly ListBox places;
    private readonly ListBox favoritePlaces;
    private readonly Button favoritesHeader;
    private bool favoritesExpanded;
    private readonly List<string> favorites;
    private readonly ToolStrip toolbar = Theme.Strip();
    private readonly ToolStripButton[] layoutButtons = new ToolStripButton[4];
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
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

    public MainForm(string sessionPath, IReadOnlyList<string> folders)
    {
        this.sessionPath = sessionPath;
        initialState = SessionStore.Normalize(SessionStore.Load(sessionPath));
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
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("QuadExplorer.AppIcon")!)
        using (var sourceIcon = new Icon(iconStream))
            appIcon = (Icon)sourceIcon.Clone();
        Icon = appIcon;

        panes = Enumerable.Range(0, 4).Select(i => new ExplorerPane(i + 1, initialState.Panes[i])).ToArray();
        foreach (var pane in panes)
        {
            pane.Activated += (_, _) => ActivatePane(Array.IndexOf(panes, pane));
            pane.StateChanged += (_, _) => ScheduleSave();
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
        placeMenu.Items.Add("Open in new tab", null, (_, _) =>
        {
            if (places.SelectedItem is Place place) Active.AddTab(place.Path);
        });
        placeMenu.Items.Add("Remove favorite", null, (_, _) =>
        {
            if (places.SelectedItem is Place { Favorite: true } place)
            RemoveFavorite(place.Path);
        });
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
        favoriteMenu.Items.Add("Open in new tab", null, (_, _) =>
        { if (favoritePlaces.SelectedItem is Place p) Active.AddTab(p.Path); });
        favoriteMenu.Items.Add("Remove favorite", null, (_, _) =>
        {
            if (favoritePlaces.SelectedItem is Place p)
            RemoveFavorite(p.Path);
        });
        favoriteMenu.Items.Add(new ToolStripSeparator());
        favoriteMenu.Items.Add("Import from File Explorer", null, (_, _) => ImportExplorerFavorites());
        favoriteMenu.Items.Add("Remove all favorites", null, (_, _) => ClearFavorites());
        favoriteMenu.Items.Add("Restore favorites...", null, (_, _) => ShowRestoreFavorites());
        favoritePlaces.ContextMenuStrip = favoriteMenu;
        favoritePlaces.MouseDown += (_, e) =>
        { if (e.Button == MouseButtons.Right) favoritePlaces.SelectedIndex = favoritePlaces.IndexFromPoint(e.Location); };
        favoritesHeader = new Button
        {
            Dock = DockStyle.Top, Height = 32, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted
        };
        favoritesHeader.FlatAppearance.BorderSize = 0;
        favoritesHeader.Click += (_, _) => ToggleFavorites();
        favoritesHeader.ContextMenuStrip = favoriteMenu;
        var manage = Theme.Button("Manage...", "Manage favorites", () => favoriteMenu.Show(Cursor.Position));
        manage.Dock = DockStyle.Bottom;
        manage.AutoSize = true;
        var pin = Theme.Button("+  Pin folder", "Pin current folder to favorites", () => AddFavorite(Active.CurrentPath), 152);
        pin.Dock = DockStyle.Bottom;
        pin.Height = 36;
        pin.BackColor = Theme.Canvas;
        sidebar.Controls.Add(favoritePlaces);
        sidebar.Controls.Add(favoritesHeader);
        sidebar.Controls.Add(places);
        sidebar.Controls.Add(pin);
        sidebar.Controls.Add(manage);
        sidebar.Controls.Add(sideTitle);
        ReloadPlaces();

        BuildToolbar();
        var workspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 8) };
        workspace.Controls.Add(paneHost);
        workspace.Controls.Add(sidebarSplitter);
        workspace.Controls.Add(sidebar);
        Controls.Add(workspace);
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
            if (!explorerFavoritesImported) ImportExplorerFavorites();
            ActivatePane(activePane);
            if (folders.Count > 0 && !File.Exists(sessionPath)) Active.CloseTab();
            foreach (string folder in folders) Active.AddTab(folder);
            Active.FocusFiles();
        };
        ResizeEnd += (_, _) => ScheduleSave();
        RenderLayout(layoutTree);
        Application.AddMessageFilter(this);
    }

    private ExplorerPane Active => panes[activePane];
    internal PaneLayout CurrentLayout => layoutTree;
    internal ExplorerPane GetPane(int id) => panes[id];

    private void BuildToolbar()
    {
        toolbar.Padding = new Padding(10, 7, 10, 7);
        var brand = new ToolStripLabel("Folder Splitter") { Font = new Font(Font, FontStyle.Bold), Margin = new Padding(5, 0, 18, 0) };
        toolbar.Items.Add(brand);
        sidebarButton.CheckOnClick = true;
        sidebarButton.Checked = initialState.ShowSidebar;
        sidebarButton.ToolTipText = "Show or hide quick access";
        sidebarButton.Click += (_, _) => SetSidebarVisible(sidebarButton.Checked);
        toolbar.Items.Add(sidebarButton);
        toolbar.Items.Add(new ToolStripSeparator());
        int[] counts = [1, 2, 3, 4];
        for (int i = 0; i < counts.Length; i++)
        {
            int count = counts[i];
            var button = new ToolStripButton($"{count} pane{(count == 1 ? "" : "s")}")
            {
                ToolTipText = $"{count} pane layout (Ctrl+{count})", AutoSize = true,
                AccessibleName = $"{count} pane layout"
            };
            button.Click += (_, _) => ApplyLayout(count);
            layoutButtons[i] = button;
            toolbar.Items.Add(button);
            if (count == 3)
            {
                var arrangements = new ToolStripDropDownButton("\u25BE")
                {
                    AccessibleName = "Three pane arrangements",
                    ToolTipText = "Choose the three-pane arrangement",
                    AutoSize = false, Width = 22
                };
                arrangements.DropDownItems.Add("Two left, one right", null, (_, _) => ApplyLayout(3));
                arrangements.DropDownItems.Add("One left, two right", null, (_, _) => ApplyLayout(3, false));
                arrangements.DropDownItems.Add("Two above, one below", null, (_, _) => ApplyLayout(3, true, false));
                arrangements.DropDownItems.Add("One above, two below", null, (_, _) => ApplyLayout(3, false, false));
                toolbar.Items.Add(arrangements);
            }
        }
        toolbar.Items.Add(new ToolStripSeparator());
        var equalize = new ToolStripButton("Equal size") { ToolTipText = "Reset split dividers to equal size" };
        equalize.Click += (_, _) => EqualizeSplits();
        toolbar.Items.Add(equalize);
        var help = new ToolStripButton("Shortcuts") { Alignment = ToolStripItemAlignment.Right };
        help.Click += (_, _) => ShowHelp();
        toolbar.Items.Add(help);
        var close = new ToolStripButton("Close tab") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Close active tab (Ctrl+W)" };
        close.Click += (_, _) => Active.CloseTab();
        toolbar.Items.Add(close);
    }

    private void ReloadPlaces()
    {
        places.Items.Clear();
        places.Items.Add(new Place("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        places.Items.Add(new Place("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        places.Items.Add(new Place("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
        places.Items.Add(new Place("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        places.Items.Add(new Place("This PC", ShellNative.ThisPc));
        places.Items.Add(new Place("Recycle Bin", ShellNative.RecycleBin));
        favoritePlaces.Items.Clear();
        foreach (string path in favorites) favoritePlaces.Items.Add(new Place(ShellViewHost.TitleFor(path), path, true));
        favoritePlaces.Visible = favoritesExpanded;
        favoritesHeader.Text = $"{(favoritesExpanded ? "\u25BE" : "\u25B8")} Favorites";
        favoritesHeader.AccessibleName = favoritesExpanded ? "Collapse favorites" : "Expand favorites";
    }

    internal bool FavoritesExpanded => favoritesExpanded;
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
        Active.SetStatus($"Imported {added} favorites from File Explorer."
            + (result.Warning is { } warning ? " " + warning : ""));
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
            Text = "Restore favorites", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(650, 430), MinimumSize = new Size(450, 300),
            Font = Font, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false
        };
        var list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true };
        foreach (string path in deletedFavorites)
            list.Items.Add(new Place($"{ShellViewHost.TitleFor(path)} — {path}", path));
        foreach (Place place in places.Items)
            if (!favorites.Contains(place.Path, StringComparer.OrdinalIgnoreCase)
                && !deletedFavorites.Contains(place.Path, StringComparer.OrdinalIgnoreCase))
                list.Items.Add(place);
        var restore = new Button { Text = "Restore selected", Dock = DockStyle.Bottom, AutoSize = true };
        restore.Click += (_, _) =>
        {
            foreach (Place item in list.SelectedItems.Cast<Place>().ToArray()) RestoreFavorite(item.Path);
            dialog.Close();
        };
        var label = new Label
        {
            Text = "Removed favorites and standard locations (including Recycle Bin). Select items to restore.",
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(600, 0)
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(restore);
        dialog.Controls.Add(label);
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
        var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, (place.Favorite ? "\u2606  " : "") + place.Label, Font, rect,
            selected ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    private void OpenPlace()
    {
        if (places.SelectedItem is Place place) Active.Navigate(place.Path);
    }

    private void AddFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (favorites.Contains(path, StringComparer.OrdinalIgnoreCase)) { Active.SetStatus("Already in favorites."); return; }
        if (favorites.Count >= 100) { Active.SetStatus("Maximum 100 favorites."); return; }
        favorites.Add(path);
        deletedFavorites.RemoveAll(p => StringComparer.OrdinalIgnoreCase.Equals(p, path));
        ReloadPlaces();
        Active.SetStatus("Added to favorites.");
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
            Math.Max(600, minimum.Height + toolbar.Height + 12 + Height - ClientSize.Height));
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
        using var overlay = new TabDockOverlay(bounds, regions, point => FindDropPlan(point, source, tab));
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
            string label = allowed ? edge is { } side ? $"Split {side.ToString().ToLowerInvariant()}" : $"Move tab to pane {target + 1}" : reason;
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
        { source.SetStatus(reason.Replace("\n", " ")); return false; }
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
            Active.SetStatus("Session could not be saved: " + ex.Message);
        }
        finally { saving = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        saveTimer.Stop();
        SaveSession();
        if (saveError is not null && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = MessageBox.Show(this, "Your session could not be saved:\n\n" + saveError + "\n\nClose anyway?",
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
            places.ContextMenuStrip?.Dispose();
            favoritePlaces.ContextMenuStrip?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var pane in panes) if (!pane.IsDisposed) pane.Dispose();
            appIcon.Dispose();
        }
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "LAYOUT\nCtrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4   Pane count\nCtrl+Shift+3   One left, two right\nF6   Next pane\nThe arrow beside 3 panes offers both 2:1 arrangements.\nDrag dividers to resize; Equal size resets them.\n\n" +
            "TABS\nDrag a tab to a file-list edge to split; center to move.\nA blue preview shows the destination. Esc cancels.\nRight-click tab > Split tab is the keyboard alternative.\nUnused panes remain empty until you open a folder.\nCtrl+T   New tab at current folder\nCtrl+W / middle-click   Close tab\nCtrl+Shift+T   Reopen closed tab\nCtrl+Tab / Ctrl+Shift+Tab   Switch tabs\n\n" +
            "NAVIGATION\nCtrl+L / Alt+D   Address bar\nAlt+Left / Right / Up   Back, forward, parent\nF5   Refresh\nCtrl+D   Pin current folder\n\n" +
            "FILES\nUse Windows context menus and drag files between panes.\nCtrl+C / Ctrl+X / Ctrl+V   Copy, cut, paste\nF2   Rename    |    Delete   Recycle\nCtrl+Shift+N   New folder\n\n" +
            "Tabs, favorites and layout are saved locally.\nPane options include view modes and Open in File Explorer.",
            "Folder Splitter shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
