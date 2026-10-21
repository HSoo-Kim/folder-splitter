namespace QuadExplorer;

internal sealed record TabDropPlan(int TargetPane, SplitEdge? Edge, Rectangle Preview, string Label, bool Allowed);

// Shown only while dragging one of our tabs. Native Shell file drops otherwise
// continue to go directly to the Shell views beneath this window.
internal sealed class TabDockOverlay : Form
{
    internal const string DragFormat = "FolderSplitter.Tab";
    private readonly Func<Point, TabDropPlan?> hitTest;
    private readonly Rectangle[] paneBounds;
    private readonly UiText text;
    private TabDropPlan? preview;

    internal TabDropPlan? Result { get; private set; }
    protected override bool ShowWithoutActivation => true;

    internal TabDockOverlay(Rectangle bounds, Rectangle[] panes, Func<Point, TabDropPlan?> hitTest, UiText text)
    {
        this.text = text;
        this.hitTest = hitTest;
        paneBounds = panes;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.White;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 11);
        Opacity = .78;
        AllowDrop = true;
        DoubleBuffered = true;
        AccessibleName = "Tab docking preview";
        DragEnter += UpdatePreview;
        DragOver += UpdatePreview;
        DragLeave += (_, _) => { preview = null; Invalidate(); };
        DragDrop += (_, e) =>
        {
            UpdatePreview(this, e);
            if (preview is { Allowed: true }) Result = preview;
        };
    }

    private void UpdatePreview(object? sender, DragEventArgs e)
    {
        preview = e.Data?.GetDataPresent(DragFormat, false) == true
            ? hitTest(new Point(e.X, e.Y)) : null;
        e.Effect = preview is { Allowed: true } ? DragDropEffects.Move : DragDropEffects.None;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Border, 2);
        foreach (Rectangle screen in paneBounds)
        {
            var rectangle = ToLocal(screen);
            rectangle.Inflate(-3, -3);
            e.Graphics.DrawRectangle(border, rectangle);
        }
        if (preview is null)
        {
            TextRenderer.DrawText(e.Graphics, text.T("Drop near a file-list edge to split, or in the center to move a tab.\nEsc cancels."),
                Font, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        Rectangle area = ToLocal(preview.Preview);
        area.Inflate(-5, -5);
        Color accent = preview.Allowed ? Theme.Accent : Color.FromArgb(160, 55, 45);
        using var fill = new SolidBrush(preview.Allowed ? Theme.Selected : Color.FromArgb(253, 232, 228));
        using var outline = new Pen(accent, 3);
        e.Graphics.FillRectangle(fill, area);
        e.Graphics.DrawRectangle(outline, area);
        TextRenderer.DrawText(e.Graphics, preview.Label, Font, area, accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private Rectangle ToLocal(Rectangle screen) =>
        new(screen.X - Left, screen.Y - Top, screen.Width, screen.Height);
}
