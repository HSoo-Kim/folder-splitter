using QuadExplorer;
using System.Text;

internal static partial class Program
{
    private static void WritePdfFixture(string path)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>"
        };
        for (int page = 1; page <= 3; page++)
        {
            int content = objects.Count + 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 9 0 R >> >> /Contents {content} 0 R >>");
            string stream = $"BT /F1 36 Tf 50 680 Td (PREVIEW PAGE {page}) Tj ET\n";
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var bytes = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(bytes.Length);
            bytes.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int start = bytes.Length;
        bytes.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (int offset in offsets.Skip(1)) bytes.Append($"{offset:0000000000} 00000 n \n");
        bytes.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
        File.WriteAllText(path, bytes.ToString(), Encoding.ASCII);
    }

    private static void RunPdfScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "PDF")).FullName;
        string path = Path.Combine(folder, "three-pages.pdf");
        WritePdfFixture(path);
        string session = Path.Combine(root, "pdf.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "ko", ExplorerFavoritesImported = true, ShowPreview = true,
            ShowSidebar = false, PreviewWidth = 500, WindowWidth = 1440, WindowHeight = 900,
            Panes = [new PaneState { Tabs = [folder] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                if (!pane.ActiveView!.HasNavigated) await NavigationSignal(pane.ActiveView).WaitAsync(TimeSpan.FromSeconds(20));
                async Task Page(int number, Action trigger)
                {
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Changed(object? sender, EventArgs e)
                    {
                        if (form.Preview.Pdf is { HasImage: true } pdf && pdf.PageNumber == number) ready.TrySetResult();
                    }
                    form.Preview.PreviewChanged += Changed;
                    try
                    {
                        trigger();
                        Changed(null, EventArgs.Empty);
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    }
                    finally { form.Preview.PreviewChanged -= Changed; }
                }
                void Wheel(int delta)
                {
                    var point = form.Preview.PointToScreen(new Point(form.Preview.Width / 2, form.Preview.Height / 2));
                    var message = Message.Create(pane.ActiveView!.Handle, 0x20A, delta << 16,
                        (nint)((point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16)));
                    Check(form.PreFilterMessage(ref message), "wheel over a PDF is handled by the preview");
                }
                var selected = SelectionSignal(pane.ActiveView);
                await Navigate(pane.ActiveView, () => pane.ActiveView.NavigateAndSelect(path));
                await selected.WaitAsync(TimeSpan.FromSeconds(10));
                await Page(1, form.UpdatePreview);
                var pdf = form.Preview.Pdf!;
                Check(pdf.PageCount == 3 && form.Preview.HasImage, "PDF preview renders the first of three real pages");
                CapturePreview(form, "pdf-docked-light");
                pane.FocusFiles();
                Wheel(-60);
                Check(pdf.PageNumber == 1, "high-resolution wheel waits for a complete notch");
                await Page(2, () => Wheel(-60));
                await Page(3, () => Wheel(-120));
                Wheel(-120);
                Check(pdf.PageNumber == 3, "PDF wheel stops at the last page");
                CapturePreview(form, "pdf-page-three");
                await Page(2, () => Wheel(120));
                Wheel(-120);
                Wheel(120);
                await pdf.Rendering.WaitAsync(TimeSpan.FromSeconds(15));
                Check(pdf.PageNumber == 2, "rapid wheel changes keep the latest requested PDF page");
                form.SetPreviewFloating(true);
                Check(ReferenceEquals(form.Preview.Pdf, pdf) && pdf.PageNumber == 2, "detaching preserves the rendered PDF and page");
                form.FloatingPreview!.Size = new Size(520, 760);
                var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnIdle(object? sender, EventArgs args) { Application.Idle -= OnIdle; idle.TrySetResult(); }
                Application.Idle += OnIdle;
                await idle.Task.WaitAsync(TimeSpan.FromSeconds(10));
                CapturePreview(form.FloatingPreview, "pdf-floating-light");
                await Page(1, () => Wheel(120));
                form.SetColorTheme("dark");
                CapturePreview(form.FloatingPreview, "pdf-floating-dark");
                form.SetPreviewFloating(false);
                Check(ReferenceEquals(form.Preview.Pdf, pdf) && pdf.PageNumber == 1, "docking preserves the rendered PDF and page");
                form.Size = new Size(1000, 700);
                CapturePreview(form, "pdf-docked-dark");
                var next = Descendants(pdf).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                    .Single(item => item.AccessibleName == "Next page");
                await Page(2, next.PerformClick);
                Check(pdf.PageNumber == 2, "PDF page button also navigates without the mouse wheel");
                await pdf.Rendering.WaitAsync(TimeSpan.FromSeconds(15));
                form.OpenPreviewWindow(path);
                var own = form.PreviewWindows[^1];
                await own.Pdf!.Rendering.WaitAsync(TimeSpan.FromSeconds(15));
                var inside = own.PointToScreen(new Point(own.Width / 2, own.Height / 2));
                var ownWheel = Message.Create(pane.ActiveView!.Handle, 0x20A, -120 << 16, (nint)((inside.X & 0xFFFF) | ((inside.Y & 0xFFFF) << 16)));
                Check(form.PreFilterMessage(ref ownWheel), "wheel over a preview window is handled by that window");
                await own.Pdf.Rendering.WaitAsync(TimeSpan.FromSeconds(15));
                Check(own.Pdf.PageNumber == 2 && pdf.PageNumber == 2, "wheel pages through the PDF in its own window only");
                own.FindForm()!.Close();
                // Closing an owned window hands activation back asynchronously; make the main window active again.
                form.Activate();
                pane.FocusFiles();
                Wheel(-120);
                form.SetPreviewVisible(false);
                await pdf.Rendering.WaitAsync(TimeSpan.FromSeconds(15));
                Check(pdf.IsDisposed && form.Preview.Pdf is null, "closing a PDF during rendering cannot restore a stale preview");
                File.Move(path, path + ".moved");
                Check(File.Exists(path + ".moved"), "closed PDF preview does not prevent moving the file");
                string invalid = Path.Combine(folder, "invalid.pdf");
                File.WriteAllText(invalid, "not a PDF");
                using var broken = new PdfPreview(new UiText("en")) { Dock = DockStyle.Fill };
                using var states = new Form { Size = new Size(500, 700), ShowInTaskbar = false, Text = "PDF preview states" };
                states.Controls.Add(broken);
                states.Show(form);
                var statesIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnStatesIdle(object? sender, EventArgs args) { Application.Idle -= OnStatesIdle; statesIdle.TrySetResult(); }
                Application.Idle += OnStatesIdle;
                await statesIdle.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(!Descendants(broken).OfType<PictureBox>().Single().Visible &&
                    Descendants(broken).OfType<Label>().Any(label => label.Visible && label.Text.Length > 0),
                    "PDF loading message is not covered by the image canvas");
                CapturePreview(states, "pdf-loading");
                await broken.LoadAsync(invalid).WaitAsync(TimeSpan.FromSeconds(15));
                Check(!broken.HasImage && Descendants(broken).OfType<Label>().Any(label => label.Visible && label.Text.Length > 0),
                    "invalid PDF shows an error without stopping the app");
                CapturePreview(states, "pdf-error");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }

    private static void CapturePreview(Form form, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("FOLDERSPLITTER_WHEEL_CAPTURE");
        if (directory is null) return;
        form.Refresh();
        using var image = PrintForm(form, 2);
        image.Save(Path.Combine(directory, name + ".png"));
    }
}
