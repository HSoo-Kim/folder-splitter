namespace QuadExplorer;

internal sealed class FolderTabButton : ToolStripButton
{
    private bool closePressed;
    private bool dragged;
    private Point pressPosition;
    private ToolStrip? dragOwner;
    private TabGroupColor? groupColor;
    private bool multiSelected;
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

    internal Rectangle CloseBounds => new(Width - CloseWidth, 0, CloseWidth, Height);
    internal int CloseWidth => (int)Math.Round(28 * (Owner?.DeviceDpi ?? 96) / 96d);

    public FolderTabButton()
    {
        Padding = new Padding(0, 0, 28, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        StopTracking();
        dragged = false;
        closePressed = e.Button == MouseButtons.Left && CloseBounds.Contains(e.Location);
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && !closePressed && Owner is not null)
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
        if (closePressed)
        {
            closePressed = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else base.OnClick(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
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
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Checked || Selected) // the close mark shows on the viewed tab and under the pointer
            Theme.PaintClose(e.Graphics, CloseBounds, Selected ? Theme.Text : Theme.Muted, Owner?.DeviceDpi ?? 96);
        // Draw last: renderer early returns for selected/hovered tabs must never hide membership.
        if (GroupColor is { } color && Width > 0 && Height > 0)
        {
            int thickness = Math.Min(Height, Math.Max(2, (int)Math.Round(3 * (Owner?.DeviceDpi ?? 96) / 96d)));
            using var brush = new SolidBrush(Theme.GroupColor(color));
            e.Graphics.FillRectangle(brush, 0, Height - thickness, Width, thickness);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopTracking();
        base.Dispose(disposing);
    }
}
