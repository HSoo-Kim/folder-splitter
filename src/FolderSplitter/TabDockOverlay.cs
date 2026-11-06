namespace QuadExplorer;

internal sealed record TabDropPlan(int TargetPane, SplitEdge? Edge, Rectangle Preview, string Label, bool Allowed,
    int? InsertionIndex = null, TabGroup? JoinGroup = null, MainForm? TargetWindow = null)
{
    internal Color MarkerColor => !Allowed ? Color.FromArgb(160, 55, 45)
        : JoinGroup is { } group ? Theme.GroupColor(group.Color) : Theme.Accent;
    internal TabGroup? DwellGroup { get; init; }
    internal float DwellProgress { get; init; }
    internal Rectangle GhostSlot { get; init; }
    internal Rectangle[] GroupHighlight { get; init; } = [];
    internal int IncomingCount { get; init; }
}

// Shown only while dragging one of our tabs. Native Shell file drops otherwise
// continue to go directly to the Shell views beneath this window.
internal sealed class TabDockOverlay : Form
{
    internal const string DragFormat = "FolderSplitter.Tab";
    private readonly Func<Point, TabDropPlan?> hitTest;
    private Rectangle[] paneBounds;
    private readonly UiText text;
    private TabDropPlan? preview;
    private Point? previewPoint;
    private readonly Action? clearStripDwell;

    internal TabDropPlan? Result { get; private set; }
    protected override bool ShowWithoutActivation => true;

    internal TabDockOverlay(Rectangle bounds, Rectangle[] panes, Func<Point, TabDropPlan?> hitTest, UiText text,
        Action? clearStripDwell = null)
    {
        this.text = text;
        this.hitTest = hitTest;
        this.clearStripDwell = clearStripDwell;
        paneBounds = panes;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 11);
        Opacity = .78;
        AllowDrop = true;
        DoubleBuffered = true;
        AccessibleName = "Tab docking preview";
        DragEnter += UpdatePreview;
        DragOver += UpdatePreview;
        DragLeave += (_, _) => { preview = null; previewPoint = null; clearStripDwell?.Invoke(); Invalidate(); Update(); };
        DragDrop += (_, e) =>
        {
            UpdatePreview(this, e);
            if (preview is { Allowed: true }) Result = preview;
        };
    }

    internal void RefreshBounds(Rectangle bounds, Rectangle[] panes)
    {
        if (Bounds != bounds) Bounds = bounds;
        paneBounds = panes;
        Invalidate();
    }

    private void UpdatePreview(object? sender, DragEventArgs e)
    {
        previewPoint = new Point(e.X, e.Y);
        preview = e.Data?.GetDataPresent(DragFormat, false) == true
            ? hitTest(new Point(e.X, e.Y)) : null;
        e.Effect = preview is { Allowed: true } ? DragDropEffects.Move : DragDropEffects.None;
        Invalidate();
    }

    internal void RefreshStripPreview()
    {
        if (previewPoint is not { } point || IsDisposed) return;
        preview = hitTest(point);
        Invalidate();
        Update();
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
            TextRenderer.DrawText(e.Graphics, text.T("Drop near a file-list edge to split, in the center to move, or on the tab strip to reorder.\nEsc cancels."),
                Font, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        Rectangle area = ToLocal(preview.Preview);
        Color accent = preview.MarkerColor;
        if (preview.InsertionIndex is not null)
        {
            PaintStripFeedback(e.Graphics, preview, Location, true);
            var label = new Rectangle(8, Math.Min(ClientSize.Height - Font.Height * 2, area.Bottom + 4),
                Math.Max(0, ClientSize.Width - 16), Font.Height * 2);
            Color labelColor = preview.JoinGroup is { } group ? Theme.GroupTextColor(group.Color) : Theme.Text;
            TextRenderer.DrawText(e.Graphics, preview.Label, Font, label, labelColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        area.Inflate(-5, -5);
        using var fill = new SolidBrush(preview.Allowed ? Theme.Selected : Color.FromArgb(253, 232, 228));
        using var outline = new Pen(accent, 3);
        e.Graphics.FillRectangle(fill, area);
        e.Graphics.DrawRectangle(outline, area);
        TextRenderer.DrawText(e.Graphics, preview.Label, Font, area, accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    internal static void PaintStripFeedback(Graphics graphics, TabDropPlan plan, Point origin, bool highlightGroup)
    {
        Rectangle Local(Rectangle screen) => new(screen.X - origin.X, screen.Y - origin.Y, screen.Width, screen.Height);
        Color color = plan.MarkerColor;
        if (plan.JoinGroup is { } group)
        {
            if (highlightGroup)
            {
                for (int i = 0; i < plan.GroupHighlight.Length; i++)
                {
                    Rectangle region = Local(plan.GroupHighlight[i]);
                    if (region.Width <= 0 || region.Height <= 0) continue;
                    if (i == 0)
                    {
                        region.Inflate(-3, -3);
                        Theme.PaintGroupPill(graphics, region, group.Color, true);
                        string caption = group.Collapsed
                            ? $"{group.Name} +{plan.IncomingCount}" : group.Name;
                        TextRenderer.DrawText(graphics, caption, SystemFonts.MenuFont, region,
                            Theme.GroupTextColor(group.Color), TextFormatFlags.HorizontalCenter
                            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    }
                    else
                    {
                        using var tint = new SolidBrush(Color.FromArgb(35, color));
                        graphics.FillRectangle(tint, region);
                        using var underline = new SolidBrush(color);
                        int thickness = Math.Max(5, plan.Preview.Width * 2);
                        graphics.FillRectangle(underline, region.Left, region.Bottom - thickness, region.Width, thickness);
                    }
                }
            }
            Rectangle slot = Local(plan.GhostSlot);
            if (slot.Width > 2 && slot.Height > 2)
            {
                slot.Inflate(-1, -1);
                using var tint = new SolidBrush(Color.FromArgb(55, color));
                using var outline = new Pen(color, Math.Max(2, plan.Preview.Width / 2f))
                { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                graphics.FillRectangle(tint, slot);
                graphics.DrawRectangle(outline, slot);
                // A geometric plus remains distinct even when group and accent colors coincide.
                int centerX = slot.Left + slot.Width / 2, centerY = slot.Top + slot.Height / 2;
                int radius = Math.Max(4, slot.Height / 6);
                using var plus = new Pen(Theme.GroupTextColor(group.Color), Math.Max(2, plan.Preview.Width / 2f));
                graphics.DrawLine(plus, centerX - radius, centerY, centerX + radius, centerY);
                graphics.DrawLine(plus, centerX, centerY - radius, centerX, centerY + radius);
            }
        }
        Rectangle marker = Local(plan.Preview);
        using var markerBrush = new SolidBrush(color);
        graphics.FillRectangle(markerBrush, marker);
        if (plan.JoinGroup is null && plan.DwellGroup is { } candidate && plan.DwellProgress > 0)
        {
            int height = Math.Max(1, (int)Math.Round(marker.Height * plan.DwellProgress));
            var progress = new Rectangle(marker.Left, marker.Bottom - height, marker.Width * 2, height);
            using var fill = new SolidBrush(Theme.GroupColor(candidate.Color));
            graphics.FillRectangle(fill, progress);
        }
    }

    private Rectangle ToLocal(Rectangle screen) =>
        new(screen.X - Left, screen.Y - Top, screen.Width, screen.Height);
}
