namespace QuadExplorer;

internal sealed class FolderTabButton : ToolStripButton
{
    private bool closePressed;
    public event EventHandler? CloseRequested;

    public FolderTabButton()
    {
        Padding = new Padding(0, 0, 20, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        closePressed = e.Button == MouseButtons.Left && e.X >= Width - 22;
        base.OnMouseDown(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (closePressed)
        {
            closePressed = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else base.OnClick(e);
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
}
