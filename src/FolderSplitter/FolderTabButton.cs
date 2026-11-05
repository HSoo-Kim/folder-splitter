namespace QuadExplorer;

internal sealed class FolderTabButton : ToolStripButton
{
    private bool closePressed;
    private bool pinPressed;
    private bool dragged;
    private System.Drawing.Drawing2D.GraphicsPath? pinPath;
    private int pinPathDpi;
    private Point pressPosition;
    private ToolStrip? dragOwner;
    private TabGroupColor? groupColor;
    private bool multiSelected;
    private bool pinned;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Pinned
    {
        get => pinned;
        set
        {
            if (pinned == value) return;
            pinned = value;
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            ImageAlign = ContentAlignment.MiddleLeft;
            Invalidate();
        }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool MultiSelected
    {
        get => multiSelected;
        set { if (multiSelected == value) return; multiSelected = value; Invalidate(); }
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new TabAccessibleObject(this);
    private sealed class TabAccessibleObject(FolderTabButton owner) : ToolStripItemAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State
            | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None)
            | (owner.MultiSelected ? AccessibleStates.Selected : AccessibleStates.None);
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal TabGroupColor? GroupColor
    {
        get => groupColor;
        set
        {
            if (groupColor == value) return;
            groupColor = value;
            Invalidate();
        }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";
    public event EventHandler? CloseRequested;
    public event EventHandler? DragRequested;
    public event EventHandler? UnpinRequested;
    internal string UnpinToolTip = "";
    internal string PathToolTip = "";
    internal Rectangle PinBounds => Pinned ? new(Width - ActionWidth, 0, ActionWidth, Height) : Rectangle.Empty;
    internal int ActionWidth => (int)Math.Round(28 * (Owner?.DeviceDpi ?? 96) / 96d);

    internal Rectangle CloseBounds => Pinned ? Rectangle.Empty : new(Width - ActionWidth, 0, ActionWidth, Height);
    internal int CloseWidth => Pinned ? 0 : ActionWidth;

    public FolderTabButton()
    {
        Padding = new Padding(0, 0, 28, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        StopTracking();
        dragged = false;
        closePressed = e.Button == MouseButtons.Left && CloseBounds.Contains(e.Location);
        pinPressed = e.Button == MouseButtons.Left && PinBounds.Contains(e.Location);
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && !closePressed && !pinPressed && Owner is not null)
        {
            pressPosition = Cursor.Position;
            dragOwner = Owner;
            dragOwner.MouseMove += TrackDrag;
            dragOwner.MouseUp += EndPress;
            dragOwner.MouseCaptureChanged += CaptureChanged;
            dragOwner.Capture = true;
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (dragged) return;
        if (pinPressed)
        {
            pinPressed = false;
            UnpinRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (closePressed)
        {
            closePressed = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else base.OnClick(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Pinned) ToolTipText = PinBounds.Contains(e.Location) ? UnpinToolTip : PathToolTip;
        if (dragOwner is not null) TrackDrag(this, e);
    }

    private void TrackDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { StopTracking(); return; }
        Size threshold = SystemInformation.DragSize;
        var deadZone = new Rectangle(pressPosition.X - threshold.Width / 2,
            pressPosition.Y - threshold.Height / 2, threshold.Width, threshold.Height);
        if (deadZone.Contains(Cursor.Position)) return;
        dragged = true;
        StopTracking();
        DragRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EndPress(object? sender, MouseEventArgs e) => StopTracking();
    private void CaptureChanged(object? sender, EventArgs e)
    {
        if (dragOwner is { Capture: false }) StopTracking();
    }

    private void StopTracking()
    {
        var owner = dragOwner;
        dragOwner = null;
        if (owner is null) return;
        owner.MouseMove -= TrackDrag;
        owner.MouseUp -= EndPress;
        owner.MouseCaptureChanged -= CaptureChanged;
        owner.Capture = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        closePressed = false;
        pinPressed = false;
        if (Pinned) ToolTipText = PathToolTip;
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Pinned)
            PaintPin(e.Graphics);
        else if (Checked || Selected)
            Theme.PaintClose(e.Graphics, CloseBounds, Selected ? Theme.Text : Theme.Muted, Owner?.DeviceDpi ?? 96);
        // Pinned tabs never carry group membership, even during a strip rebuild.
        if (!Pinned && GroupColor is { } color && Width > 0 && Height > 0)
        {
            int thickness = Math.Min(Height, Math.Max(2, (int)Math.Round(3 * (Owner?.DeviceDpi ?? 96) / 96d)));
            using var brush = new SolidBrush(Theme.GroupColor(color));
            e.Graphics.FillRectangle(brush, 0, Height - thickness, Width, thickness);
        }
    }

    private void PaintPin(Graphics graphics)
    {
        int dpi = Owner?.DeviceDpi ?? 96;
        if (pinPath is null || pinPathDpi != dpi)
        {
            pinPath?.Dispose();
            pinPathDpi = dpi;
            using var family = new FontFamily(Theme.IconFont);
            pinPath = new System.Drawing.Drawing2D.GraphicsPath();
            pinPath.AddString("\uE718", family, (int)FontStyle.Regular, 1000, Point.Empty, StringFormat.GenericTypographic);
            var ink = pinPath.GetBounds();
            float size = 12 * dpi / 96f, scale = size / Math.Max(ink.Width, ink.Height);
            using var transform = new System.Drawing.Drawing2D.Matrix(scale, 0, 0, scale,
                -ink.Left * scale - ink.Width * scale / 2, -ink.Top * scale - ink.Height * scale / 2);
            pinPath.Transform(transform);
        }
        // Vector coverage keeps the icon monochrome rather than adding ClearType color fringes.
        var state = graphics.Save();
        var slot = PinBounds;
        graphics.TranslateTransform(slot.Left + slot.Width / 2f, slot.Height / 2f);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Checked || Selected ? Theme.Text : Theme.Muted);
        graphics.FillPath(fill, pinPath);
        graphics.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopTracking();
            pinPath?.Dispose();
        }
        base.Dispose(disposing);
    }
}
