using QuadExplorer;

internal static partial class Program
{
    private static void RunStatusChromeScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "Status", "Research")).FullName;
        string longFolder = Directory.CreateDirectory(Path.Combine(root, "Status",
            "Research plans and shared data with a very long folder name " + new string('W', 55))).FullName;
        File.WriteAllBytes(Path.Combine(folder, "data.bin"), new byte[3000]);
        string nested = Directory.CreateDirectory(Path.Combine(folder, "Nested")).FullName;
        File.WriteAllBytes(Path.Combine(nested, "excluded.bin"), new byte[8000]);
        string session = Path.Combine(root, "status-chrome.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "ko", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [folder, longFolder] }]
        });
        using var form = new MainForm(session, []);
        var pane = form.GetPane(0);
        form.Shown += async (_, _) =>
        {
            try
            {
                if (pane.ActiveView is { HasNavigated: false } view)
                    await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                var status = Descendants(pane).OfType<Label>().Single(label => label.AccessibleName == "Pane 1 status");
                var toolbar = form.Controls.OfType<ToolStrip>().Single();
                Check(!string.IsNullOrWhiteSpace(form.Text) && !toolbar.Items.OfType<ToolStripLabel>().Any(label => label.Text == form.Text)
                    && toolbar.Items[0].AccessibleName == "Sidebar",
                    "the native title keeps the app name while the toolbar begins with its actual controls");
                var text = new UiText("ko");
                await pane.UpdateFolderSize();
                Check(status.Text.StartsWith(pane.ActiveView!.DisplayName + "  |  ", StringComparison.Ordinal)
                    && status.Text.Contains(text.T("Pane {0}  |  {1} tabs", 1, 2), StringComparison.Ordinal),
                    "the current folder name comes before pane and tab metadata");
                Check(status.Text.Contains(text.T("File total: {0}", ExplorerPane.FormatSize(3000)), StringComparison.Ordinal),
                    "the file total label uses the direct-file sum without walking nested folders");
                Check(pane.ToolTipFor(status) == status.Text,
                    "the status tooltip retains the full accessible folder name and metadata");
                foreach (string language in new[] { "en", "zh-CN", "ja", "ko" })
                {
                    form.SetLanguage(language);
                    Check(status.Text.Contains(new UiText(language).T("File total: {0}", ExplorerPane.FormatSize(3000)),
                        StringComparison.Ordinal), "the file total follows the language immediately without recalculating sizes");
                }

                var arrangements = toolbar.Items.OfType<ToolStripDropDownButton>()
                    .Single(item => item.AccessibleName == "Three pane arrangements");
                foreach (string theme in new[] { "light", "dark" })
                foreach (string size in new[] { "normal", "small", "large" })
                foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                {
                    form.SetColorTheme(theme);
                    form.SetUiFont(size == "large" ? "Malgun Gothic" : null, size);
                    form.SetLanguage(language);
                    arrangements.ShowDropDown();
                    try
                    {
                        var menu = arrangements.DropDown;
                        Check(menu.Font.Equals(form.Font) && menu.Items.Count == 4
                            && menu.Items.Cast<ToolStripItem>().All(item => item.Font.Equals(form.Font)
                                && item.Height >= TextRenderer.MeasureText(item.Text, item.Font).Height
                                && item.Width >= TextRenderer.MeasureText(item.Text, item.Font).Width
                                && menu.ClientRectangle.Contains(item.Bounds)),
                            $"three-pane menu uses readable UI text without icon-font inheritance or clipping in {theme}/{size}/{language}");
                        if (language == "ko" && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                        {
                            using var image = new Bitmap(menu.Width, menu.Height);
                            menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                            image.Save(Path.Combine(output, $"arrangements-{theme}-{size}.png"));
                        }
                    }
                    finally { arrangements.HideDropDown(); }
                }
                form.SetUiFont(null, "normal");
                form.SetLanguage("ko");

                foreach (string theme in new[] { "light", "dark" })
                foreach (bool narrow in new[] { false, true })
                {
                    form.SetColorTheme(theme);
                    form.Size = narrow ? new Size(900, 600) : new Size(1500, 900);
                    form.ApplyLayout(narrow ? 4 : 1);
                    pane.SelectTab(0);
                    form.Refresh();
                    using (var image = new Bitmap(status.Width, status.Height))
                    {
                        status.DrawToBitmap(image, new Rectangle(Point.Empty, status.Size));
                        int nameWidth = TextRenderer.MeasureText(pane.ActiveView!.DisplayName, status.Font).Width;
                        int split = Math.Min(status.Width / 2, nameWidth + status.Padding.Left);
                        int Contrast(int start, int end)
                        {
                            int max = 0;
                            for (int x = start; x < end; x++)
                            for (int y = 0; y < image.Height; y++)
                            {
                                Color pixel = image.GetPixel(x, y), background = status.BackColor;
                                max = Math.Max(max, Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G)
                                    + Math.Abs(pixel.B - background.B));
                            }
                            return max;
                        }
                        int nameContrast = Contrast(status.Padding.Left, split);
                        int detailContrast = Contrast(split + 8, image.Width - status.Padding.Right);
                        Check(nameContrast > detailContrast + 15 && detailContrast > 0,
                            $"the folder name has stronger text contrast than metadata in {theme}/{(narrow ? "four" : "one")}");
                    }

                    await PaneChange(pane, () => pane.SelectTab(1),
                        () => pane.ActiveView is { HasNavigated: true } && pane.CurrentPath == longFolder);
                    form.Refresh();
                    Check(status.Text.StartsWith(pane.ActiveView!.DisplayName + "  |  ", StringComparison.Ordinal)
                        && pane.ToolTipFor(status) == status.Text && status.Height >= status.PreferredHeight
                        && status.Parent!.ClientRectangle.Contains(status.Bounds),
                        "a long folder name remains complete in accessible text and the tooltip while fitting the caption height");
                    Check(Enumerable.Range(0, 4).Select(form.GetPane).Where(each => each.Visible).All(each =>
                    {
                        var caption = Descendants(each).OfType<Label>()
                            .Single(label => label.AccessibleName == $"Pane {each.PaneNumber} status");
                        var bounds = each.RectangleToClient(caption.RectangleToScreen(caption.ClientRectangle));
                        return each.ClientRectangle.Contains(bounds) && caption.Parent!.ClientRectangle.Contains(caption.Bounds)
                            && caption.Height >= caption.PreferredHeight;
                    }), "every visible pane keeps its complete status caption inside its own content area");
                    Check(toolbar.Items.Cast<ToolStripItem>().Where(item => item.Available)
                        .All(item => toolbar.ClientRectangle.Contains(item.Bounds)),
                        "removing the repeated brand leaves toolbar controls reachable at normal and minimum sizes");
                    if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                    {
                        using var image = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(output, $"status-{theme}-{(narrow ? "four-minimum" : "one")}.png"));
                        using var caption = new Bitmap(status.Width, status.Height);
                        status.DrawToBitmap(caption, new Rectangle(Point.Empty, status.Size));
                        caption.Save(Path.Combine(output, $"status-caption-{theme}-{(narrow ? "four-minimum" : "one")}.png"));
                    }
                }

                pane.SetLocalizedStatus("Folder path copied.");
                Check(status.Text.StartsWith(pane.ActiveView!.DisplayName + "  |  " + text.T("Folder path copied."),
                    StringComparison.Ordinal), "action feedback keeps the folder identity at the leading edge");
                pane.AddBlankTab();
                Check(status.Text == text.T("Pane {0}  |  {1} tabs", 1, 3),
                    "a blank new tab does not show the previous folder name or file total");
                while (pane.TabCount > 0) pane.CloseTab();
                Check(status.Text == text.T("Pane {0}  |  {1} tabs", 1, 0),
                    "an empty pane keeps its pane and tab count without an orphan separator");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }
}
