namespace QuadExplorer;

internal sealed class FolderTabButton : ToolStripButton
{
    private bool closePressed;
    private bool dragged;
    private Point pressPosition;
    private ToolStrip? dragOwner;
    public event EventHandler? CloseRequested;
    public event EventHandler? DragRequested;

    public FolderTabButton()
    {
        Padding = new Padding(0, 0, 20, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        StopTracking();
        dragged = false;
        closePressed = e.Button == MouseButtons.Left && e.X >= Width - 22;
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
        using var pen = new Pen(Selected ? Theme.Text : Theme.Muted, 1.2f);
        int x = Width - 14;
        int y = Height / 2;
        e.Graphics.DrawLine(pen, x - 3, y - 3, x + 3, y + 3);
        e.Graphics.DrawLine(pen, x + 3, y - 3, x - 3, y + 3);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopTracking();
        base.Dispose(disposing);
    }
}
