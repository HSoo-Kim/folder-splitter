using QuadExplorer;

internal static partial class Program
{
    private static void RunActivePaneAppearanceScenario(string root)
    {
        Check(SessionStore.Normalize(new SessionState()).ActivePaneColor == "default"
            && SessionStore.Normalize(new SessionState { ActivePaneColor = "unknown" }).ActivePaneColor == "default"
            && SessionStore.Normalize(new SessionState { ActivePaneColor = " GREEN " }).ActivePaneColor == "green",
            "active pane colors default safely and normalize persisted values");
        string session = Path.Combine(root, "active-pane-appearance.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ColorTheme = "beige", Layout = 4, ExplorerFavoritesImported = true,
            TextSize = "small", ShowCommandBar = false, ShowVerticalSeparators = true,
            GlobalHotkey = false, KeepInTray = false, CheckUpdates = false,
            Panes = [new PaneState(), new PaneState(), new PaneState(), new PaneState()]
        });
        ToolStripMenuItem ColorMenu(ExplorerPane pane)
        {
            Descendants(pane).OfType<Button>()
                .Single(button => button.AccessibleName == $"Pane {pane.PaneNumber} Pane options").PerformClick();
            var options = (ContextMenuStrip)typeof(ExplorerPane).GetField("tabMenu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pane)!;
            return options.Items.OfType<ToolStripMenuItem>().SelectMany(item => item.DropDownItems.OfType<ToolStripMenuItem>())
                .Single(item => item.AccessibleName == "Active pane color");
        }
        void CloseOptions(ToolStripMenuItem menu) => ((ContextMenuStrip)menu.OwnerItem!.Owner!).Close();
        int EdgeCoverage(ExplorerPane pane, Color color)
        {
            pane.Refresh();
            using var image = new Bitmap(pane.Width, pane.Height);
            pane.DrawToBitmap(image, new Rectangle(Point.Empty, pane.Size));
            int count = 0;
            for (int x = 0; x <= pane.Padding.Left; x++)
            {
                Color pixel = image.GetPixel(x, pane.Height / 2);
                if (Math.Abs(pixel.R - color.R) + Math.Abs(pixel.G - color.G) + Math.Abs(pixel.B - color.B) <= 15) count++;
            }
            return count;
        }
        using (var form = new MainForm(session, []))
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
                    var geometry = panes.Select(pane => (pane.Bounds, pane.FileAreaOnScreen)).ToArray();
                    float points = form.Font.SizeInPoints;
                    Check(panes.All(pane => pane.ActivePaneColor == "default" && pane.ActiveBorderColor == Theme.Accent),
                        "all panes use their palette accent by default");
                    var menu = ColorMenu(panes[3]);
                    menu.DropDownItems.Cast<ToolStripItem>()
                        .Single(item => item.AccessibleName == "Active pane color green").PerformClick();
                    CloseOptions(menu);
                    Check(form.ActivePaneColor == "green" && panes.All(pane => pane.ActivePaneColor == "green"),
                        "changing color in an empty pane applies to every pane");
                    foreach (string theme in SessionStore.ColorThemes)
                    {
                        form.SetColorTheme(theme);
                        Check(panes.All(pane => pane.ActivePaneColor == "green"
                            && pane.ActiveBorderColor == panes[0].ActiveBorderColor),
                            "theme changes preserve the common color choice");
                        foreach (var pane in panes)
                        {
                            pane.SetActive(false);
                            int inactive = EdgeCoverage(pane, Theme.Border);
                            pane.SetActive(true);
                            int active = EdgeCoverage(pane, pane.ActiveBorderColor);
                            Console.WriteLine($"ACTIVE_EDGE theme={theme} pane={pane.PaneNumber} dpi={pane.DeviceDpi} inactive={inactive} active={active}");
                            if (active <= inactive && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } failedOutput)
                            {
                                using var failedImage = new Bitmap(pane.Width, pane.Height);
                                pane.DrawToBitmap(failedImage, new Rectangle(Point.Empty, pane.Size));
                                failedImage.Save(Path.Combine(failedOutput, "active-pane-failed.png"));
                            }
                            Check(active > inactive,
                                "the active rounded outline paints wider than an inactive outline");
                            pane.SetActive(false);
                        }
                    }
                    panes[1].FocusAddress();
                    panes[0].FocusAddress();
                    Check(panes.Select(pane => (pane.Bounds, pane.FileAreaOnScreen)).SequenceEqual(geometry)
                        && form.Font.SizeInPoints == points,
                        "active appearance preserves pane and file-area placement and font size");
                    form.SaveWorkspace("Active appearance");
                    form.SetActivePaneColor("amber");
                    form.LoadWorkspace("Active appearance");
                    Check(form.ActivePaneColor == "amber" && panes.All(pane => pane.ActivePaneColor == "amber"),
                        "workspace loads preserve the application-wide color instead of restoring an old color");
                    foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                    {
                        form.SetLanguage(language);
                        menu = ColorMenu(panes[1]);
                        Check(menu.DropDownItems.OfType<ToolStripMenuItem>().Count() == 4
                            && menu.DropDownItems.OfType<ToolStripMenuItem>()
                                .Single(item => item.AccessibleName == "Active pane color amber").Checked,
                            "every locale exposes the same persisted color choices and checked value");
                        CloseOptions(menu);
                    }
                    form.SetActivePaneColor("default");
                    foreach (string theme in new[] { "light", "dark", "beige" })
                    {
                        form.SetColorTheme(theme);
                        Check(panes.All(pane => pane.ActiveBorderColor == Theme.Accent),
                            "reset restores the current palette accent and follows subsequent theme changes");
                    }
                    form.SetActivePaneColor("blue");
                    form.SetLanguage("en");
                    var commandBar = Descendants(panes[0]).OfType<ToolStrip>()
                        .Single(strip => strip.AccessibleName == "Pane 1 commands");
                    panes[0].SetCommandBarVisible(true);
                    var view = commandBar.Items.OfType<ToolStripDropDownButton>()
                        .Single(item => item.AccessibleName == "Pane 1 View");
                    view.ShowDropDown();
                    Check(view.DropDownItems.OfType<ToolStripMenuItem>()
                        .Single(item => item.AccessibleName == "Active pane color").DropDownItems
                        .OfType<ToolStripMenuItem>().Single(item => item.AccessibleName == "Active pane color blue").Checked,
                        "the command-bar View menu exposes the same common setting as pane options");
                    view.HideDropDown();
                    panes[0].SetCommandBarVisible(false);
                    if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                    {
                        Size normal = form.Size;
                        foreach (string theme in new[] { "light", "dark" })
                        foreach (bool minimum in new[] { false, true })
                        {
                            await NativeAppearanceIdle(() =>
                            {
                                form.SetColorTheme(theme);
                                form.Size = minimum ? form.MinimumSize : normal;
                                foreach (Control control in Descendants(form).Prepend(form)) control.PerformLayout();
                            });
                            Console.WriteLine($"ACTIVE_LAYOUT theme={theme} minimum={minimum} client={form.ClientSize} panes={string.Join(";", panes.Select(pane => pane.Bounds))}");
                            panes[0].FocusAddress();
                            form.Refresh();
                            using var image = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                            image.Save(Path.Combine(output, $"active-pane-{theme}-{(minimum ? "minimum" : "normal")}.png"));
                        }
                        form.Size = normal;
                        form.SetColorTheme("beige");
                    }
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        var saved = SessionStore.Load(session);
        Check(saved.ActivePaneColor == "blue" && saved.ColorTheme == "beige" && saved.TextSize == "small"
            && !saved.ShowCommandBar && saved.ShowVerticalSeparators,
            "saving color preserves existing appearance preferences");
        using var reopened = new MainForm(session, []);
        reopened.Shown += (_, _) =>
        {
            try
            {
                Check(reopened.ActivePaneColor == "blue"
                    && Enumerable.Range(0, 4).Select(reopened.GetPane).All(pane => pane.ActivePaneColor == "blue"),
                    "the active pane color is applied to all panes after restart");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { reopened.ExitApplication(); }
        };
        Application.Run(reopened);
    }
}
