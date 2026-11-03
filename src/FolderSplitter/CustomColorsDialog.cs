using System.Drawing.Drawing2D;

namespace QuadExplorer;

// Builds a theme from an accent and a background color. Swatches cover common choices and More colors opens
// the Windows color picker; the preview shows the generated palette, which only takes effect on Apply.
internal sealed class CustomColorsDialog : Form
{
    private static readonly Color[] AccentSwatches = Colors("#0078D4", "#008B8B", "#278349", "#7C8530", "#D97818", "#CC4545", "#CA4D8C", "#8F57B5", "#5966C7", "#64748B");
    private static readonly Color[] BackgroundSwatches = Colors("#FFFFFF", "#F7F3E8", "#E6E5E1", "#E6F2EA", "#E8F1F8", "#EEEAF7", "#F8E9ED", "#EEDFC4", "#25282D", "#182638", "#1D3028", "#302336");
    private readonly List<SwatchButton> swatches = [];
    private readonly PalettePreview preview;
    private readonly UiText text;

    internal Color Accent { get; private set; }
    internal Color Background { get; private set; }
    internal PaletteGenerator.ThemeColors PreviewColors => preview.Colors;

    internal CustomColorsDialog(UiText text, Color accent, Color background, Form owner)
    {
        Accent = accent;
        this.text = text;
        Background = background;
        Owner = owner;
        Font = owner.Font;
        Text = text.T("Custom colors");
        AccessibleName = "Custom colors";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Margin = Padding.Empty
        };
        AddColorRow("Accent color", true, AccentSwatches);
        AddColorRow("Background color", false, BackgroundSwatches);
        preview = new PalettePreview(text, PaletteGenerator.Generate(accent, background))
        {
            Dock = DockStyle.Fill, Size = new Size(600, 220), MinimumSize = new Size(600, 220),
            Margin = new Padding(0, 12, 0, 8), AccessibleName = "Custom colors preview", TabStop = false
        };
        layout.Controls.Add(preview);
        layout.Controls.Add(new Label
        {
            Text = text.T("Colors are adjusted so text stays easy to read."), AutoSize = true,
            ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 12)
        });
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty
        };
        var cancel = Theme.Button(text.T("Cancel"), "Cancel", () => DialogResult = DialogResult.Cancel, 100);
        var apply = Theme.Button(text.T("Apply"), "Apply", () => DialogResult = DialogResult.OK, 100);
        cancel.Dock = apply.Dock = DockStyle.None;
        cancel.AutoSize = apply.AutoSize = true;
        cancel.DialogResult = DialogResult.Cancel;
        apply.DialogResult = DialogResult.OK;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = apply;
        CancelButton = cancel;
        Theme.Apply(this);
        UpdateSelection();

        void AddColorRow(string key, bool isAccent, Color[] choices)
        {
            layout.Controls.Add(new Label
            {
                Text = text.T(key), AutoSize = true, Margin = new Padding(0, isAccent ? 0 : 12, 0, 4)
            });
            var row = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                WrapContents = false, Margin = Padding.Empty
            };
            foreach (Color choice in choices)
            {
                var swatch = new SwatchButton(choice, isAccent)
                {
                    AccessibleName = text.T(key) + " " + SessionStore.FormatColor(choice),
                    Size = new Size(32, 32), Margin = new Padding(0, 0, 3, 0)
                };
                swatch.Click += (_, _) => PickColor(isAccent, choice);
                swatches.Add(swatch);
                row.Controls.Add(swatch);
            }
            var more = Theme.Button(text.T("More colors…"), key + " more colors", () =>
            {
                using var picker = new ColorDialog { Color = isAccent ? Accent : Background, FullOpen = true };
                if (picker.ShowDialog(this) == DialogResult.OK) PickColor(isAccent, picker.Color);
            }, 140);
            more.Dock = DockStyle.None;
            more.AutoSize = true;
            more.Margin = new Padding(8, 0, 0, 0);
            row.Controls.Add(more);
            layout.Controls.Add(row);
        }
    }

    internal void PickSwatch(bool accent, int index)
    {
        Color[] choices = accent ? AccentSwatches : BackgroundSwatches;
        if ((uint)index >= choices.Length) throw new ArgumentOutOfRangeException(nameof(index));
        PickColor(accent, choices[index]);
    }

    private void PickColor(bool accent, Color color)
    {
        if (accent) Accent = color;
        else Background = color;
        UpdateSelection();
        preview.Colors = PaletteGenerator.Generate(Accent, Background);
        preview.Invalidate();
    }

    private void UpdateSelection()
    {
        foreach (var swatch in swatches)
        {
            swatch.Selected = swatch.Color.ToArgb() == (swatch.IsAccent ? Accent : Background).ToArgb();
            swatch.AccessibleDescription = swatch.Selected ? text.T("Selected") : null;
            swatch.Invalidate();
        }
    }

    private static Color[] Colors(params string[] values) => values.Select(SessionStore.ParseColor).ToArray();

    private sealed class SwatchButton : Button
    {
        internal Color Color { get; }
        internal bool IsAccent { get; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Selected { get; set; }

        internal SwatchButton(Color color, bool accent)
        {
            Color = color;
            IsAccent = accent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = DeviceDpi / 96f;
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var circle = new RectangleF(5 * scale, 5 * scale, Width - 10 * scale - 1, Height - 10 * scale - 1);
            using var fill = new SolidBrush(Color);
            using var edge = new Pen(Theme.Muted, scale);
            e.Graphics.FillEllipse(fill, circle);
            e.Graphics.DrawEllipse(edge, circle);
            if (Selected)
            {
                circle.Inflate(3 * scale, 3 * scale);
                using var ring = new Pen(Theme.Accent, 2 * scale);
                e.Graphics.DrawEllipse(ring, circle);
            }
            // Keyboard focus gets its own dotted square so it stays visible on the selected swatch too.
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Text, Math.Max(1, scale)) { DashStyle = DashStyle.Dot };
                e.Graphics.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    private sealed class PalettePreview : Control
    {
        private readonly UiText text;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal PaletteGenerator.ThemeColors Colors { get; set; }

        internal PalettePreview(UiText text, PaletteGenerator.ThemeColors colors)
        {
            this.text = text;
            Colors = colors;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var colors = Colors;
            float scale = DeviceDpi / 96f;
            int S(int value) => (int)Math.Round(value * scale);
            e.Graphics.Clear(colors.Canvas);
            int top = S(36), sidebar = S(146), inset = S(12), row = S(34);
            var card = Rectangle.FromLTRB(sidebar + inset, top, Width - inset, Height - inset);
            Fill(card, colors.Surface);
            Outline(card, colors.Border);
            Draw(text.T("Custom colors"), new Rectangle(inset, 0, Width - inset * 2, top), colors.Text);
            var favorite = new Rectangle(S(8), top + S(8), sidebar - S(16), row);
            Fill(favorite, colors.Selected);
            Draw(text.T("Favorites"), Rectangle.Inflate(favorite, -S(8), 0), colors.Text);
            Draw(text.T("Folder tree"), new Rectangle(S(16), favorite.Bottom + S(4), sidebar - S(24), row), colors.Muted);
            Draw(text.T("Name"), new Rectangle(card.Left + inset, card.Top + S(4), card.Width - inset * 2, row), colors.Muted);
            for (int i = 0; i < 3; i++)
            {
                var bounds = new Rectangle(card.Left + S(6), card.Top + row + S(8) + i * row, card.Width - S(12), row);
                if (i == 1) Fill(bounds, colors.Selected);
                Draw(i switch { 0 => text.T("Documents"), 1 => "Project.txt", _ => "Notes.txt" },
                    new Rectangle(bounds.Left + S(8), bounds.Top, bounds.Width / 2, row), colors.Text);
                Draw(i == 0 ? text.T("Folder") : "12 KB", new Rectangle(bounds.Left + bounds.Width / 2, bounds.Top, bounds.Width / 2 - S(8), row), colors.Muted);
            }
            var link = new Rectangle(card.Left + inset, card.Bottom - row - S(6), S(124), row);
            Outline(link, colors.Accent);
            Draw(text.T("Open in new tab"), Rectangle.Inflate(link, -S(8), 0), colors.Accent);
            Outline(new Rectangle(0, 0, Width - 1, Height - 1), colors.Border);

            void Fill(Rectangle bounds, Color color)
            {
                using var brush = new SolidBrush(color);
                e.Graphics.FillRectangle(brush, bounds);
            }
            void Outline(Rectangle bounds, Color color)
            {
                using var pen = new Pen(color, Math.Max(1, scale));
                e.Graphics.DrawRectangle(pen, bounds);
            }
            void Draw(string value, Rectangle bounds, Color color) => TextRenderer.DrawText(e.Graphics, value, Font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
