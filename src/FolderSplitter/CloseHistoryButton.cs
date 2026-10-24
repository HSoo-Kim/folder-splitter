namespace QuadExplorer;

internal sealed class CloseHistoryButton(bool clockwise) : ToolStripButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = (Owner?.DeviceDpi ?? 96) / 96f;
        float radius = 6 * scale, x = Width / 2f, y = Height / 2f + .75f * scale;
        int direction = clockwise ? 1 : -1;
        using var pen = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.2f * scale)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawArc(pen, x - radius, y - radius, radius * 2, radius * 2,
            clockwise ? 0 : 180, direction * 270);
        e.Graphics.DrawLine(pen, x, y - radius, x - direction * 2.75f * scale, y - radius - 2.5f * scale);
        e.Graphics.DrawLine(pen, x, y - radius, x - direction * 2.75f * scale, y - radius + 2.5f * scale);
        e.Graphics.SmoothingMode = smoothing;
    }
}
