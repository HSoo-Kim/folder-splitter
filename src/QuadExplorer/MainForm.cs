using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed class MainForm : Form, IMessageFilter
{
    private readonly string sessionPath;
    private readonly SessionState initialState;
    private readonly ExplorerPane[] panes;
    private readonly SplitContainer columns;
    private readonly SplitContainer leftRows;
    private readonly SplitContainer rightRows;
    private readonly Panel sidebar;
    private readonly ListBox places;
    private readonly List<string> favorites;
    private readonly ToolStrip toolbar = Theme.Strip();
    private readonly ToolStripButton[] layoutButtons = new ToolStripButton[3];
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
    private int activePane;
    private int layout;
    private bool ready;
    private bool saving;
    private string? saveError;
    private readonly record struct Place(string Label, string Path, bool Favorite = false)
    {
        public override string ToString() => Label;
    }

    public MainForm(string sessionPath, IReadOnlyList<string> folders)
    {
        this.sessionPath = sessionPath;
        initialState = SessionStore.Normalize(SessionStore.Load(sessionPath));
        favorites = [.. initialState.Favorites];
        Text = "QuadExplorer";
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        MinimumSize = new Size(900, 600);
        Size = new Size(initialState.WindowWidth, initialState.WindowHeight);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        Icon = SystemIcons.Application;

        panes = Enumerable.Range(0, 4).Select(i => new ExplorerPane(i + 1, initialState.Panes[i])).ToArray();
        foreach (var pane in panes)
        {
            pane.Activated += (_, _) => ActivatePane(Array.IndexOf(panes, pane));
            pane.StateChanged += (_, _) => ScheduleSave();
            pane.FavoriteRequested += (_, path) => AddFavorite(path);
            pane.MoveTabRequested += MoveTab;
        }
        columns = Split(Orientation.Vertical);
        leftRows = Split(Orientation.Horizontal);
        rightRows = Split(Orientation.Horizontal);
        leftRows.Panel1.Controls.Add(panes[0]);
        leftRows.Panel2.Controls.Add(panes[2]);
        rightRows.Panel1.Controls.Add(panes[1]);
        rightRows.Panel2.Controls.Add(panes[3]);
        columns.Panel1.Controls.Add(leftRows);
        columns.Panel2.Controls.Add(rightRows);
        foreach (var split in new[] { columns, leftRows, rightRows })
        {
            split.SplitterMoved += (_, _) => ScheduleSave();
            split.DoubleClick += (_, _) => EqualizeSplits();
        }

        sidebar = new Panel { Dock = DockStyle.Left, Width = 172, Padding = new Padding(10, 12, 8, 8), BackColor = Theme.Canvas };
        var sideTitle = new Label
        {
            Text = "QUICK ACCESS", Dock = DockStyle.Top, Height = 28,
            Font = new Font(Font.FontFamily, 8, FontStyle.Bold), ForeColor = Theme.Muted,
            Padding = new Padding(7, 0, 0, 0)
        };
        places = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Canvas,
            ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36,
            IntegralHeight = false, AccessibleName = "Quick access and favorites"
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
            { favorites.Remove(place.Path); ReloadPlaces(); ScheduleSave(); }
        });
        placeMenu.Opening += (_, _) => placeMenu.Items[1].Enabled = places.SelectedItem is Place { Favorite: true };
        places.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) places.SelectedIndex = places.IndexFromPoint(e.Location); };
        places.ContextMenuStrip = placeMenu;
        var pin = Theme.Button("+  Pin folder", "Pin current folder to favorites", () => AddFavorite(Active.CurrentPath), 152);
        pin.Dock = DockStyle.Bottom;
        pin.Height = 36;
        pin.BackColor = Theme.Canvas;
        sidebar.Controls.Add(places);
        sidebar.Controls.Add(pin);
        sidebar.Controls.Add(sideTitle);
        ReloadPlaces();

        BuildToolbar();
        var workspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 8) };
        workspace.Controls.Add(columns);
        workspace.Controls.Add(sidebar);
        Controls.Add(workspace);
        Controls.Add(toolbar);
        sidebar.Visible = initialState.ShowSidebar;
        layout = initialState.Layout;
        activePane = initialState.ActivePane;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSession(); };
        Shown += (_, _) =>
        {
            ApplyLayout(layout);
            SetSplitRatio(columns, initialState.ColumnRatio);
            SetSplitRatio(leftRows, initialState.LeftRowRatio);
            SetSplitRatio(rightRows, initialState.RightRowRatio);
            if (initialState.Maximized) WindowState = FormWindowState.Maximized;
            ready = true;
            ActivatePane(activePane);
            foreach (string folder in folders) Active.AddTab(folder);
            Active.FocusFiles();
        };
        ResizeEnd += (_, _) => ScheduleSave();
        Application.AddMessageFilter(this);
    }

    private ExplorerPane Active => panes[activePane];

    private static SplitContainer Split(Orientation orientation) => new()
    {
        Size = new Size(700, 500),
        Dock = DockStyle.Fill, Orientation = orientation, SplitterWidth = 6,
        BackColor = Theme.Canvas, Panel1MinSize = 100, Panel2MinSize = 100,
    };

    private void BuildToolbar()
    {
        toolbar.Height = 48;
        toolbar.Padding = new Padding(10, 7, 10, 7);
        var brand = new ToolStripLabel("QuadExplorer") { Font = new Font(Font, FontStyle.Bold), Margin = new Padding(5, 0, 18, 0) };
        toolbar.Items.Add(brand);
        var sidebarButton = new ToolStripButton("Sidebar") { CheckOnClick = true, Checked = initialState.ShowSidebar, ToolTipText = "Show or hide quick access" };
        sidebarButton.Click += (_, _) => { sidebar.Visible = sidebarButton.Checked; ScheduleSave(); };
        toolbar.Items.Add(sidebarButton);
        toolbar.Items.Add(new ToolStripSeparator());
        int[] counts = [1, 2, 4];
        for (int i = 0; i < counts.Length; i++)
        {
            int count = counts[i];
            var button = new ToolStripButton($"{count} pane{(count == 1 ? "" : "s")}")
            {
                ToolTipText = $"{count} pane layout (Ctrl+{count})", AutoSize = false, Width = 66,
                AccessibleName = $"{count} pane layout"
            };
            button.Click += (_, _) => ApplyLayout(count);
            layoutButtons[i] = button;
            toolbar.Items.Add(button);
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
        foreach (string path in favorites) places.Items.Add(new Place(ShellViewHost.TitleFor(path), path, true));
    }

    private void DrawPlace(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = e.State.HasFlag(DrawItemState.Selected);
        using var background = new SolidBrush(selected ? Theme.Selected : Theme.Canvas);
        e.Graphics.FillRectangle(background, e.Bounds);
        var place = (Place)places.Items[e.Index];
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
        if (favorites.Contains(path, StringComparer.OrdinalIgnoreCase)) { Active.SetStatus("Already in favorites."); return; }
        if (favorites.Count >= 100) { Active.SetStatus("Maximum 100 favorites."); return; }
        favorites.Add(path);
        ReloadPlaces();
        Active.SetStatus("Added to favorites.");
        ScheduleSave();
    }

    private void MoveTab(ExplorerPane source, string path, int number)
    {
        var target = panes[number - 1];
        if (target.TabCount >= 32) { source.SetStatus("Destination has 32 tabs. Close one first."); return; }
        if (number > layout) ApplyLayout(number > 2 ? 4 : 2);
        target.AddTab(path);
        source.CloseTab();
        ActivatePane(number - 1);
        target.FocusFiles();
    }

    private void ApplyLayout(int count)
    {
        layout = count;
        SuspendLayout();
        columns.Panel2Collapsed = count == 1;
        leftRows.Panel2Collapsed = count != 4;
        rightRows.Panel2Collapsed = count != 4;
        for (int i = 0; i < 3; i++) layoutButtons[i].Checked = count == new[] { 1, 2, 4 }[i];
        if (activePane >= count) activePane = 0;
        ResumeLayout(true);
        ActivatePane(activePane);
        ScheduleSave();
    }

    private void ActivatePane(int index)
    {
        if (index < 0 || index >= layout) return;
        activePane = index;
        for (int i = 0; i < panes.Length; i++) panes[i].SetActive(i == index);
        ScheduleSave();
    }

    private void EqualizeSplits()
    {
        foreach (var split in new[] { columns, leftRows, rightRows }) SetSplitRatio(split, .5);
        ScheduleSave();
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
        if (!ContainsFocus) return false;
        if (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x100 || m.Msg == 0x104)
        {
            for (int i = 0; i < layout; i++)
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
            case Keys.Control | Keys.Tab: Active.SelectTab((Active.SelectedIndex + 1) % Active.TabCount); break;
            case Keys.Control | Keys.Shift | Keys.Tab: Active.SelectTab((Active.SelectedIndex + Active.TabCount - 1) % Active.TabCount); break;
            case Keys.Control | Keys.D1: ApplyLayout(1); break;
            case Keys.Control | Keys.D2: ApplyLayout(2); break;
            case Keys.Control | Keys.D4: ApplyLayout(4); break;
            case Keys.F6: ActivatePane((activePane + 1) % layout); Active.FocusFiles(); break;
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
        if (!ready || saving) return;
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
                Layout = layout, ActivePane = activePane, Panes = panes.Select(p => p.CaptureState()).ToList(),
                Favorites = [.. favorites], ShowSidebar = sidebar.Visible,
                WindowWidth = bounds.Width, WindowHeight = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized,
                ColumnRatio = SplitRatio(columns), LeftRowRatio = SplitRatio(leftRows), RightRowRatio = SplitRatio(rightRows)
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
                "QuadExplorer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            saveTimer.Dispose();
            places.ContextMenuStrip?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "LAYOUT\nCtrl+1 / Ctrl+2 / Ctrl+4   One, two or four panes\nF6   Next pane\nDrag dividers to resize; Equal size resets them.\n\n" +
            "TABS\nCtrl+T   New tab at current folder\nCtrl+W / middle-click   Close tab\nCtrl+Shift+T   Reopen closed tab\nCtrl+Tab / Ctrl+Shift+Tab   Switch tabs\nRight-click tab   Duplicate, move, close others\n\n" +
            "NAVIGATION\nCtrl+L / Alt+D   Address bar\nAlt+Left / Right / Up   Back, forward, parent\nF5   Refresh\nCtrl+D   Pin current folder\n\n" +
            "FILES\nUse Windows context menus and drag files between panes.\nCtrl+C / Ctrl+X / Ctrl+V   Copy, cut, paste\nF2   Rename    |    Delete   Recycle\nCtrl+Shift+N   New folder\n\n" +
            "Tabs, favorites and layout are saved locally.\nPane options include view modes and Open in File Explorer.",
            "QuadExplorer shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
