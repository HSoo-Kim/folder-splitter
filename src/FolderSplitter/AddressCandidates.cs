using QuadExplorer.Search;

namespace QuadExplorer;

internal sealed class AddressCandidates : Form
{
    private readonly ListBox rows = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed, DisplayMember = nameof(SearchHit.FullPath)
    };
    private readonly ToolTip tips = new();
    private readonly object iconGate = new();
    private readonly Dictionary<SearchHit, Bitmap> icons = [];
    private readonly Func<SearchHit, Bitmap?> loadIcon;
    private SearchHit[] pendingIcons = [];
    private int iconGeneration;
    private bool iconWorkerRunning;
    private bool iconsDisposed;
    private Form? ownerForm;
    private string? hoveredPath;
    internal event Action<SearchHit>? Chosen;
    internal IReadOnlyList<SearchHit> Hits => rows.Items.OfType<SearchHit>().ToArray();
    internal SearchHit? SelectedHit => rows.SelectedItem as SearchHit;

    internal AddressCandidates(int pane, Func<SearchHit, Bitmap?>? loadIcon = null)
    {
        this.loadIcon = loadIcon ?? AddressShellIcons.Load;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Border;
        Padding = new Padding(1);
        AccessibleName = $"Pane {pane} search candidates";
        Text = AccessibleName;
        rows.AccessibleName = $"Pane {pane} search results";
        Controls.Add(rows);
        rows.DrawItem += DrawRow;
        rows.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            int index = rows.IndexFromPoint(e.Location);
            if (index >= 0 && rows.Items[index] is SearchHit hit) Chosen?.Invoke(hit);
        };
        rows.MouseMove += (_, e) =>
        {
            int index = rows.IndexFromPoint(e.Location);
            string? path = index >= 0 && rows.Items[index] is SearchHit hit ? hit.FullPath : null;
            if (path == hoveredPath) return;
            hoveredPath = path;
            tips.SetToolTip(rows, path);
        };
        RefreshTheme();
    }

    internal void RefreshTheme()
    {
        BackColor = Theme.Border;
        rows.BackColor = Theme.Surface;
        rows.ForeColor = Theme.Text;
        rows.Invalidate();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = 3; return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }

    internal void Present(TextBox address, IReadOnlyList<SearchHit> hits, string? message = null)
    {
        QueueIcons(message is null ? hits : []);
        rows.BeginUpdate();
        try
        {
            Font = address.Font;
            rows.Font = Font;
            rows.ItemHeight = Math.Max(Scale(42), Font.Height * 2 + Scale(10));
            rows.Items.Clear();
            rows.Enabled = message is null;
            if (message is not null) rows.Items.Add(message);
            else foreach (var hit in hits) rows.Items.Add(hit);
            if (rows.Items.Count > 0 && message is null) rows.SelectedIndex = 0;
        }
        finally { rows.EndUpdate(); }
        if (rows.Items.Count == 0) { Hide(); return; }
        Rectangle anchor = address.RectangleToScreen(address.ClientRectangle);
        Rectangle work = Screen.FromControl(address).WorkingArea;
        int width = Math.Min(work.Width, Math.Max(anchor.Width, 560 * address.DeviceDpi / 96));
        int height = Math.Min(rows.ItemHeight * Math.Min(8, rows.Items.Count) + 2, work.Height);
        int y = anchor.Bottom + 2;
        if (y + height > work.Bottom) y = Math.Max(work.Top, anchor.Top - height - 2);
        Bounds = new Rectangle(Math.Clamp(anchor.Left, work.Left, work.Right - width), y, width, height);
        if (ownerForm is null)
        {
            ownerForm = address.FindForm();
            if (ownerForm is not null)
            {
                ownerForm.Deactivate += HideOnOwnerChange;
                ownerForm.Move += HideOnOwnerChange;
                ownerForm.Resize += HideOnOwnerChange;
            }
        }
        if (!Visible) Show(ownerForm);
    }

    internal void MoveSelection(int delta)
    {
        if (rows.Enabled && rows.Items.Count > 0)
            rows.SelectedIndex = Math.Clamp(rows.SelectedIndex + delta, 0, rows.Items.Count - 1);
    }

    private void HideOnOwnerChange(object? sender, EventArgs e) => Hide();

    private int Scale(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        rows.ItemHeight = Math.Max(Scale(42), Font.Height * 2 + Scale(10));
        QueueIcons(Hits);
        rows.Invalidate();
    }

    private void QueueIcons(IReadOnlyList<SearchHit> hits)
    {
        lock (iconGate)
        {
            iconGeneration++;
            foreach (Bitmap image in icons.Values) image.Dispose();
            icons.Clear();
            // The address search backend supplies at most 50 results. Bound memory
            // even if another caller supplies an unexpectedly large result set.
            pendingIcons = hits.Take(200).Distinct().ToArray();
            if (iconsDisposed || iconWorkerRunning || pendingIcons.Length == 0) return;
            iconWorkerRunning = true;
            _ = Task.Run(LoadIcons);
        }
    }

    private void LoadIcons()
    {
        int com = AddressShellIcons.InitializeWorker();
        try { LoadIconsCore(); }
        finally { if (com >= 0) AddressShellIcons.UninitializeWorker(); }
    }

    private void LoadIconsCore()
    {
        while (true)
        {
            SearchHit[] work;
            int generation;
            lock (iconGate)
            {
                if (iconsDisposed || pendingIcons.Length == 0)
                {
                    iconWorkerRunning = false;
                    return;
                }
                work = pendingIcons;
                pendingIcons = [];
                generation = iconGeneration;
            }
            foreach (SearchHit hit in work)
            {
                lock (iconGate)
                    if (iconsDisposed || generation != iconGeneration) break;
                Bitmap? image;
                try { image = loadIcon(hit); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or
                    System.Runtime.InteropServices.ExternalException)
                { continue; }
                if (image is null) continue;
                lock (iconGate)
                {
                    if (iconsDisposed || generation != iconGeneration)
                    {
                        image.Dispose();
                        break;
                    }
                    icons.Add(hit, image);
                }
                try { BeginInvoke(() => { if (!IsDisposed) rows.Invalidate(); }); }
                catch (InvalidOperationException) { /* Handle destroyed or not yet created. */ }
            }
        }
    }

    // Returns a private copy so diagnostics cannot take ownership of a cached image.
    internal Bitmap? CopyIcon(SearchHit hit)
    {
        lock (iconGate) return icons.TryGetValue(hit, out Bitmap? image) ? (Bitmap)image.Clone() : null;
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        e.DrawBackground();
        if (rows.Items[e.Index] is not SearchHit hit)
        {
            TextRenderer.DrawText(e.Graphics, rows.Items[e.Index]?.ToString(), Font, e.Bounds,
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color foreground = selected ? SystemColors.HighlightText : Theme.Text;
        int iconSize = Scale(20);
        var iconBounds = new Rectangle(e.Bounds.Left + Scale(7),
            e.Bounds.Top + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
        lock (iconGate)
            if (icons.TryGetValue(hit, out Bitmap? image)) e.Graphics.DrawImage(image, iconBounds);
        int titleLeft = e.Bounds.Left + Scale(39);
        var title = new Rectangle(titleLeft, e.Bounds.Top + Scale(3),
            Math.Max(0, e.Bounds.Right - titleLeft - Scale(7)), Font.Height + 2);
        TextRenderer.DrawText(e.Graphics, Path.GetFileName(hit.FullPath.TrimEnd('\\')), Font, title, foreground,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var path = new Rectangle(title.Left, title.Bottom, title.Width, Font.Height + 2);
        TextRenderer.DrawText(e.Graphics, hit.FullPath, Font, path, selected ? foreground : Theme.Muted,
            TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (ownerForm is not null)
            {
                ownerForm.Deactivate -= HideOnOwnerChange;
                ownerForm.Move -= HideOnOwnerChange;
                ownerForm.Resize -= HideOnOwnerChange;
            }
            lock (iconGate)
            {
                iconsDisposed = true;
                iconGeneration++;
                pendingIcons = [];
                foreach (Bitmap image in icons.Values) image.Dispose();
                icons.Clear();
            }
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
