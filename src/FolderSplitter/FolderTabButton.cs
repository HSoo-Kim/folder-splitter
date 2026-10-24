namespace QuadExplorer;

internal sealed class FolderTabButton : ToolStripButton
{
    private bool closePressed;
    private bool dragged;
    private Point pressPosition;
    private ToolStrip? dragOwner;
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
        float scale = (Owner?.DeviceDpi ?? 96) / 96f;
        using var pen = new Pen(Selected ? Theme.Text : Theme.Muted, 1.6f * scale);
        float x = CloseBounds.Left + CloseBounds.Width / 2f;
        int y = Height / 2;
        float radius = 5 * scale;
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLine(pen, x - radius, y - radius, x + radius, y + radius);
        e.Graphics.DrawLine(pen, x + radius, y - radius, x - radius, y + radius);
        e.Graphics.SmoothingMode = smoothing;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopTracking();
        base.Dispose(disposing);
    }
}
