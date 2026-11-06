using System.Globalization;
using System.Text;

namespace QuadExplorer;

internal sealed class TabGroupChip : ToolStripButton
{
    private const TextFormatFlags LabelFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
        | TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
        | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis;

    private bool dragged;
    private Point pressPosition;
    private ToolStrip? dragOwner;
    private string? labelName;
    private string? labelLanguage;
    private int labelCount = -1;
    private bool labelCollapsed;
    private TabGroupColor labelColor;
    private Color labelFill;
    private readonly string?[] memberTitles = new string?[TabGroup.MaxTabs];
    private string? measuredText;
    private Font? measuredFont;
    private int measuredDpi;
    private Size measuredLabel;
    internal event Action? DragRequested;

    internal TabGroup Group { get; }
    private bool dropJoinArmed;
    private int dropJoinCount;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int DropJoinCount
    {
        get => dropJoinCount;
        set { if (dropJoinCount == value) return; dropJoinCount = value; Invalidate(); }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool DropJoinArmed
    {
        get => dropJoinArmed;
        set
        {
            if (dropJoinArmed == value) return;
            dropJoinArmed = value;
            Invalidate();
        }
    }

    internal TabGroupChip(TabGroup group)
    {
        Group = group;
        DisplayStyle = ToolStripItemDisplayStyle.Text;
        AutoToolTip = false;
        CheckOnClick = false;
        AccessibleRole = AccessibleRole.PushButton;
        Overflow = ToolStripItemOverflow.Never;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    internal int MinimumLabelWidth => Math.Min(GetPreferredSize(Size.Empty).Width,
        Scale(28));

    internal void RefreshLabel(UiText text, IReadOnlyList<ExplorerPane.FolderTab> members, int first, int count)
    {
        string name = Group.Name;
        bool labelChanged = labelName != name || labelLanguage != text.Language || labelCount != count
            || labelCollapsed != Group.Collapsed || labelColor != Group.Color;
        if (labelChanged)
        {
            labelName = name;
            labelLanguage = text.Language;
            labelCount = count;
            labelCollapsed = Group.Collapsed;
            labelColor = Group.Color;
            string label = name;
            if (Group.Collapsed)
            {
                string number = count.ToString(CultureInfo.GetCultureInfo(text.Language));
                label = name.Length == 0 ? number : $"{name} {number}";
            }
            if (Text != label) Text = label;
            string displayName = name.Length == 0 ? text.T(Group.Color.ToString()) : name;
            string accessibleName = text.T("Tab group: {0}, {1} tabs, {2}", displayName, count,
                text.T(Group.Collapsed ? "collapsed" : "expanded"));
            if (AccessibleName != accessibleName) AccessibleName = accessibleName;
        }
        bool tooltipChanged = labelChanged;
        int titleCount = Math.Min(count, TabGroup.MaxTabs);
        for (int index = 0; index < titleCount; index++)
        {
            string title = members[first + index].Button.Title;
            if (memberTitles[index] == title) continue;
            memberTitles[index] = title;
            tooltipChanged = true;
        }
        if (tooltipChanged)
        {
            var tooltip = new StringBuilder(AccessibleName);
            for (int index = 0; index < titleCount; index++)
                tooltip.AppendLine().Append(memberTitles[index]);
            ToolTipText = tooltip.ToString();
        }
        Color foreground = Theme.GroupTextColor(Group.Color);
        if (ForeColor != foreground) ForeColor = foreground;
        Color fill = Theme.GroupColor(Group.Color);
        if (labelFill != fill)
        {
            labelFill = fill;
            Invalidate();
        }
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        int dpi = Owner?.DeviceDpi ?? 96;
        string label = Text ?? "";
        if (measuredText != label || !ReferenceEquals(measuredFont, Font) || measuredDpi != dpi)
        {
            measuredText = label;
            measuredFont = Font;
            measuredDpi = dpi;
            measuredLabel = TextRenderer.MeasureText(label, Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        return new Size(Math.Clamp(measuredLabel.Width + Scale(label.Length == 0 ? 30 : 44), Scale(30), Scale(200)),
            Math.Max(Scale(28), measuredLabel.Height + Scale(12)));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new GroupAccessibleObject(this);

    private sealed class GroupAccessibleObject(TabGroupChip owner) : ToolStripItemAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State
            | (owner.Group.Collapsed ? AccessibleStates.Collapsed : AccessibleStates.Expanded);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData is Keys.Enter or Keys.Space)
        {
            // Consume the ToolStrip dialog-key route: do not also call the base click route.
            // Keeping selection/focus in the strip allows the next press to toggle it again.
            dragged = false;
            if (Enabled && Available) PerformClick();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        StopTracking();
        dragged = false;
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Owner is null) return;
        pressPosition = Cursor.Position;
        dragOwner = Owner;
        dragOwner.MouseMove += TrackDrag;
        dragOwner.MouseUp += EndPress;
        dragOwner.MouseCaptureChanged += CaptureChanged;
        dragOwner.Capture = true;
    }

    protected override void OnClick(EventArgs e)
    {
        if (!dragged) base.OnClick(e);
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
        DragRequested?.Invoke();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopTracking();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // ToolStrip paints the strip background. Skipping base prevents its button border/text.
        if (Width <= 0 || Height <= 0) return;
        string labelText = Text ?? "";
        int inset = Scale(3);
        var bounds = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (Pressed) bounds.Offset(0, Scale(1));
        Color fill = Theme.GroupPillColor(Group.Color, Selected || DropJoinArmed, Pressed);
        Color foreground = Theme.GroupTextColor(Group.Color);
        Theme.PaintGroupPill(e.Graphics, bounds, Group.Color, Selected || DropJoinArmed, Pressed);
        int icon = Math.Min(Scale(14), bounds.Height - Scale(6));
        var glyph = new Rectangle(bounds.Left + Scale(7), bounds.Top + (bounds.Height - icon) / 2, icon, icon);
        bool showIncomingCount = DropJoinArmed && Group.Collapsed;
        if (!showIncomingCount) Theme.PaintGroupFolder(e.Graphics, glyph, Group.Color);
        if (labelText.Length != 0 || DropJoinArmed && Group.Collapsed)
        {
            var label = Rectangle.FromLTRB(bounds.Left + Scale(showIncomingCount ? 7 : 26), bounds.Top, bounds.Right - Scale(7), bounds.Bottom);
            var flags = LabelFlags;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            if (DropJoinArmed && Group.Collapsed)
            {
                // Reserve the count suffix rather than resizing the chip beneath the held pointer.
                string suffix = $"{labelCount} +{DropJoinCount}";
                int countWidth = Math.Min(label.Width, TextRenderer.MeasureText(e.Graphics, suffix, Font,
                    Size.Empty, LabelFlags & ~TextFormatFlags.EndEllipsis).Width);
                var countBounds = new Rectangle(label.Right - countWidth, label.Top, countWidth, label.Height);
                TextRenderer.DrawText(e.Graphics, suffix, Font, countBounds, foreground, flags);
                label.Width = Math.Max(0, label.Width - countWidth - Scale(3));
                labelText = Group.Name;
            }
            TextRenderer.DrawText(e.Graphics, labelText, Font, label, foreground, flags);
        }
        if (Selected && Owner is { ContainsFocus: true })
        {
            var focus = Rectangle.Inflate(bounds, -Scale(2), -Scale(2));
            if (focus.Width > 0 && focus.Height > 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, focus,
                    labelText.Length == 0 ? Theme.Text : foreground, labelText.Length == 0 ? Owner.BackColor : fill);
        }
    }

    private int Scale(int value) => Math.Max(1, (int)Math.Round(value * (Owner?.DeviceDpi ?? 96) / 96d));
}
