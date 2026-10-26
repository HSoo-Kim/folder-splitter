using QuadExplorer;

internal static partial class Program
{
    private static void RunBreadcrumbDpiScenario(string root, string? output)
    {
        if (output is not null) Directory.CreateDirectory(output);
        string folder = Directory.CreateDirectory(Path.Combine(root, "Program Files", "Windows NT")).FullName;
        string session = Path.Combine(root, "breadcrumb-dpi.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ColorTheme = "nord", ExplorerFavoritesImported = true,
            GlobalHotkey = false, WindowWidth = 1100, WindowHeight = 700,
            Panes = [new PaneState { Tabs = [folder] }]
        });
        using var form = new MainForm(session, []);
        var pane = form.GetPane(0);
        var navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                pane.FocusFiles();
                await NativeAppearanceDispatch(form);
                var address = Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName == "Pane 1 address");
                using var originalFont = (Font)address.Font.Clone();
                int windowDpi = address.DeviceDpi;
                var results = new List<(bool Fits, string Name)>();
                int step = 0;
                // Pure sizing regression: vary the address's final font metrics as a DPI change does.
                // Synthetic WM_DPICHANGED cannot change GetDpiForWindow when TextBox recreates its HWND.
                // All HWNDs remain on the real monitor; this does not claim hardware DPI coverage.
                foreach (int dpi in new[] { 144, 120, 96, 144, 192, 96, 120, 144 })
                {
                    using var scaledFont = new Font(originalFont.FontFamily, originalFont.SizeInPoints * dpi / windowDpi);
                    address.Font = scaledFont;
                    await NativeAppearanceDispatch(form);
                    pane.Breadcrumb.PerformLayout();
                    address.Parent!.PerformLayout();
                    form.Refresh();
                    var strip = pane.Breadcrumb;
                    // Exercise the final synchronization hook independently of FontChanged.
                    // This is a child notification only, not a simulated monitor transition.
                    strip.Font = originalFont;
                    SendMessage(strip.Handle, 0x02E3, 0, 0); // WM_DPICHANGED_AFTERPARENT
                    results.Add((strip.Font.Equals(address.Font),
                        $"breadcrumb follows address font at {dpi} DPI-equivalent sizing, step {step}"));
                    var fieldBounds = Rectangle.Inflate(address.Bounds,
                        (int)Math.Round(7 * pane.DeviceDpi / 96d), (int)Math.Round(5 * pane.DeviceDpi / 96d));
                    results.Add((fieldBounds.Top >= 0 && fieldBounds.Bottom <= address.Parent.ClientSize.Height,
                        $"breadcrumb field outline fits navigation at {dpi} DPI-equivalent sizing, step {step}"));
                    Console.WriteLine($"FIELD bounds={fieldBounds} navigationHeight={address.Parent.Height}");
                    Console.WriteLine($"DPI step={step} requested={dpi} form={form.DeviceDpi} address={address.DeviceDpi} strip={strip.DeviceDpi} " +
                        $"addressHeight={address.ClientSize.Height} stripHeight={strip.ClientSize.Height} " +
                        $"addressFont={address.Font.SizeInPoints:F2} stripFont={strip.Font.SizeInPoints:F2}");
                    foreach (var item in strip.Items.Cast<ToolStripItem>().Where(item => item.Available))
                    {
                        // ToolStrip's layout can place a correctly sized glyph below its clipping parent.
                        var layout = (ToolStripItemTextRenderEventArgs?)null;
                        void TextRendered(object? sender, ToolStripItemTextRenderEventArgs e)
                        {
                            if (ReferenceEquals(e.Item, item)) layout = e;
                        }
                        strip.Renderer.RenderItemText += TextRendered;
                        using (var image = new Bitmap(strip.Width, strip.Height))
                            strip.DrawToBitmap(image, strip.ClientRectangle);
                        strip.Renderer.RenderItemText -= TextRendered;
                        Rectangle textBounds = layout?.TextRectangle ?? Rectangle.Empty;
                        textBounds.Offset(item.Bounds.Location);
                        Console.WriteLine($"ITEM {item.Text} bounds={item.Bounds} content={item.ContentRectangle} text={textBounds} font={item.Font.SizeInPoints:F2}");
                        int textHeight = TextRenderer.MeasureText(item.Text, item.Font).Height;
                        bool fits = !textBounds.IsEmpty && textBounds.Top >= 0 && textBounds.Bottom <= strip.ClientSize.Height
                            && textHeight <= textBounds.Height;
                        Console.WriteLine($"TEXT_FITS {fits} measuredHeight={textHeight}");
                        results.Add((fits, $"breadcrumb {item.Text} text fits address at {dpi} DPI-equivalent sizing, step {step}"));
                    }
                    if (output is not null)
                    {
                        // Capture real WinForms chrome, not the native Shell view (uncomposed on private desktop).
                        void Capture(string suffix)
                        {
                            using var image = new Bitmap(pane.Width, pane.Height);
                            pane.DrawToBitmap(image, new Rectangle(Point.Empty, pane.Size));
                            Rectangle crop = pane.RectangleToClient(address.Parent!.RectangleToScreen(address.Parent.ClientRectangle));
                            using var chrome = image.Clone(crop, image.PixelFormat);
                            string path = Path.Combine(output, $"{step:00}-{dpi}{suffix}.png");
                            chrome.Save(path);
                            Console.WriteLine("SCREENSHOT " + path);
                        }
                        Capture("");
                        address.Focus();
                        await NativeAppearanceDispatch(form);
                        results.Add((address.Focused && !strip.Visible,
                            $"address editing replaces breadcrumb at {dpi} DPI-equivalent sizing, step {step}"));
                        Capture("-focused");
                        // Native Shell focus is not reliable on a non-input desktop.
                        // Use a real managed control in this window to leave edit mode.
                        address.Parent.Controls.OfType<Button>().First(button => button.Enabled && button.Visible).Focus();
                        await NativeAppearanceDispatch(form);
                        Console.WriteLine($"FOCUS_RETURN address={address.Focused} breadcrumb={strip.Visible}");
                        results.Add((strip.Visible,
                            $"breadcrumb returns after address editing at {dpi} DPI-equivalent sizing, step {step}"));
                    }
                    step++;
                }
                foreach (var result in results) Check(result.Fits, result.Name);
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }
}
