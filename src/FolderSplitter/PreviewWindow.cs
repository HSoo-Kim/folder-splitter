using System.Runtime.InteropServices;

namespace QuadExplorer;

internal sealed class PreviewWindow : Form
{
    private readonly UiText text;
    private readonly string titleKey;
    private readonly string dockKey;
    private readonly ToolStrip toolbar = Theme.Strip();
    private readonly ToolStripButton dockButton = new() { AccessibleName = "Dock preview" };
    private bool moving;

    internal event EventHandler? DockRequested;
    internal event Action<Point>? MoveFinished;

    internal PreviewWindow(UiText text, string titleKey = "Preview", string dockKey = "Dock preview")
    {
        this.text = text;
        this.titleKey = titleKey;
        this.dockKey = dockKey;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(260, 260);
        ShowInTaskbar = false;
        toolbar.Items.Add(dockButton);
        dockButton.Click += (_, _) => DockRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(toolbar);
        RefreshLanguage();
    }

    internal void AddPreview(PreviewPane preview)
        => AddContent(preview);

    internal void AddContent(Control preview)
    {
        Controls.Add(preview);
        preview.Dock = DockStyle.Fill;
        preview.BringToFront();
    }

    internal void RefreshLanguage()
    {
        Text = text.T(titleKey);
        dockButton.Text = text.T(dockKey);
        dockButton.AccessibleName = dockKey;
    }

    internal void RefreshTheme()
    {
        Theme.Apply(this);
    }

    internal void BeginMove(Point screenPoint)
    {
        Location = new Point(screenPoint.X - Width / 2, screenPoint.Y - 15);
        ReleaseCapture();
        nint coordinates = (nint)((screenPoint.X & 0xFFFF) | ((screenPoint.Y & 0xFFFF) << 16));
        SendMessage(Handle, 0xA1, 2, coordinates); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x112 && ((long)m.WParam & 0xFFF0) == 0xF010) moving = true; // SC_MOVE
        base.WndProc(ref m);
        if (m.Msg == 0x232 && moving)
        {
            moving = false;
            MoveFinished?.Invoke(Cursor.Position); // WM_EXITSIZEMOVE
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
