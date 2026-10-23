using QuadExplorer;

internal static partial class Program
{
    private static void RunTabStripScenario(string root)
    {
        string parent = Directory.CreateDirectory(Path.Combine(root, "Tabs")).FullName;
        string longFolder = Directory.CreateDirectory(Path.Combine(parent,
            "\uc544\uc8fc \uae34 \uc774\ub984\uc758 \uc5f0\uad6c \ub370\uc774\ud130 \ud3f4\ub354 2026 \uac00\uc744 \ud559\uae30 \uc2e4\ud5d8 \uacb0\uacfc \uc815\ub9ac\ubcf8")).FullName;
        string shortFolder = Directory.CreateDirectory(Path.Combine(parent, "A")).FullName;
        string session = Path.Combine(root, "tabs.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [longFolder] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                if (!pane.ActiveView!.HasNavigated) await NavigationSignal(pane.ActiveView!).WaitAsync(TimeSpan.FromSeconds(20));
                var strip = pane.TabStrip;
                float scale = strip.DeviceDpi / 96f;
                int Scaled(int value) => (int)Math.Round(value * scale);

                Check(pane.TabButtons[0].Width > Scaled(240) && pane.TabButtons[0].Width <= Scaled(320),
                    "a lone tab with a long name grows past the normal maximum");
                pane.AddTab(shortFolder);
                Check(pane.TabButtons[0].Width <= Scaled(240) && pane.TabButtons[0].Text!.EndsWith('\u2026'),
                    "with more tabs a long name returns to the normal maximum with an ellipsis");
                Check(pane.TabButtons[1].Width >= Scaled(110), "a short name keeps a readable minimum width");
                using (var image = new Bitmap(strip.Width, strip.Height))
                {
                    strip.DrawToBitmap(image, new Rectangle(Point.Empty, strip.Size));
                    var idle = pane.TabButtons[0].Bounds;
                    Color edge = image.GetPixel(idle.Left + 1, idle.Top + idle.Height / 2);
                    Color inside = image.GetPixel(idle.Left + idle.Width / 2, idle.Top + 2);
                    Check(!pane.TabButtons[0].Checked && edge.ToArgb() != inside.ToArgb(),
                        "a tab that is not being viewed draws a faint outline");
                }
                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_TAB_CAPTURE") is { Length: > 0 } captures)
                {
                    for (int i = 0; i < 4; i++) pane.AddTab(shortFolder, select: false);
                    foreach (string mode in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(mode);
                        form.Refresh();
                        using var shot = PrintForm(form, 2);
                        shot.Save(Path.Combine(captures, $"tabs-{mode}.png"));
                    }
                    form.SetColorTheme("light");
                    while (pane.TabCount > 2) pane.CloseTab(pane.TabCount - 1);
                }

                for (int i = 0; i < 22; i++) pane.AddTab(shortFolder, select: false);
                var plus = strip.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Pane 1 new tab");
                var shown = pane.TabButtons.Where(button => button.Available).ToList();
                Check(pane.TabList.Available && shown.Count < pane.TabButtons.Count,
                    "tabs that cannot fit move to the tab list button");
                Check(shown.All(button => button.Width >= Scaled(90)), "visible tabs shrink no further than the floor");
                Check(plus.Available && shown.All(button => button.Bounds.Right <= plus.Bounds.Left)
                    && pane.TabList.Bounds.Right <= strip.ClientSize.Width,
                    "the new tab and tab list buttons stay inside the tab row");
                Check(pane.TabList.DropDownItems.Count == pane.TabButtons.Count, "the tab list names every tab");

                pane.TabList.DropDownItems[^1].PerformClick();
                Check(pane.SelectedIndex == pane.TabButtons.Count - 1 && pane.TabButtons[^1].Available
                    && !pane.TabButtons[0].Available, "choosing a hidden tab from the list selects and reveals it");

                pane.FocusFiles();
                var center = strip.PointToScreen(new Point(strip.Width / 2, strip.Height / 2));
                bool Wheel(int message, int delta, Point at)
                {
                    var wheel = Message.Create(strip.Handle, message, (nint)(delta << 16), (nint)((at.Y << 16) | (at.X & 0xFFFF)));
                    return form.PreFilterMessage(ref wheel);
                }
                int last = pane.TabButtons.Count - 1;
                Check(Wheel(0x20A, -120, center) && pane.SelectedIndex == last, "wheel past the last tab stays on it");
                Check(Wheel(0x20A, 120, center) && pane.SelectedIndex == last - 1, "wheel up over the tabs selects the previous tab");
                Check(Wheel(0x20E, 120, center) && pane.SelectedIndex == last, "tilting the wheel right selects the next tab");
                Wheel(0x20E, -60, center);
                Check(pane.SelectedIndex == last, "half a wheel notch does not switch tabs");
                Wheel(0x20E, -60, center);
                Check(pane.SelectedIndex == last - 1, "a completed notch from small steps switches one tab");
                var files = pane.ActiveView!.RectangleToScreen(pane.ActiveView.ClientRectangle);
                Wheel(0x20A, 120, new Point(files.Left + files.Width / 2, files.Top + files.Height / 2));
                Check(pane.SelectedIndex == last - 1, "wheel over the file list does not switch tabs");

                pane.SelectTab(0);
                Check(pane.TabButtons[0].Available && !pane.TabButtons[^1].Available, "selecting the first tab scrolls back to it");
                while (pane.TabCount > 1) pane.CloseTab(pane.TabCount - 1);
                Check(!pane.TabList.Available && pane.TabButtons[0].Width > Scaled(240),
                    "closing tabs hides the list and lets the last tab grow again");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }

    // Reports layout cost per window resize step with four panes of tabs; printed for comparison only.
    private static void RunResizeMetric(string root)
    {
        var folders = Enumerable.Range(1, 4).Select(i => Directory.CreateDirectory(Path.Combine(root, "Resize", $"Folder {i}")).FullName).ToArray();
        string session = Path.Combine(root, "resize.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 4, Language = "en", ExplorerFavoritesImported = true,
            Panes = folders.Select(folder => new PaneState { Tabs = Enumerable.Repeat(folder, 6).ToList() }).ToList()
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                foreach (var view in Enumerable.Range(0, 4).Select(i => form.GetPane(i).ActiveView!))
                    if (!view.HasNavigated) await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                form.Size = new Size(1500, 950);
                form.Refresh();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                const int steps = 40;
                for (int i = 0; i < steps; i++)
                {
                    form.Size = i % 2 == 0 ? new Size(1300, 850) : new Size(1500, 950);
                    form.Refresh();
                }
                Console.WriteLine($"METRIC resize_ms_per_step={watch.Elapsed.TotalMilliseconds / steps:F2}");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }
}
