using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace QuadExplorer;

// Builds a theme from an accent and a background color. Swatches cover common choices and More colors opens
// the Windows color picker; the preview shows the generated palette, which only takes effect on Apply.
internal sealed class CustomColorsDialog : Form
{
    private static readonly Color[] AccentSwatches = Colors("#0078D4", "#008B8B", "#278349", "#7C8530", "#D97818", "#CC4545", "#CA4D8C", "#8F57B5", "#5966C7", "#64748B");
    private static readonly Color[] BackgroundSwatches = Colors("#FFFFFF", "#F7F3E8", "#E6E5E1", "#E6F2EA", "#E8F1F8", "#EEEAF7", "#F8E9ED", "#EEDFC4", "#25282D", "#182638", "#1D3028", "#302336");
    private readonly List<SwatchButton> swatches = [];
    private readonly List<Label> rowLabels = [];
    private readonly List<Button> moreButtons = [];
    private readonly ToolTip colorTips = new();
    private readonly SwatchButton currentAccent;
    private readonly SwatchButton currentBackground;
    private readonly TableLayoutPanel layout;
    private readonly Label note;
    private readonly Button cancel;
    private readonly Button apply;
    private readonly PalettePreview preview;
    private readonly UiText text;
    private List<string> pickerColors;

    internal Color Accent { get; private set; }
    internal Color Background { get; private set; }
    internal IReadOnlyList<string> PickerColors => pickerColors;
    internal PaletteGenerator.ThemeColors PreviewColors => preview.Colors;
    internal PreviewLayout PreviewBounds => CalculatePreviewLayout(preview.ClientSize, preview.DeviceDpi);

    internal CustomColorsDialog(UiText text, Color accent, Color background, Form owner,
        IReadOnlyList<(Color Background, Color Accent)> recentColors, IReadOnlyList<string> pickerColors)
    {
        Accent = accent;
        this.text = text;
        Background = background;
        this.pickerColors = [.. pickerColors];
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
        Padding = Padding.Empty;

        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Margin = Padding.Empty
        };
        if (recentColors.Count > 0) AddRecentRow();
        // The background sets the whole look and decides light or dark text, so it is chosen first;
        // the accent row below then reads against it in the preview.
        currentBackground = AddColorRow("Background color", false, BackgroundSwatches);
        currentAccent = AddColorRow("Accent color", true, AccentSwatches);
        preview = new PalettePreview(text, PaletteGenerator.Generate(accent, background))
        {
            Dock = DockStyle.Fill, AccessibleName = "Custom colors preview", TabStop = false
        };
        layout.Controls.Add(preview);
        note = new Label
        {
            Text = text.T("Colors are adjusted so text stays easy to read."), AutoSize = true,
            ForeColor = Theme.Muted
        };
        layout.Controls.Add(note);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty
        };
        cancel = Theme.Button(text.T("Cancel"), "Cancel", () => DialogResult = DialogResult.Cancel, 100);
        apply = Theme.Button(text.T("Apply"), "Apply", () => DialogResult = DialogResult.OK, 100);
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
        ApplyDpiMetrics();

        void AddRecentRow()
        {
            var label = new Label
            {
                Text = text.T("Recent combinations"), AccessibleName = "Recent combinations", AutoSize = true
            };
            rowLabels.Add(label);
            layout.Controls.Add(label);
            var row = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                WrapContents = false, Margin = Padding.Empty
            };
            foreach (var pair in recentColors)
            {
                var swatch = new SwatchButton(pair.Accent, true, pair.Background)
                {
                    AccessibleName = "Recent combinations " + SessionStore.FormatColor(pair.Background)
                        + " " + SessionStore.FormatColor(pair.Accent)
                };
                swatch.Click += (_, _) => PickColors(pair.Accent, pair.Background);
                swatches.Add(swatch);
                colorTips.SetToolTip(swatch, swatch.AccessibleName);
                row.Controls.Add(swatch);
            }
            layout.Controls.Add(row);
        }

        SwatchButton AddColorRow(string key, bool isAccent, Color[] choices)
        {
            var label = new Label { Text = text.T(key), AutoSize = true };
            rowLabels.Add(label);
            layout.Controls.Add(label);
            var row = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                WrapContents = false, Margin = Padding.Empty
            };
            foreach (Color choice in choices)
            {
                var swatch = new SwatchButton(choice, isAccent)
                {
                    AccessibleName = text.T(key) + " " + SessionStore.FormatColor(choice)
                };
                swatch.Click += (_, _) => PickColor(isAccent, choice);
                swatches.Add(swatch);
                colorTips.SetToolTip(swatch, swatch.AccessibleName);
                row.Controls.Add(swatch);
            }
            var current = new SwatchButton(isAccent ? Accent : Background, isAccent)
            {
                Name = isAccent ? "CurrentAccentSwatch" : "CurrentBackgroundSwatch",
                Visible = false
            };
            current.Click += (_, _) => PickColor(isAccent, current.Color);
            swatches.Add(current);
            row.Controls.Add(current);
            var more = Theme.Button(text.T("More colors…"), key + " more colors", () =>
            {
                var initial = Enumerable.Repeat(0x00FFFFFF, 16).ToArray();
                for (int i = 0; i < this.pickerColors.Count; i++)
                    initial[i] = ColorTranslator.ToWin32(SessionStore.ParseColor(this.pickerColors[i]));
                using var picker = new ExpandedColorDialog
                {
                    Color = isAccent ? Accent : Background, FullOpen = true, CustomColors = initial
                };
                var result = picker.ShowDialog(this);
                var selected = picker.CustomColors;
                if (!selected.SequenceEqual(initial))
                {
                    // White is also Windows' empty-slot marker. Keep the original prefix and every changed
                    // slot (including a color replaced with white), but not untouched trailing white padding.
                    // Adding white to an already empty white slot makes no observable change.
                    int count = this.pickerColors.Count;
                    for (int i = 0; i < selected.Length; i++)
                        if (selected[i] != initial[i] || selected[i] != 0x00FFFFFF) count = Math.Max(count, i + 1);
                    this.pickerColors = selected.Take(count)
                        .Select(value => SessionStore.FormatColor(ColorTranslator.FromWin32(value))).ToList();
                }
                if (result == DialogResult.OK) PickColor(isAccent, picker.Color);
            }, 140);
            more.Dock = DockStyle.None;
            more.AutoSize = true;
            moreButtons.Add(more);
            row.Controls.Add(more);
            layout.Controls.Add(row);
            return current;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDpiMetrics();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // WinForms finishes scaling child controls after this event; restore our logical metrics afterward.
        BeginInvoke((Action)ApplyDpiMetrics);
    }

    private int Scaled(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    private void ApplyDpiMetrics()
    {
        if (IsDisposed) return;
        SuspendLayout();
        layout.SuspendLayout();
        Padding = new Padding(Scaled(16));
        foreach (var swatch in swatches)
        {
            swatch.Size = new Size(Scaled(32), Scaled(32));
            swatch.Margin = new Padding(0, 0, Scaled(3), 0);
        }
        for (int i = 0; i < rowLabels.Count; i++)
            rowLabels[i].Margin = new Padding(0, i == 0 ? 0 : Scaled(12), 0, Scaled(4));
        foreach (var more in moreButtons)
        {
            more.MinimumSize = new Size(Scaled(140), Scaled(30));
            more.Size = more.MinimumSize;
            more.Margin = new Padding(Scaled(8), 0, 0, 0);
        }
        foreach (var button in new[] { cancel, apply })
        {
            button.MinimumSize = new Size(Scaled(100), Scaled(30));
            button.Size = button.MinimumSize;
            button.Margin = new Padding(Scaled(2));
        }
        preview.MinimumSize = new Size(Scaled(600), Scaled(220));
        preview.Size = preview.MinimumSize;
        preview.Margin = new Padding(0, Scaled(12), 0, Scaled(8));
        note.Margin = new Padding(0, 0, 0, Scaled(12));
        layout.ResumeLayout(true);
        ResumeLayout(true);
        preview.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) colorTips.Dispose();
        base.Dispose(disposing);
    }

    private void PickColor(bool accent, Color color) => PickColors(accent ? color : Accent, accent ? Background : color);

    private void PickColors(Color accent, Color background)
    {
        Accent = accent;
        Background = background;
        UpdateSelection();
        preview.Colors = PaletteGenerator.Generate(Accent, Background);
        preview.Invalidate();
    }

    private void UpdateSelection()
    {
        UpdateCurrent(currentAccent, Accent, AccentSwatches, "Accent color");
        UpdateCurrent(currentBackground, Background, BackgroundSwatches, "Background color");
        foreach (var swatch in swatches)
        {
            swatch.Selected = swatch.PairBackground is { } pairBackground
                ? pairBackground.ToArgb() == Background.ToArgb() && swatch.Color.ToArgb() == Accent.ToArgb()
                : swatch.Color.ToArgb() == (swatch.IsAccent ? Accent : Background).ToArgb();
            swatch.AccessibleDescription = swatch.Selected ? text.T("Selected") : null;
            swatch.Invalidate();
        }

        void UpdateCurrent(SwatchButton current, Color color, Color[] choices, string key)
        {
            current.Color = color;
            current.AccessibleName = text.T(key) + " " + SessionStore.FormatColor(color);
            colorTips.SetToolTip(current, current.AccessibleName);
            current.Visible = !choices.Any(choice => choice.ToArgb() == color.ToArgb());
        }
    }

    private static Color[] Colors(params string[] values) => values.Select(SessionStore.ParseColor).ToArray();

    private sealed class ExpandedColorDialog : ColorDialog
    {
        protected override nint HookProc(nint hWnd, int msg, nint wparam, nint lparam)
        {
            // FullOpen already exposes the custom controls, so the disabled expansion button is misleading.
            if (msg == 0x0110) ShowWindow(GetDlgItem(hWnd, 719), 0);
            return base.HookProc(hWnd, msg, wparam, lparam);
        }

        [DllImport("user32.dll")]
        private static extern nint GetDlgItem(nint dialog, int id);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint window, int command);
    }

    private sealed class SwatchButton : Button
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Color Color { get; set; }
        internal bool IsAccent { get; }
        internal Color? PairBackground { get; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Selected { get; set; }

        internal SwatchButton(Color color, bool accent, Color? pairBackground = null)
        {
            Color = color;
            IsAccent = accent;
            PairBackground = pairBackground;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = DeviceDpi / 96f;
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var circle = new RectangleF(5 * scale, 5 * scale, Width - 11 * scale, Height - 11 * scale);
            using var fill = new SolidBrush(Color);
            using var edge = new Pen(Theme.Muted, scale);
            if (PairBackground is { } background)
            {
                using var backgroundFill = new SolidBrush(background);
                e.Graphics.FillPie(backgroundFill, circle.X, circle.Y, circle.Width, circle.Height, 90, 180);
                e.Graphics.FillPie(fill, circle.X, circle.Y, circle.Width, circle.Height, 270, 180);
            }
            else e.Graphics.FillEllipse(fill, circle);
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

    internal readonly record struct PreviewLayout(Rectangle Bounds, Rectangle Title, Rectangle SidebarSelected,
        Rectangle SidebarSecondary, Rectangle FileCard, Rectangle FileHeader, Rectangle FirstRow,
        Rectangle SelectedRow, Rectangle LastRow, Rectangle Link);

    internal static PreviewLayout CalculatePreviewLayout(Size size, int dpi)
    {
        int S(int value) => (int)Math.Round(value * dpi / 96d);
        int inset = S(12), top = S(36), sidebar = S(146), row = S(28);
        var card = Rectangle.FromLTRB(sidebar + inset, top, size.Width - inset, size.Height - inset);
        var first = new Rectangle(card.Left + S(6), card.Top + S(36), card.Width - S(12), row);
        return new PreviewLayout(new Rectangle(Point.Empty, size),
            new Rectangle(inset, 0, size.Width - inset * 2, top),
            new Rectangle(S(8), top + S(8), sidebar - S(16), row),
            new Rectangle(S(16), top + S(40), sidebar - S(24), row),
            card, new Rectangle(card.Left + inset, card.Top + S(4), card.Width - inset * 2, row),
            first, new Rectangle(first.Left, first.Bottom, first.Width, row),
            new Rectangle(first.Left, first.Bottom + row, first.Width, row),
            new Rectangle(card.Left + inset, card.Bottom - S(40), card.Width - inset * 2, row));
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
            var bounds = CalculatePreviewLayout(ClientSize, DeviceDpi);
            e.Graphics.Clear(colors.Canvas);
            Fill(bounds.FileCard, colors.Surface);
            Draw(text.T("Custom colors"), bounds.Title, colors.Text);
            Fill(bounds.SidebarSelected, colors.Selected);
            Draw(text.T("Favorites"), Rectangle.Inflate(bounds.SidebarSelected, -S(8), 0), colors.Accent);
            Draw(text.T("Folder tree"), bounds.SidebarSecondary, colors.Muted);
            Draw(text.T("Name"), bounds.FileHeader, colors.Muted);
            DrawRow(bounds.FirstRow, text.T("Documents"), text.T("Folder"));
            Fill(bounds.SelectedRow, colors.Selected);
            DrawRow(bounds.SelectedRow, "Project.txt", "12 KB");
            DrawRow(bounds.LastRow, "Notes.txt", "12 KB");
            Draw(text.T("Open in new tab"), bounds.Link, colors.Accent);
            Outline(bounds.FileCard, colors.Accent);
            Outline(new Rectangle(0, 0, Width - 1, Height - 1), colors.Border);

            void DrawRow(Rectangle row, string name, string detail)
            {
                Draw(name, new Rectangle(row.Left + S(8), row.Top, row.Width / 2 - S(8), row.Height), colors.Text);
                Draw(detail, new Rectangle(row.Left + row.Width / 2, row.Top, row.Width / 2 - S(8), row.Height), colors.Muted);
            }

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
