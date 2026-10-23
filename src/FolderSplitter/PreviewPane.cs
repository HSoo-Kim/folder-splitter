using System.Runtime.InteropServices;

namespace QuadExplorer;

internal sealed class PreviewPane : Panel
{
    private readonly UiText text;
    private readonly Label title = new()
    {
        Dock = DockStyle.Top, AutoEllipsis = true, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 0, 6, 0), AccessibleName = "Preview file name"
    };
    private readonly Label message = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12),
        AccessibleName = "Preview message"
    };
    private readonly Panel host = new() { Dock = DockStyle.Fill, Visible = false };
    private IPreviewHandler? handler;
    private string messageKey = "Select a file to preview it.";
    private object[] messageArguments = [];

    internal PreviewPane(UiText text)
    {
        this.text = text;
        AccessibleName = "Preview";
        Controls.Add(host);
        Controls.Add(message);
        Controls.Add(title);
        ShowMessage("Select a file to preview it.");
    }

    internal string? CurrentPath { get; private set; }
    internal bool HasHandler => handler is not null;
    internal string MessageText => message.Visible ? message.Text : "";
    internal event EventHandler? PreviewChanged;

    internal void ShowFile(string path)
    {
        if (string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase) && handler is not null) return;
        Unload();
        CurrentPath = path;
        title.Text = Path.GetFileName(path);
        if (PreviewHandlers.Find(Path.GetExtension(path)) is not { } id)
        {
            SetMessage("No preview is available for this file type.");
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
        title.Text = "";
        SetMessage(key, arguments);
        Changed();
    }

    internal void RefreshLanguage() => message.Text = text.T(messageKey, messageArguments);

    internal void ApplyColors()
    {
        BackColor = host.BackColor = message.BackColor = title.BackColor = Theme.Canvas;
        message.ForeColor = Theme.Muted;
        title.ForeColor = Theme.Text;
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
        if (handler is null) return;
        var current = handler;
        handler = null;
        try { current.Unload(); }
        catch (COMException) { }
        catch (InvalidComObjectException) { }
        Marshal.FinalReleaseComObject(current);
        host.Visible = false;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
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
}
