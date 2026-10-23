using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace QuadExplorer;

internal static class Theme
{
    private sealed record Palette(Color Canvas, Color Surface, Color Border, Color Text,
        Color Muted, Color Accent, Color Selected, Color ErrorText, Color Hover, Color Pressed, bool Dark = false,
        Color? SidebarColor = null, Color? ListColor = null, Color? SortColor = null);

    private static readonly Palette Light = new(
        Color.FromArgb(246, 247, 249), Color.White, Color.FromArgb(222, 226, 232),
        Color.FromArgb(32, 37, 44), Color.FromArgb(96, 104, 116), Color.FromArgb(0, 95, 184),
        Color.FromArgb(229, 241, 253), Color.FromArgb(150, 45, 35),
        Color.FromArgb(235, 243, 252), Color.FromArgb(212, 231, 250));
    private static readonly Palette Dark = new(
        Color.FromArgb(27, 30, 35), Color.FromArgb(35, 39, 45), Color.FromArgb(66, 73, 83),
        Color.FromArgb(237, 240, 245), Color.FromArgb(172, 181, 194), Color.FromArgb(112, 183, 255),
        Color.FromArgb(39, 65, 92), Color.FromArgb(255, 154, 143),
        Color.FromArgb(44, 53, 64), Color.FromArgb(49, 79, 110), Dark: true);
    // Softer choices for long sessions: warm beige, muted green, and a dark theme with less contrast.
    private static readonly Palette Beige = new(
        Color.FromArgb(240, 234, 222), Color.FromArgb(250, 246, 238), Color.FromArgb(222, 212, 194),
        Color.FromArgb(58, 50, 40), Color.FromArgb(118, 106, 90), Color.FromArgb(150, 92, 36),
        Color.FromArgb(238, 226, 204), Color.FromArgb(150, 45, 35),
        Color.FromArgb(244, 236, 222), Color.FromArgb(232, 220, 198));
    private static readonly Palette Green = new(
        Color.FromArgb(230, 238, 230), Color.FromArgb(242, 247, 241), Color.FromArgb(205, 220, 204),
        Color.FromArgb(36, 52, 40), Color.FromArgb(92, 112, 96), Color.FromArgb(40, 118, 74),
        Color.FromArgb(220, 236, 222), Color.FromArgb(150, 45, 35),
        Color.FromArgb(230, 242, 231), Color.FromArgb(210, 230, 212));
    private static readonly Palette Dim = new(
        Color.FromArgb(40, 42, 46), Color.FromArgb(48, 51, 56), Color.FromArgb(70, 74, 80),
        Color.FromArgb(200, 203, 208), Color.FromArgb(150, 155, 162), Color.FromArgb(125, 170, 215),
        Color.FromArgb(58, 72, 88), Color.FromArgb(230, 150, 140),
        Color.FromArgb(56, 62, 70), Color.FromArgb(64, 80, 98), Dark: true);
    // Mixed palettes give the sidebar, file list and sorted column their own colors. Color values follow
    // the public Solarized, Everforest, Gruvbox, Rosé Pine, Catppuccin and Nord palettes.
    private static readonly Palette Solarized = new(
        Color.FromArgb(238, 232, 213), Color.FromArgb(253, 246, 227), Color.FromArgb(221, 214, 193),
        Color.FromArgb(88, 110, 117), Color.FromArgb(123, 138, 139), Color.FromArgb(38, 139, 210),
        Color.FromArgb(228, 220, 195), Color.FromArgb(220, 50, 47),
        Color.FromArgb(230, 223, 200), Color.FromArgb(221, 213, 188), Dark: false,
        SidebarColor: Color.FromArgb(238, 232, 213), ListColor: Color.FromArgb(253, 246, 227), SortColor: Color.FromArgb(245, 238, 216));
    private static readonly Palette Everforest = new(
        Color.FromArgb(239, 235, 212), Color.FromArgb(244, 240, 217), Color.FromArgb(224, 220, 199),
        Color.FromArgb(92, 106, 114), Color.FromArgb(130, 145, 129), Color.FromArgb(53, 167, 124),
        Color.FromArgb(223, 229, 189), Color.FromArgb(208, 62, 58),
        Color.FromArgb(232, 229, 201), Color.FromArgb(223, 229, 189), Dark: false,
        SidebarColor: Color.FromArgb(230, 233, 200), ListColor: Color.FromArgb(253, 246, 227), SortColor: Color.FromArgb(244, 240, 217));
    private static readonly Palette Gruvbox = new(
        Color.FromArgb(235, 219, 178), Color.FromArgb(249, 245, 215), Color.FromArgb(213, 196, 161),
        Color.FromArgb(60, 56, 54), Color.FromArgb(124, 111, 100), Color.FromArgb(175, 58, 3),
        Color.FromArgb(230, 213, 168), Color.FromArgb(157, 0, 6),
        Color.FromArgb(227, 210, 166), Color.FromArgb(213, 196, 161), Dark: false,
        SidebarColor: Color.FromArgb(242, 229, 188), ListColor: Color.FromArgb(251, 241, 199), SortColor: Color.FromArgb(242, 229, 188));
    private static readonly Palette RosePine = new(
        Color.FromArgb(242, 233, 225), Color.FromArgb(250, 244, 237), Color.FromArgb(223, 218, 217),
        Color.FromArgb(87, 82, 121), Color.FromArgb(121, 117, 147), Color.FromArgb(180, 99, 122),
        Color.FromArgb(235, 224, 214), Color.FromArgb(180, 99, 122),
        Color.FromArgb(235, 224, 214), Color.FromArgb(223, 212, 203), Dark: false,
        SidebarColor: Color.FromArgb(242, 233, 225), ListColor: Color.FromArgb(255, 250, 243), SortColor: Color.FromArgb(250, 244, 237));
    private static readonly Palette Latte = new(
        Color.FromArgb(230, 233, 239), Color.FromArgb(246, 247, 250), Color.FromArgb(204, 208, 218),
        Color.FromArgb(76, 79, 105), Color.FromArgb(124, 127, 147), Color.FromArgb(136, 57, 239),
        Color.FromArgb(204, 208, 218), Color.FromArgb(210, 15, 57),
        Color.FromArgb(221, 225, 234), Color.FromArgb(204, 208, 218), Dark: false,
        SidebarColor: Color.FromArgb(220, 224, 232), ListColor: Color.FromArgb(239, 241, 245), SortColor: Color.FromArgb(233, 235, 241));
    private static readonly Palette EverforestDark = new(
        Color.FromArgb(39, 46, 51), Color.FromArgb(58, 70, 76), Color.FromArgb(71, 82, 88),
        Color.FromArgb(211, 198, 170), Color.FromArgb(157, 169, 160), Color.FromArgb(167, 192, 128),
        Color.FromArgb(66, 80, 71), Color.FromArgb(230, 126, 128),
        Color.FromArgb(61, 72, 77), Color.FromArgb(71, 82, 88), Dark: true,
        SidebarColor: Color.FromArgb(52, 63, 68), ListColor: Color.FromArgb(45, 53, 59), SortColor: Color.FromArgb(52, 63, 68));
    private static readonly Palette Nord = new(
        Color.FromArgb(46, 52, 64), Color.FromArgb(59, 66, 82), Color.FromArgb(67, 76, 94),
        Color.FromArgb(216, 222, 233), Color.FromArgb(154, 165, 184), Color.FromArgb(136, 192, 208),
        Color.FromArgb(67, 76, 94), Color.FromArgb(229, 147, 155),
        Color.FromArgb(59, 66, 82), Color.FromArgb(76, 86, 106), Dark: true,
        SidebarColor: Color.FromArgb(59, 66, 82), ListColor: Color.FromArgb(46, 52, 64), SortColor: Color.FromArgb(53, 60, 74));
    private static readonly Palette Mocha = new(
        Color.FromArgb(24, 24, 37), Color.FromArgb(49, 50, 68), Color.FromArgb(69, 71, 90),
        Color.FromArgb(205, 214, 244), Color.FromArgb(166, 173, 200), Color.FromArgb(203, 166, 247),
        Color.FromArgb(49, 50, 68), Color.FromArgb(243, 139, 168),
        Color.FromArgb(49, 50, 68), Color.FromArgb(69, 71, 90), Dark: true,
        SidebarColor: Color.FromArgb(30, 30, 46), ListColor: Color.FromArgb(30, 30, 46), SortColor: Color.FromArgb(36, 36, 58));
    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["light"] = Light, ["dark"] = Dark, ["beige"] = Beige, ["green"] = Green, ["dim"] = Dim,
        ["solarized"] = Solarized, ["everforest"] = Everforest, ["gruvbox"] = Gruvbox, ["rosepine"] = RosePine,
        ["latte"] = Latte, ["everforest-dark"] = EverforestDark, ["nord"] = Nord, ["mocha"] = Mocha
    };
    private static Palette current = Light;
    private static readonly FlatRenderer Renderer = new();
    private const int CornerRadius = 7;
    private const int PaintInset = 2;
    private static readonly Color FolderBack = Color.FromArgb(205, 145, 36);
    private static readonly Color FolderFront = Color.FromArgb(245, 192, 66);

    // Segoe Fluent Icons ships only with Windows 11; Windows 10 has the same glyphs in Segoe MDL2 Assets.
    public static readonly string IconFont = new System.Drawing.Text.InstalledFontCollection().Families
        .Any(family => family.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static Color Canvas => current.Canvas;
    public static Color Surface => current.Surface;
    public static Color Border => current.Border;
    public static Color Text => current.Text;
    public static Color Muted => current.Muted;
    public static Color Accent => current.Accent;
    public static Color Selected => current.Selected;
    public static Color ErrorText => current.ErrorText;
    public static bool IsDark => current.Dark;
    // Font for the Shell file lists; null keeps Windows' own list font.
    public static (string Face, float Points)? ListFont { get; set; }
    public static Color Sidebar => current.SidebarColor ?? Canvas;
    public static Color ListBack => current.ListColor ?? Surface;
    public static Color SortColumn => current.SortColor ?? Canvas;
    // Strips tagged with this show a faint rounded outline around every button, so their commands stand out.
    public static readonly object OutlineItems = new();

    public static void Set(string mode)
    {
        current = Palettes.TryGetValue(mode?.Trim().ToLowerInvariant() ?? "", out var palette) ? palette
            : throw new ArgumentException("Unknown color theme.", nameof(mode));
    }

    // Does not create handles, activate a control, or change any system theme setting.
    public static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.IsDisposed) return;
        ApplyControl(root);
        if (root.IsHandleCreated) ApplyNative(root.Handle);
    }

    private static void ApplyControl(Control control)
    {
        control.BackColor = Recolor(control.BackColor, false,
            control is Form or ToolStrip ? Canvas : Surface);
        control.ForeColor = Recolor(control.ForeColor, true, Text);
        control.HandleCreated -= HandleCreated;
        control.HandleCreated += HandleCreated;
        control.ControlAdded -= ControlAdded;
        control.ControlAdded += ControlAdded;
        if (control is Button button)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = current.Hover;
            button.FlatAppearance.MouseDownBackColor = current.Pressed;
        }
        if (control is ToolStrip strip) ApplyStrip(strip);
        if (control.ContextMenuStrip is { } menu)
        {
            ApplyStrip(menu);
            menu.Opening -= MenuOpening;
            menu.Opening += MenuOpening;
        }
        foreach (Control child in control.Controls) ApplyControl(child);
        control.Invalidate();
    }

    private static void HandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control { IsHandleCreated: true, IsDisposed: false } control)
            ApplyNative(control.Handle);
    }

    private static void ControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is { } child) Apply(child);
    }

    private static void MenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is ToolStrip menu) ApplyStrip(menu);
    }

    private static void ApplyStrip(ToolStrip strip)
    {
        strip.Renderer = Renderer;
        strip.BackColor = strip is ToolStripDropDown ? Surface : Canvas;
        strip.ForeColor = Text;
        foreach (ToolStripItem item in strip.Items)
        {
            item.BackColor = strip.BackColor;
            item.ForeColor = Recolor(item.ForeColor, true, Text);
            if (item is ToolStripControlHost host) ApplyControl(host.Control);
            if (item is ToolStripDropDownItem { HasDropDownItems: true } dropDown)
                ApplyStrip(dropDown.DropDown);
            item.Invalidate();
        }
        strip.Invalidate();
    }

    private static Color Recolor(Color value, bool foreground, Color fallback)
    {
        foreach (Palette palette in Palettes.Values)
        {
            int argb = value.ToArgb();
            if (foreground)
            {
                if (argb == palette.Muted.ToArgb()) return Muted;
                if (argb == palette.ErrorText.ToArgb()) return ErrorText;
                if (argb == palette.Accent.ToArgb()) return Accent;
                if (argb == palette.Text.ToArgb()) return Text;
            }
            else
            {
                if (argb == palette.Canvas.ToArgb()) return Canvas;
                if (argb == palette.Surface.ToArgb()) return Surface;
                if (argb == palette.Border.ToArgb()) return Border;
                if (argb == palette.Accent.ToArgb()) return Accent;
                if (argb == palette.Selected.ToArgb()) return Selected;
            }
        }
        return value.IsSystemColor || value.IsEmpty ? fallback : value;
    }

    // Only this process's supplied HWND and its children are touched. ExplorerBrowser
    // can use DirectUI rather than SysListView32: its private painting, scrollbars and
    // Shell-owned popup menus are OS-dependent and need native capture verification.
    // DarkMode_Explorer/ItemsView are best-effort window theme classes, not a promise
    // that the OS implements dark painting for every hosted Shell extension.
    public static void ApplyNative(nint root)
    {
        if (!OwnsWindow(root)) return;
        ApplyWindow(root);
        Native.EnumChildWindows(root, (window, _) =>
        {
            if (Native.IsChild(root, window) && OwnsWindow(window)) ApplyWindow(window);
            return true;
        }, 0);
        Native.RedrawWindow(root, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0400);
    }

    private static bool OwnsWindow(nint window) =>
        window != 0 && Native.GetWindowThreadProcessId(window, out uint process) != 0
        && process == (uint)Environment.ProcessId;

    private static void ApplyWindow(nint window)
    {
        var name = new StringBuilder(256);
        Native.GetClassName(window, name, name.Capacity);
        string className = name.ToString();
        string theme = IsDark ? "DarkMode_Explorer" : "Explorer";
        // Shell folder lists and their headers use the ItemsView class, whose dark variant also
        // darkens the sorted-column highlight; DarkMode_Explorer leaves that column white.
        if (className.Equals("SysHeader32", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
            theme = IsDark ? "DarkMode_ItemsView" : "ItemsView";
        Native.SetWindowTheme(window, theme, null);
        if (className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            Native.SendMessage(window, 0x1001, 0, ColorRef(ListBack)); // LVM_SETBKCOLOR
            Native.SendMessage(window, 0x1024, 0, ColorRef(Text)); // LVM_SETTEXTCOLOR
            Native.SendMessage(window, 0x1026, 0, ColorRef(ListBack)); // LVM_SETTEXTBKCOLOR
        }
        else if (className.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase))
        {
            Native.SendMessage(window, 0x111D, 0, ColorRef(Surface)); // TVM_SETBKCOLOR
            Native.SendMessage(window, 0x111E, 0, ColorRef(Text)); // TVM_SETTEXTCOLOR
        }
        if (Native.GetAncestor(window, 2) != window) return; // GA_ROOT, never an ancestor.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int dark = IsDark ? 1 : 0;
            Native.DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
            int caption = ColorRef(Canvas), text = ColorRef(Text), border = ColorRef(Border);
            Native.DwmSetWindowAttribute(window, 35, ref caption, sizeof(int));
            Native.DwmSetWindowAttribute(window, 36, ref text, sizeof(int));
            Native.DwmSetWindowAttribute(window, 34, ref border, sizeof(int));
        }
    }

    private static Color Blend(Color a, Color b) => Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
    private static int Scale(int value, int dpi) => Math.Max(1, (int)Math.Round(value * dpi / 96d));

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Button Button(string text, string accessibleName, Action action, int width = 34)
    {
        var button = new RoundedButton
        {
            Text = text, AccessibleName = accessibleName, Width = width, Height = 30,
            Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2), BackColor = Surface, ForeColor = Text,
            UseVisualStyleBackColor = false, TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Selected;
        button.Click += (_, _) => action();
        return button;
    }

    public static ToolStrip Strip(bool outlineItems = false) => new()
    {
        Tag = outlineItems ? OutlineItems : null,
        GripStyle = ToolStripGripStyle.Hidden, BackColor = Canvas, ForeColor = Text,
        Renderer = Renderer, Padding = new Padding(6, 3, 6, 3),
        AutoSize = true, Stretch = true, CanOverflow = true
    };

    public static Image FolderIcon()
    {
        var image = new Bitmap(20, 20);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var back = new SolidBrush(FolderBack);
        using var front = new SolidBrush(FolderFront);
        g.FillRectangle(back, 2, 4, 8, 5);
        g.FillRectangle(back, 2, 7, 16, 10);
        g.FillRectangle(front, 2, 8, 16, 9);
        return image;
    }

    private sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size preferred = base.GetPreferredSize(proposedSize);
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            int inset = Theme.Scale(PaintInset, DeviceDpi) * 2;
            return new Size(Math.Max(preferred.Width, text.Width + Padding.Horizontal + inset),
                Math.Max(preferred.Height, text.Height + Padding.Vertical + inset));
        }

        protected override void OnMouseEnter(EventArgs e)
        { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e)
        { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) pressed = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) pressed = true; Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e)
        { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e)
        { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e)
        { base.OnLostFocus(e); pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        { base.OnEnabledChanged(e); pressed = false; Invalidate(); }

        // ButtonBase is an opaque control, so WinForms skips OnPaintBackground; OnPaint fills the
        // corners outside the rounded shape itself, using the nearest opaque ancestor's color.
        private Color CornerColor()
        {
            for (Control? parent = Parent; parent is not null; parent = parent.Parent)
                if (parent.BackColor.A == 255) return parent.BackColor;
            return Canvas;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            e.Graphics.Clear(CornerColor());
            int inset = Theme.Scale(PaintInset, DeviceDpi);
            var bounds = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using var shape = Rounded(bounds, Theme.Scale(CornerRadius, DeviceDpi));
            Color fill = !Enabled ? BackColor : pressed && (hovered || Focused)
                ? current.Pressed : hovered ? current.Hover : BackColor;
            var smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
            using (var pen = new Pen(Enabled && (Focused || IsDefault) ? Accent : Border))
                e.Graphics.DrawPath(pen, shape);
            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(bounds, -inset, -inset);
                using var focusShape = Rounded(focus, Theme.Scale(CornerRadius - PaintInset, DeviceDpi));
                using var focusPen = new Pen(Accent) { DashStyle = DashStyle.Dot };
                e.Graphics.DrawPath(focusPen, focusShape);
            }
            e.Graphics.SmoothingMode = smoothing;
            // Retain Button's preferred-size contract, padding, mnemonics and RTL.
            // No Region is assigned: the full rectangular hit target stays intact.
            var content = new Rectangle(Padding.Left + inset, Padding.Top + inset,
                Math.Max(0, Width - Padding.Horizontal - inset * 2),
                Math.Max(0, Height - Padding.Vertical - inset * 2));
            if (Image is { } image)
            {
                var imageBounds = new Rectangle(content.Left + (content.Width - image.Width) / 2,
                    content.Top + (content.Height - image.Height) / 2, image.Width, image.Height);
                if (Enabled) e.Graphics.DrawImage(image, imageBounds);
                else ControlPaint.DrawImageDisabled(e.Graphics, image, imageBounds.X, imageBounds.Y, fill);
            }
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
            flags |= TextAlign switch
            {
                ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
                _ => TextFormatFlags.HorizontalCenter
            };
            flags |= TextAlign switch
            {
                ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
                ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
                _ => TextFormatFlags.VerticalCenter
            };
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(e.Graphics, Text, Font, content, Enabled ? ForeColor : Muted, flags);
        }
    }

    private sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        public FlatRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => PaintItem(e);
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => PaintItem(e);
        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e) => PaintItem(e);
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => PaintItem(e);

        private static void PaintItem(ToolStripItemRenderEventArgs e)
        {
            bool isChecked = e.Item is ToolStripButton { Checked: true } or ToolStripMenuItem { Checked: true };
            bool active = e.Item.Enabled && (e.Item.Selected || e.Item.Pressed);
            // Folder tabs keep a faint outline when idle so neighbouring tabs read as separate items.
            bool tab = e.Item is FolderTabButton;
            bool outlined = e.Item is OutlinedToolStripButton || ReferenceEquals(e.ToolStrip?.Tag, OutlineItems);
            bool outlineOnly = (outlined || tab) && !isChecked && !active;
            if (!isChecked && !active && !outlineOnly) return;
            int dpi = e.ToolStrip?.DeviceDpi ?? 96;
            var bounds = new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(bounds, Scale(CornerRadius, dpi));
            using var brush = new SolidBrush(!e.Item.Enabled ? Surface
                : e.Item.Pressed ? current.Pressed : isChecked ? Selected : current.Hover);
            using var pen = new Pen(e.Item.Enabled && (isChecked || e.Item.Pressed) ? Accent
                : outlineOnly ? (tab ? Border : Blend(Border, Muted)) : tab ? Blend(Border, Muted) : Border);
            if (!outlineOnly) e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            e.Graphics.SmoothingMode = smoothing;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Recolor(e.Item.ForeColor, true, Text) : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? Muted : Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            if (e.Vertical) e.Graphics.DrawLine(pen, e.Item.Width / 2, 3, e.Item.Width / 2, e.Item.Height - 4);
            else e.Graphics.DrawLine(pen, 4, e.Item.Height / 2, e.Item.Width - 5, e.Item.Height / 2);
        }
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
        public override Color ButtonCheckedGradientBegin => Selected;
        public override Color ButtonCheckedGradientMiddle => Selected;
        public override Color ButtonCheckedGradientEnd => Selected;
        public override Color ButtonCheckedHighlight => Selected;
        public override Color ButtonPressedGradientBegin => Selected;
        public override Color ButtonPressedGradientMiddle => Selected;
        public override Color ButtonPressedGradientEnd => Selected;
        public override Color ButtonSelectedBorder => Border;
        public override Color ButtonPressedBorder => Accent;
        public override Color ButtonCheckedHighlightBorder => Border;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color MenuItemSelectedGradientBegin => Selected;
        public override Color MenuItemSelectedGradientEnd => Selected;
        public override Color MenuItemPressedGradientBegin => current.Pressed;
        public override Color MenuItemPressedGradientMiddle => current.Pressed;
        public override Color MenuItemPressedGradientEnd => current.Pressed;
        public override Color CheckBackground => Selected;
        public override Color CheckSelectedBackground => Selected;
        public override Color CheckPressedBackground => current.Pressed;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }

    private static class Native
    {
        public delegate bool EnumWindow(nint window, nint parameter);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(nint parent, nint window);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(nint window, StringBuilder name, int count);
        [DllImport("user32.dll")]
        public static extern nint GetAncestor(nint window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(nint window, string subAppName, string? subIdList);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
    }
}

// A toolbar button that keeps its rounded outline when idle, so a lone icon such as "+" stays easy to find.
internal sealed class OutlinedToolStripButton(string text) : ToolStripButton(text);
