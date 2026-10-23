using System.Runtime.InteropServices;

namespace QuadExplorer;

internal sealed class PreviewPane : Panel
{
    private readonly UiText text;
    private readonly Label title = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 0, 6, 0), AccessibleName = "Preview file name"
    };
    private readonly Panel header = new() { Dock = DockStyle.Top, Height = 30 };
    private readonly Button detachButton;
    private readonly Button pinButton;
    private readonly Button windowButton;
    private bool pinned;
    private readonly Label message = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12),
        AccessibleName = "Preview message"
    };
    private readonly Panel host = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly PictureBox picture = new()
    {
        Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Visible = false, Padding = new Padding(8),
        AccessibleName = "Preview image"
    };
    private IPreviewHandler? handler;
    private PdfPreview? pdf;
    private Point? dragStart;
    private string messageKey = "Select a file to preview it.";
    private object[] messageArguments = [];

    internal PreviewPane(UiText text)
    {
        this.text = text;
        detachButton = Theme.Button(text.T("Detach preview"), "Detach preview",
            () => DetachRequested?.Invoke(this, EventArgs.Empty));
        detachButton.Dock = DockStyle.Right;
        detachButton.AutoSize = true;
        pinButton = Theme.Button(text.T("Pin"), "Pin preview", () => SetPinned(!pinned));
        pinButton.Dock = DockStyle.Right;
        pinButton.AutoSize = true;
        windowButton = Theme.Button(text.T("New window"), "Open preview in new window", () =>
        {
            if (CurrentPath is { } path) NewWindowRequested?.Invoke(path);
        });
        windowButton.Dock = DockStyle.Right;
        windowButton.AutoSize = true;
        AccessibleName = "Preview";
        Controls.Add(host);
        Controls.Add(picture);
        Controls.Add(message);
        header.Controls.Add(title);
        header.Controls.Add(windowButton);
        header.Controls.Add(pinButton);
        header.Controls.Add(detachButton);
        Controls.Add(header);
        host.Resize += (_, _) => ResizePreview();
        title.Cursor = Cursors.SizeAll;
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) dragStart = e.Location; };
        title.MouseUp += (_, _) => dragStart = null;
        title.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || dragStart is not { } start) return;
            Size threshold = SystemInformation.DragSize;
            if (new Rectangle(start.X - threshold.Width / 2, start.Y - threshold.Height / 2,
                threshold.Width, threshold.Height).Contains(e.Location)) return;
            dragStart = null;
            DragRequested?.Invoke(title.PointToScreen(e.Location));
        };
        ShowMessage("Select a file to preview it.");
    }

    internal string? CurrentPath { get; private set; }
    internal bool HasHandler => handler is not null;
    internal bool HasImage => picture.Image is not null || pdf?.HasImage == true;
    internal PdfPreview? Pdf => pdf;
    internal string MessageText => message.Visible ? message.Text : "";
    internal event EventHandler? PreviewChanged;
    internal event Action<Point>? DragRequested;
    internal event EventHandler? DetachRequested;
    internal event Action<string>? NewWindowRequested;
    internal event EventHandler? PinChanged;
    internal void SetFloating(bool floating) => detachButton.Visible = !floating;

    // A preview window that shows one file: no pinning, detaching or further windows.
    internal void SetStandalone() => detachButton.Visible = pinButton.Visible = windowButton.Visible = false;

    // A pinned preview keeps its file while other files are selected.
    internal bool Pinned => pinned;

    internal void SetPinned(bool value)
    {
        if (pinned == value) return;
        pinned = value;
        RefreshPinButton();
        PinChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPinButton()
    {
        pinButton.Text = text.T(pinned ? "Unpin" : "Pin");
        pinButton.BackColor = pinned ? Theme.Selected : Theme.Canvas;
        pinButton.ForeColor = pinned ? Theme.Accent : Theme.Text;
    }

    internal void ShowFile(string path)
    {
        if (string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase) &&
            (handler is not null || pdf is not null || picture.Image is not null)) return;
        Unload();
        CurrentPath = path;
        title.Text = Path.GetFileName(path);
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            pdf = new PdfPreview(text) { Dock = DockStyle.Fill };
            Controls.Add(pdf);
            pdf.BringToFront();
            message.Visible = false;
            pdf.Changed += (_, _) => Changed();
            _ = pdf.LoadAsync(path);
            Changed();
            return;
        }
        if (PreviewHandlers.Find(Path.GetExtension(path)) is not { } id)
        {
            int size = Math.Clamp(Math.Max(ClientSize.Width, ClientSize.Height - title.Height), 256, 1600);
            if (ShellThumbnail.Load(path, size) is { } image)
            {
                picture.Image = image;
                picture.Visible = true;
                message.Visible = false;
            }
            else SetMessage("No preview is available for this file type.");
            Changed();
            return;
        }
        try
        {
            handler = PreviewHandlers.Create(id, path);
            host.Visible = true;
            message.Visible = false;
            var bounds = HostBounds();
            handler.SetWindow(host.Handle, ref bounds);
            ApplyColors();
            handler.DoPreview();
        }
        catch (Exception error) when (error is COMException or InvalidCastException or UnauthorizedAccessException
            or FileNotFoundException or IOException)
        {
            Unload();
            CurrentPath = path;
            title.Text = Path.GetFileName(path);
            SetMessage("Could not show a preview of this file.");
        }
        Changed();
    }

    internal void ShowMessage(string key, params object[] arguments)
    {
        Unload();
        CurrentPath = null;
        title.Text = text.T("Preview");
        SetMessage(key, arguments);
        Changed();
    }

    internal void RefreshLanguage()
    {
        message.Text = text.T(messageKey, messageArguments);
        detachButton.Text = text.T("Detach preview");
        windowButton.Text = text.T("New window");
        RefreshPinButton();
        pdf?.RefreshLanguage();
        if (CurrentPath is null) title.Text = text.T("Preview");
    }

    internal bool RouteMouseWheel(ref Message message)
    {
        if (message.Msg is not (0x20A or 0x20E) || !Visible) return false;
        // Wheel coordinates are signed screen coordinates, including monitors left of the primary.
        var point = new Point(unchecked((short)(long)message.LParam),
            unchecked((short)((long)message.LParam >> 16)));
        if (pdf is not null && pdf.RectangleToScreen(pdf.ClientRectangle).Contains(point))
        {
            if (message.Msg == 0x20A) pdf.Wheel(unchecked((short)((long)message.WParam >> 16)));
            return true;
        }
        if (handler is null || !host.Visible) return false;
        if (!host.RectangleToScreen(host.ClientRectangle).Contains(point)) return false;
        if (ShellNative.IsChild(host.Handle, message.HWnd)) return false;

        nint target = host.Handle;
        while (true)
        {
            var client = point;
            ScreenToClient(target, ref client);
            nint child = ChildWindowFromPointEx(target, client, 0x1 | 0x2 | 0x4);
            if (child == 0 || child == target) break;
            target = child;
        }
        // A preview handler can live in prevhost.exe. Bound cross-process delivery and never move focus.
        if (target != host.Handle)
            SendMessageTimeout(target, (uint)message.Msg, message.WParam, message.LParam, 0x2, 200, out _);
        return true;
    }

    internal void ApplyColors()
    {
        BackColor = host.BackColor = picture.BackColor = message.BackColor = title.BackColor = header.BackColor = Theme.Canvas;
        message.ForeColor = Theme.Muted;
        title.ForeColor = Theme.Text;
        RefreshPinButton();
        pdf?.RefreshTheme();
        if (handler is IPreviewHandlerVisuals visuals)
        {
            try
            {
                visuals.SetBackgroundColor(ColorTranslator.ToWin32(Theme.Canvas));
                visuals.SetTextColor(ColorTranslator.ToWin32(Theme.Text));
            }
            catch (COMException) { }
        }
    }

    internal void Unload()
    {
        if (pdf is { } currentPdf)
        {
            pdf = null;
            currentPdf.Dispose();
        }
        if (picture.Image is { } image)
        {
            picture.Image = null;
            picture.Visible = false;
            image.Dispose();
        }
        if (handler is null) return;
        var current = handler;
        handler = null;
        try { current.Unload(); }
        catch (COMException) { }
        catch (InvalidComObjectException) { }
        Marshal.FinalReleaseComObject(current);
        host.Visible = false;
    }

    private void ResizePreview()
    {
        if (handler is null) return;
        var bounds = HostBounds();
        try { handler.SetRect(ref bounds); }
        catch (COMException) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Unload();
        base.Dispose(disposing);
    }

    private PreviewRect HostBounds() => new(0, 0, Math.Max(1, host.ClientSize.Width), Math.Max(1, host.ClientSize.Height));

    private void SetMessage(string key, params object[] arguments)
    {
        messageKey = key;
        messageArguments = arguments;
        host.Visible = false;
        message.Visible = true;
        RefreshLanguage();
    }

    private void Changed() => PreviewChanged?.Invoke(this, EventArgs.Empty);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint window, ref Point point);

    [DllImport("user32.dll")]
    private static extern nint ChildWindowFromPointEx(nint parent, Point point, uint flags);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam,
        uint flags, uint timeout, out nint result);
}
