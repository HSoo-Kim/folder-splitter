using QuadExplorer.Search;

namespace QuadExplorer;

// The row after the last result: "show more" asks for the next page; the limit note only explains.
internal enum CandidateFooter { More, Limit }

internal sealed class AddressCandidates : Form
{
    private readonly ListBox rows = new ResultList()
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
    internal event Action? MoreRequested;
    // Result pages shown in turn; choosing the "show more" row asks for the next one.
    internal static readonly int[] ResultLimits = [50, 200, 500];
    private sealed record FooterRow(CandidateFooter Kind, string Text)
    {
        public override string ToString() => Text;
    }
    private int lastSelectable;
    internal bool MoreSelected => rows.SelectedItem is FooterRow { Kind: CandidateFooter.More };
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
            if (index < 0) return;
            if (rows.Items[index] is SearchHit hit) Chosen?.Invoke(hit);
            else if (rows.Items[index] is FooterRow { Kind: CandidateFooter.More }) MoreRequested?.Invoke();
        };
        rows.MouseMove += (_, e) =>
        {
            int index = rows.IndexFromPoint(e.Location);
            string? path = index >= 0 && rows.Items[index] is SearchHit hit ? hit.FullPath : null;
            if (path == hoveredPath) return;
            hoveredPath = path;
            tips.SetToolTip(rows, path);
        };
        rows.SelectedIndexChanged += (_, _) =>
        {
            // The note after the last result is not a choice; stay on the last real row.
            if (rows.SelectedItem is FooterRow { Kind: CandidateFooter.Limit }) rows.SelectedIndex = lastSelectable;
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

    internal void Present(TextBox address, IReadOnlyList<SearchHit> hits, string? message = null,
        (CandidateFooter Kind, string Text)? footer = null, int select = 0)
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
            lastSelectable = Math.Max(0, rows.Items.Count - 1);
            if (message is null && footer is { } note)
            {
                rows.Items.Add(new FooterRow(note.Kind, note.Text));
                if (note.Kind == CandidateFooter.More) lastSelectable = rows.Items.Count - 1;
            }
            if (rows.Items.Count > 0 && message is null) rows.SelectedIndex = Math.Clamp(select, 0, lastSelectable);
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
        // After "show more", the first new result is on screen with one earlier row for context.
        if (select > 0 && rows.SelectedIndex > 0) rows.TopIndex = rows.SelectedIndex - 1;
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
            rows.SelectedIndex = Math.Clamp(rows.SelectedIndex + delta, 0, lastSelectable);
    }

    private void HideOnOwnerChange(object? sender, EventArgs e) => Hide();

    private int Scale(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        rows.ItemHeight = Math.Max(Scale(42), Font.Height * 2 + Scale(10));
        QueueIcons(Hits, reload: true);
        rows.Invalidate();
    }

    private void QueueIcons(IReadOnlyList<SearchHit> hits, bool reload = false)
    {
        lock (iconGate)
        {
            iconGeneration++;
            // The popup never shows more than its largest page. Bound memory
            // even if another caller supplies an unexpectedly large result set.
            var current = hits.Take(ResultLimits[^1]).Distinct().ToArray();
            HashSet<SearchHit> wanted = reload ? [] : new(current);
            // Icons already loaded for results that stay (the page before "show more") are kept; the rest are dropped.
            foreach (SearchHit stale in icons.Keys.Where(hit => !wanted.Contains(hit)).ToArray())
            {
                icons[stale].Dispose();
                icons.Remove(stale);
            }
            pendingIcons = current.Where(hit => !icons.ContainsKey(hit)).ToArray();
            if (iconsDisposed || iconWorkerRunning || pendingIcons.Length == 0) return;
            iconWorkerRunning = true;
            AddressShellIcons.StartThread(LoadIcons);
        }
    }

    private void LoadIcons()
    {
        try { LoadIconsCore(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Like the thread-pool task this thread replaced, an unexpected failure costs the icons, never the
            // window; the next query starts a new icon thread.
            System.Diagnostics.Debug.WriteLine(error);
            lock (iconGate) iconWorkerRunning = false;
        }
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
                AddressShellIcons.PumpMessages();
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
        if (rows.Items[e.Index] is FooterRow footer)
        {
            // "Show more" is an action in the accent colour; the note on the last page is plain muted text.
            Color color = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText
                : footer.Kind == CandidateFooter.More ? Theme.Accent : Theme.Muted;
            var bounds = new Rectangle(e.Bounds.Left + Scale(39), e.Bounds.Top, Math.Max(0, e.Bounds.Width - Scale(46)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, footer.Text, Font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
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

    // The popup only presents the address box's results. A Win32 list box focuses itself when clicked, which would
    // take keyboard focus from the address box (typing would stop working and "show more" would find it unfocused),
    // so a click chooses the row here without ever reaching that default handling.
    private sealed class ResultList : ListBox
    {
        internal ResultList()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x203) return; // WM_LBUTTONDBLCLK: the first click of the pair already acted; the list may have changed since
            if (m.Msg == 0x201) // WM_LBUTTONDOWN
            {
                var point = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
                int index = IndexFromPoint(point);
                if (index >= 0) SelectedIndex = index;
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                return;
            }
            base.WndProc(ref m);
        }
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
