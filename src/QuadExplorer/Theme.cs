using System.Drawing.Drawing2D;

namespace QuadExplorer;

internal static class Theme
{
    public static readonly Color Canvas = Color.FromArgb(246, 247, 249);
    public static readonly Color Border = Color.FromArgb(222, 226, 232);
    public static readonly Color Text = Color.FromArgb(32, 37, 44);
    public static readonly Color Muted = Color.FromArgb(96, 104, 116);
    public static readonly Color Accent = Color.FromArgb(0, 95, 184);
    public static readonly Color Selected = Color.FromArgb(229, 241, 253);

    public static Button Button(string text, string accessibleName, Action action, int width = 34)
    {
        var button = new Button
        {
            Text = text, AccessibleName = accessibleName, Width = width, Height = 30,
            Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2), BackColor = Color.White, ForeColor = Text,
            UseVisualStyleBackColor = false, TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Selected;
        button.Click += (_, _) => action();
        return button;
    }

    public static ToolStrip Strip() => new()
    {
        GripStyle = ToolStripGripStyle.Hidden, BackColor = Canvas, ForeColor = Text,
        Renderer = new FlatRenderer(), Padding = new Padding(6, 3, 6, 3),
        AutoSize = false, Height = 38, Stretch = true, CanOverflow = true
    };

    public static Image FolderIcon()
    {
        var image = new Bitmap(20, 20);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var back = new SolidBrush(Color.FromArgb(205, 145, 36));
        using var front = new SolidBrush(Color.FromArgb(245, 192, 66));
        g.FillRectangle(back, 2, 4, 8, 5);
        g.FillRectangle(back, 2, 7, 16, 10);
        g.FillRectangle(front, 2, 8, 16, 9);
        return image;
    }

    private sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        public FlatRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Canvas;
        public override Color ToolStripGradientMiddle => Canvas;
        public override Color ToolStripGradientEnd => Canvas;
        public override Color ButtonSelectedHighlight => Selected;
        public override Color ButtonSelectedGradientBegin => Selected;
        public override Color ButtonSelectedGradientMiddle => Selected;
        public override Color ButtonSelectedGradientEnd => Selected;
        public override Color ButtonCheckedGradientBegin => Color.White;
        public override Color ButtonCheckedGradientMiddle => Color.White;
        public override Color ButtonCheckedGradientEnd => Color.White;
        public override Color ButtonCheckedHighlight => Color.White;
        public override Color ButtonPressedGradientBegin => Selected;
        public override Color ButtonPressedGradientMiddle => Selected;
        public override Color ButtonPressedGradientEnd => Selected;
        public override Color ButtonSelectedBorder => Border;
        public override Color ButtonPressedBorder => Accent;
        public override Color ButtonCheckedHighlightBorder => Border;
    }
}
