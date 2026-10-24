namespace QuadExplorer;

internal sealed class ShortcutHelpDialog : Form
{
    private readonly Panel content = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty,
        AccessibleName = "Help content"
    };
    private readonly Panel sheet = new() { Margin = Padding.Empty, BackColor = Theme.Surface };
    private readonly List<(Label? Key, Label Body)> rows = [];
    private readonly List<Label> headings = [];
    private readonly List<Button> sections = [];
    private readonly Font headingFont;
    private bool arranging;

    internal ShortcutHelpDialog(UiText text, Font font)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = font;
        content.Controls.Add(sheet);
        headingFont = font.Name == Theme.UiFace
            ? new Font(Theme.TitleFace, font.SizeInPoints + 2)
            : new Font(font.FontFamily, font.SizeInPoints + 2, FontStyle.Bold);
        Text = text.T("Folder Splitter shortcuts");
        AccessibleName = Text;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        Size = new Size(800, 680);
        MinimumSize = new Size(480, Math.Max(360, font.Height * 20));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(24), BackColor = Theme.Canvas
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = Text, Font = headingFont, AutoSize = true, Dock = DockStyle.Fill,
            UseMnemonic = false, Margin = new Padding(0, 0, 0, 12),
            AccessibleName = "Help title", BackColor = Theme.Canvas
        };
        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Margin = new Padding(0, 0, 0, 16), BackColor = Theme.Canvas,
            AccessibleName = "Help sections"
        };
        string[] paragraphs = text.Help.Replace("\r", "").Trim()
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        for (int section = 0; section < paragraphs.Length - 1; section++)
        {
            string[] lines = paragraphs[section].Split('\n');
            var heading = AddText(lines[0], "Help section " + section);
            heading.Font = headingFont;
            headings.Add(heading);
            rows.Add((null, heading));
            int index = section;
            var button = Theme.Button(lines[0], "Help navigate " + section, () => NavigateTo(index));
            button.Dock = DockStyle.None;
            button.AutoSize = true;
            button.Padding = new Padding(12, 4, 12, 4);
            button.Margin = new Padding(0, 0, 8, 4);
            sections.Add(button);
            navigation.Controls.Add(button);

            foreach (string line in lines.Skip(1))
            {
                int divider = line.IndexOf("   ", StringComparison.Ordinal);
                if (divider < 0)
                {
                    rows.Add((null, AddText(line, "Help note")));
                    continue;
                }
                var key = AddText(line[..divider], "Help shortcut");
                key.BackColor = Theme.Canvas;
                key.Padding = new Padding(8, 4, 8, 4);
                var description = AddText(line[divider..].TrimStart(), "Help description");
                rows.Add((key, description));
            }
        }
        var footer = AddText(paragraphs[^1], "Help footer");
        rows.Add((null, footer));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 16, 0, 0), BackColor = Theme.Canvas
        };
        var close = Theme.Button(text.T("Close"), "Close help", () =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }, 96);
        close.Dock = DockStyle.None;
        close.AutoSize = true;
        close.Padding = new Padding(16, 4, 16, 4);
        close.Margin = Padding.Empty;
        close.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(close);
        CancelButton = close;
        AcceptButton = close;
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(navigation, 0, 1);
        layout.Controls.Add(content, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        content.ClientSizeChanged += (_, _) => ArrangeContent();
        Theme.Apply(this);
        content.BackColor = Theme.Surface;
        SelectSection(0);
        ResumeLayout(true);
    }

    private Label AddText(string value, string name)
    {
        var label = new Label
        {
            Text = value, AccessibleName = name, UseMnemonic = false,
            AutoSize = false, Margin = Padding.Empty,
            ForeColor = name is "Help note" or "Help footer" ? Theme.Muted : Theme.Text,
            BackColor = Theme.Surface
        };
        sheet.Controls.Add(label);
        return label;
    }

    // A single four-pixel spacing scale follows the app's DPI, while text heights
    // come from the actual supplied font. Long shortcuts wrap rather than truncate.
    private int Space(int units) => (int)Math.Round(units * 4 * DeviceDpi / 96d);

    private void ArrangeContent()
    {
        if (arranging || content.ClientSize.Width <= 0) return;
        arranging = true;
        Point scroll = content.AutoScrollPosition;
        content.AutoScrollPosition = Point.Empty;
        sheet.SuspendLayout();
        try
        {
            int inset = Space(4), gap = Space(4);
            int width = Math.Max(1, content.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - inset * 2);
            int keyWidth = Math.Max(1, (width - gap) * 2 / 5);
            int bodyWidth = Math.Max(1, width - keyWidth - gap);
            bool stacked = width < Math.Max(Space(120), Font.Height * 24);
            int y = inset;
            foreach (var row in rows)
            {
                bool heading = headings.Contains(row.Body);
                bool footer = row.Body.AccessibleName == "Help footer";
                if ((heading && row.Body != headings[0]) || footer) y += Space(4);
                if (row.Key is { } key)
                {
                    if (stacked)
                    {
                        int keyHeight = key.GetPreferredSize(new Size(width, 0)).Height;
                        int bodyHeight = row.Body.GetPreferredSize(new Size(width, 0)).Height;
                        key.SetBounds(inset, y, width, keyHeight);
                        y += keyHeight + Space(1);
                        row.Body.SetBounds(inset, y, width, bodyHeight);
                        y += bodyHeight + Space(3);
                    }
                    else
                    {
                        int keyHeight = key.GetPreferredSize(new Size(keyWidth, 0)).Height;
                        int bodyHeight = row.Body.GetPreferredSize(new Size(bodyWidth, 0)).Height;
                        int height = Math.Max(keyHeight, bodyHeight);
                        key.SetBounds(inset, y, keyWidth, height);
                        row.Body.SetBounds(inset + keyWidth + gap, y, bodyWidth, height);
                        y += height + Space(2);
                    }
                }
                else
                {
                    int height = row.Body.GetPreferredSize(new Size(width, 0)).Height;
                    row.Body.SetBounds(inset, y, width, height);
                    y += height + Space(heading ? 3 : 2);
                }
            }
            sheet.SetBounds(0, 0, width + inset * 2, y + inset);
            content.AutoScrollMinSize = sheet.Size;
        }
        finally
        {
            sheet.ResumeLayout(true);
            content.PerformLayout();
            content.AutoScrollPosition = new Point(0, -scroll.Y);
            arranging = false;
        }
    }

    private void NavigateTo(int index)
    {
        content.AutoScrollPosition = new Point(0, headings[index].Top - Space(4));
        SelectSection(index);
    }

    private void SelectSection(int index)
    {
        for (int i = 0; i < sections.Count; i++)
        {
            sections[i].BackColor = i == index ? Theme.Selected : Theme.Canvas;
            sections[i].ForeColor = i == index ? Theme.Accent : Theme.Text;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Rectangle work = Screen.FromControl(Owner ?? this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
        Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        ArrangeContent();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ArrangeContent();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        int top = -content.AutoScrollPosition.Y;
        int page = Math.Max(1, content.ClientSize.Height - Space(4));
        int? next = keyData switch
        {
            Keys.PageDown => top + page,
            Keys.PageUp => Math.Max(0, top - page),
            Keys.Control | Keys.Home => 0,
            Keys.Control | Keys.End => content.AutoScrollMinSize.Height,
            _ => null
        };
        if (next is { } position)
        {
            content.AutoScrollPosition = new Point(0, position);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) headingFont.Dispose();
    }
}
