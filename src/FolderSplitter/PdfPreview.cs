using System.Runtime.InteropServices;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace QuadExplorer;

internal sealed class PdfPreview : Panel
{
    private readonly UiText text;
    private readonly PictureBox image = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
    private readonly Label message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ToolStrip navigation = Theme.Strip();
    private readonly ToolStripButton previous = new() { AccessibleName = "Previous page" };
    private readonly ToolStripButton next = new() { AccessibleName = "Next page" };
    private readonly ToolStripLabel pageLabel = new() { AccessibleName = "PDF page number" };
    private FileStream? file;
    private IRandomAccessStream? input;
    private PdfDocument? document;
    private int requestedPage;
    private int renderedPage = -1;
    private int wheelRemainder;
    private bool working;
    private bool closed;
    private string? messageKey = "Loading preview...";

    internal int PageNumber => renderedPage + 1;
    internal int PageCount => (int)(document?.PageCount ?? 0);
    internal bool HasImage => image.Image is not null;
    internal Task Rendering { get; private set; } = Task.CompletedTask;
    internal event EventHandler? Changed;

    internal PdfPreview(UiText text)
    {
        this.text = text;
        AccessibleName = "PDF preview";
        TabStop = true;
        navigation.Dock = DockStyle.Bottom;
        navigation.Items.AddRange([previous, next, pageLabel]);
        previous.Click += (_, _) => ChangePage(-1);
        next.Click += (_, _) => ChangePage(1);
        image.MouseDown += (_, _) => Focus();
        Controls.Add(image);
        Controls.Add(message);
        Controls.Add(navigation);
        RefreshLanguage();
        RefreshTheme();
        UpdateNavigation();
    }

    internal Task LoadAsync(string path) => Rendering = LoadDocumentAsync(path);

    private async Task LoadDocumentAsync(string path)
    {
        working = true;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            input = file.AsRandomAccessStream();
            document = await PdfDocument.LoadFromStreamAsync(input);
            if (!closed) await DrawRequestedPageAsync();
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowError();
        }
        finally { FinishWork(); }
    }

    internal void Wheel(int delta)
    {
        if (document is null || closed) return;
        wheelRemainder += delta;
        int steps = wheelRemainder / 120;
        wheelRemainder %= 120;
        if (steps != 0) ChangePage(-steps);
    }

    private void ChangePage(int offset)
    {
        if (document is null || closed || PageCount == 0) return;
        int page = Math.Clamp(requestedPage + offset, 0, PageCount - 1);
        if (page == requestedPage) return;
        requestedPage = page;
        if (!working) Rendering = RenderAsync();
    }

    private async Task RenderAsync()
    {
        working = true;
        try { await DrawRequestedPageAsync(); }
        catch (Exception error) when (error is COMException or IOException or ArgumentException)
        {
            ShowError();
        }
        finally { FinishWork(); }
    }

    private async Task DrawRequestedPageAsync()
    {
        while (!closed && renderedPage != requestedPage)
        {
            int number = requestedPage;
            using var page = document!.GetPage((uint)number);
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Clamp(Math.Max(Width, 900), 900, 1600)
            });
            if (closed) return;
            if (number != requestedPage) continue;
            output.Seek(0);
            using var stream = output.AsStreamForRead();
            using var decoded = new Bitmap(stream);
            var bitmap = new Bitmap(decoded);
            image.Image?.Dispose();
            image.Image = bitmap;
            image.Visible = true;
            message.Visible = false;
            messageKey = null;
            renderedPage = number;
            UpdateNavigation();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void RefreshLanguage()
    {
        previous.Text = text.T("Previous page");
        next.Text = text.T("Next page");
        if (messageKey is not null) message.Text = text.T(messageKey);
    }

    internal void RefreshTheme()
    {
        Theme.Apply(this);
        BackColor = image.BackColor = message.BackColor = Theme.Canvas;
        message.ForeColor = Theme.Muted;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.PageDown) { ChangePage(1); return true; }
        if (keyData == Keys.PageUp) { ChangePage(-1); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateNavigation()
    {
        pageLabel.Text = PageCount == 0 ? "" : $"{PageNumber} / {PageCount}";
        previous.Enabled = PageNumber > 1;
        next.Enabled = PageNumber > 0 && PageNumber < PageCount;
    }

    private void ShowError()
    {
        if (closed) return;
        messageKey = "Could not show a preview of this file.";
        message.Text = text.T(messageKey);
        message.Visible = true;
        image.Visible = false;
        previous.Enabled = next.Enabled = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void FinishWork()
    {
        working = false;
        if (closed) ReleaseDocument();
    }

    private void ReleaseDocument()
    {
        document = null;
        input?.Dispose();
        input = null;
        file?.Dispose();
        file = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closed = true;
            image.Image?.Dispose();
            image.Image = null;
            // An in-flight render owns the stream until it completes; it cannot update a closed preview.
            if (!working) ReleaseDocument();
        }
        base.Dispose(disposing);
    }
}
