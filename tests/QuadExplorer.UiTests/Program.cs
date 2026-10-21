using QuadExplorer;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int failures;
    private static int checks;

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Path.GetTempPath(), "QuadExplorer.UiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folders = Enumerable.Range(1, 4).Select(i => Directory.CreateDirectory(Path.Combine(root, $"Pane-{i}")).FullName).ToArray();
            string child = Directory.CreateDirectory(Path.Combine(folders[0], "\ud55c\uae00 \ud3f4\ub354")).FullName;
            File.WriteAllText(Path.Combine(folders[0], "sample.txt"), "QuadExplorer integration fixture");
            string session = Path.Combine(root, "session.json");
            SessionStore.Save(session, new SessionState
            {
                Layout = 4,
                ExplorerFavoritesImported = true,
                Panes = folders.Select(path => new PaneState { Tabs = [path] }).ToList()
            });
            using var form = new MainForm(session, []);
            var panes = Descendants(form).OfType<ExplorerPane>().OrderBy(p => p.PaneNumber).ToArray();
            var initialNavigation = panes.Select(p => p.ActiveView!.HasNavigated
                ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.WhenAll(initialNavigation).WaitAsync(TimeSpan.FromSeconds(20));
                    Check(panes.Length == 4 && panes.All(p => p.Visible), "four native shell panes loaded");
                    Check(panes.Select(p => p.CurrentPath).SequenceEqual(folders, StringComparer.OrdinalIgnoreCase), "independent initial folders");
                    Check(panes.All(p => HasNativeDropTarget(p.ActiveView!)), "initial shell panes register native drop targets");

                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(child));
                    Check(panes[0].CurrentPath == child && panes[1].CurrentPath == folders[1], "Unicode folder navigation is isolated");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x4000));
                    Check(panes[0].CurrentPath == folders[0], "native back history");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x8000));
                    Check(panes[0].CurrentPath == child, "native forward history");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x2000));
                    Check(panes[0].CurrentPath == folders[0], "native parent navigation");

                    string selectedFile = Path.Combine(folders[0], "sample.txt");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.NavigateAndSelect(selectedFile));
                    Check(SelectedPaths(panes[0].ActiveView!).SequenceEqual(new[] { selectedFile },
                        StringComparer.OrdinalIgnoreCase), "file candidate selects its actual native shell item");

                    panes[0].AddTab(folders[1], select: false);
                    var newView = Descendants(panes[0]).OfType<ShellViewHost>().Single(h => h.LocationPath == folders[1]);
                    await Navigate(newView, () => panes[0].SelectTab(1));
                    Check(panes[0].TabCount == 2 && panes[0].CurrentPath == folders[1], "new independent tab");
                    Check(HasNativeDropTarget(newView), "new shell tab registers a native drop target");
                    panes[0].CloseTab();
                    Check(panes[0].TabCount == 1 && panes[0].CurrentPath == folders[0], "close restores previous tab");
                    panes[0].ReopenTab();
                    Check(panes[0].TabCount == 2, "reopen closed tab");
                    panes[0].SelectTab(0);
                    panes[0].ActiveView!.SetViewMode(3);
                    panes[0].ActiveView!.SetViewMode(4);
                    panes[0].ActiveView!.RefreshFolder();
                    Check(true, "native view modes and refresh");

                    ClickLayout(form, 1);
                    Check(panes[0].Visible && panes.Skip(1).All(p => !p.Visible), "one-pane layout");
                    ClickLayout(form, 2);
                    Check(panes[0].Visible && panes[1].Visible && !panes[2].Visible && !panes[3].Visible, "two-pane layout");
                    ClickLayout(form, 4);
                    Check(panes.All(p => p.Visible) && panes[0].TabCount == 2, "four-pane restore preserves tabs");
                    form.Size = new Size(900, 600);
                    Check(panes.All(p => p.Width > 200 && p.Height > 100), "minimum-size pane layout");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
            var restored = SessionStore.Load(session, out var warning);
            Check(warning is null && restored.Layout == 4 && restored.Panes[0].Tabs.Count == 2, "session saved on close");
            Check(restored.Panes.Skip(1).Select(p => p.Tabs[0]).SequenceEqual(folders.Skip(1)), "inactive pane paths persisted");
            RunDockingScenario(session, folders);
            RunEmptyPaneScenario(session, folders);
            RunLanguageScenario(session, folders);
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine(failures == 0 ? $"PASS {checks} native UI integration checks" : $"FAIL {failures} native UI checks ({checks} passed)");
        return failures == 0 ? 0 : 1;
    }

    private static void RunDockingScenario(string session, string[] folders)
    {
        using var form = new MainForm(session, []);
        var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
        var navigation = panes.Select(p => p.ActiveView!.HasNavigated
            ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
        PaneLayout? expectedTree = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.WhenAll(navigation).WaitAsync(TimeSpan.FromSeconds(20));
                ClickLayout(form, 3);
                Check(panes[0].Visible && panes[1].Visible && panes[2].Visible && !panes[3].Visible
                    && panes[1].Height > panes[0].Height * 1.5, "three panes: two left and one right");
                form.ApplyLayout(3, false);
                Check(panes[0].Height > panes[1].Height * 1.5 && !panes[3].Visible, "three panes: one left and two right");
                form.ApplyLayout(3, true, false);
                Check(panes[1].Width > panes[0].Width * 1.5, "three panes: horizontal arrangement");
                Check(panes.Skip(1).Select(p => p.CurrentPath).SequenceEqual(folders.Skip(1)),
                    "three-pane presets preserve hidden folder paths");

                ClickLayout(form, 1);
                var moving = panes[0].ActiveView!;
                await PaneChange(panes[1], () => Check(form.DockTab(panes[0], moving, 0, SplitEdge.Right),
                    "edge docking accepts a second pane"), () => panes[1].ActiveView!.HasNavigated && panes[1].CurrentPath == folders[0]);
                Check(form.CurrentLayout.GetPanes().Length == 2 && panes[0].TabCount == 1 && panes[1].TabCount == 2,
                    "edge docking moves the chosen tab without losing cached tabs");

                moving = panes[1].ActiveView!;
                await PaneChange(panes[2], () => Check(form.DockTab(panes[1], moving, 1, SplitEdge.Bottom),
                    "edge docking accepts a third pane"), () => panes[2].ActiveView!.HasNavigated && panes[2].CurrentPath == folders[0]);
                Check(form.CurrentLayout.First!.IsLeaf && !form.CurrentLayout.Second!.IsLeaf
                    && !form.CurrentLayout.Second.Vertical, "docking creates a one-plus-two layout");

                moving = panes[2].ActiveView!;
                await PaneChange(panes[3], () => Check(form.DockTab(panes[2], moving, 2, SplitEdge.Left),
                    "edge docking accepts a fourth pane"), () => panes[3].ActiveView!.HasNavigated && panes[3].CurrentPath == folders[0]);
                var before = form.CurrentLayout;
                int totalTabs = panes.Sum(p => p.TabCount);
                Check(!form.DockTab(panes[3], panes[3].ActiveView!, 3, SplitEdge.Bottom)
                    && form.CurrentLayout == before && panes.Sum(p => p.TabCount) == totalTabs,
                    "fifth-pane rejection keeps layout and tabs unchanged");

                moving = panes[2].ActiveView!;
                await PaneChange(panes[0], () => Check(form.DockTab(panes[2], moving, 0, null),
                    "center docking accepts a tab move"), () => panes[0].ActiveView!.HasNavigated && panes[0].CurrentPath == folders[2]);
                Check(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 })
                    && !panes[2].Visible, "moving the last tab collapses the empty source pane");
                panes[3].SelectTab(0);
                expectedTree = form.CurrentLayout;
                form.ToggleFavorites();
                Check(!form.FavoritesExpanded, "favorites collapse independently of the sidebar");
                form.ToggleFavorites();
                Check(form.FavoritesExpanded, "favorites expand again");
                form.ToggleFavorites();
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session, out var warning);
        Check(warning is null && restored.Layout == 3 && restored.ActivePane == 3
            && restored.LayoutTree == expectedTree, "docked layout and sparse active pane survive session save");
        Check(restored.Panes[0].Tabs.SequenceEqual(new[] { folders[1], folders[2] })
            && restored.Panes[3].Tabs.SequenceEqual(new[] { folders[3], folders[0] }),
            "moved and cached tabs survive session save");
        Check(panes.All(p => p.IsDisposed), "closing the form disposes hidden pane views");
        Check(!restored.FavoritesExpanded, "collapsed favorites survive session save");
    }

    private static async Task PaneChange(ExplorerPane pane, Action trigger, Func<bool> complete)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e) { if (complete()) signal.TrySetResult(); }
        pane.StateChanged += Changed;
        try
        {
            trigger();
            Changed(pane, EventArgs.Empty);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { pane.StateChanged -= Changed; }
    }

    private static void RunEmptyPaneScenario(string session, string[] folders)
    {
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, ExplorerFavoritesImported = true, Favorites = [folders[0], folders[1]],
            Panes = [new PaneState { Tabs = [folders[0]] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                form.ApplyLayout(4);
                Check(Enumerable.Range(1, 3).All(i => form.GetPane(i).TabCount == 0),
                    "new split panes stay empty");
                Check(new[] { 2, 3 }.All(i =>
                {
                    var label = Descendants(form.GetPane(i)).OfType<Label>()
                        .Single(l => l.AccessibleName == $"Pane {i + 1} status");
                    return label.Visible && Screen.FromControl(form).WorkingArea.Contains(
                        label.RectangleToScreen(label.ClientRectangle));
                }), "bottom pane status labels remain inside the screen working area");
                var source = form.GetPane(0);
                form.ApplyLayout(1);
                Check(form.DockTab(source, source.ActiveView!, 0, SplitEdge.Right)
                    && source.TabCount == 0 && form.GetPane(1).TabCount == 1,
                    "splitting the only tab moves it without duplicating");
                var empty = form.GetPane(0);
                await PaneChange(empty, () => empty.Navigate(folders[2]),
                    () => empty.ActiveView is { HasNavigated: true });
                Check(empty.CurrentPath == folders[2], "navigation opens a folder in an empty pane");
                empty.CloseTab();
                Check(empty.TabCount == 0 && empty.CurrentPath == "", "closing the last tab leaves a blank pane");
                form.SetSidebarWidth(320);
                form.SetSidebarVisible(false);
                Check(!Descendants(form).OfType<ListBox>().Any(l => l.Visible), "quick access fully collapses");
                var version = form.Controls.OfType<Label>().Single(l => l.AccessibleName == "Application version");
                Check(version.Visible && version.Text == "v" + typeof(MainForm).Assembly.GetName().Version!.ToString(3)
                    && version.Bounds.Bottom == form.ClientRectangle.Bottom && version.Height >= version.PreferredHeight,
                    "lower-left version matches running assembly and remains visible with sidebar hidden");
                form.SetSidebarVisible(true);
                Check(form.SidebarWidth == 320, "quick access restores its resized width");
                form.ClearFavorites();
                Check(form.FavoritePaths.Count == 0 && form.DeletedFavoritePaths.Count == 2,
                    "clear all favorites retains recoverable entries");
                form.RestoreFavorite(folders[1]);
                Check(form.FavoritePaths.SequenceEqual(new[] { folders[1] }) && form.DeletedFavoritePaths.SequenceEqual(new[] { folders[0] }),
                    "restore selected favorite removes it from recovery list");
                var toolbar = form.Controls.OfType<ToolStrip>().Single();
                Check(toolbar.Items.Cast<ToolStripItem>().Where(i => i.Available).All(i =>
                    i.Bounds.Height >= TextRenderer.MeasureText(i.Text, i.Font).Height),
                    "toolbar item height fits its rendered text");
                Check(Descendants(form).OfType<TextBox>().All(t => t.Height >= t.PreferredHeight),
                    "address fields fit their preferred font height");
                Check(Descendants(form).OfType<Label>().Where(l => l.AccessibleName?.EndsWith(" status") == true)
                    .All(l => l.Height >= l.PreferredHeight), "status labels fit their preferred font height");
                Check(Descendants(form).OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<Button>())
                    .All(b => b.Width >= b.GetPreferredSize(Size.Empty).Width),
                    "navigation icon columns fit their preferred glyph width");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session);
        Check(restored.SidebarWidth == 320 && restored.ShowSidebar && restored.Panes[0].Tabs.Count == 0
            && restored.DeletedFavorites.SequenceEqual(new[] { folders[0] }),
            "sidebar width, blank pane and recovery list persist");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void RunLanguageScenario(string session, string[] folders)
    {
        SessionStore.Save(session, new SessionState
        {
            ExplorerFavoritesImported = true, Favorites = [folders[0]],
            Panes = [new PaneState { Tabs = [folders[0], folders[1]], SelectedTab = 0 }]
        });
        using (var form = new MainForm(session, []))
        {
            var pane = form.GetPane(0);
            var navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
            form.Shown += async (_, _) =>
            {
                try
                {
                    await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                    await Navigate(pane.ActiveView!, () => pane.Navigate(Path.Combine(folders[0], "한글 폴더")));
                    await Navigate(pane.ActiveView!, () => pane.ActiveView!.Travel(0x4000));
                    var toolbar = form.Controls.OfType<ToolStrip>().Single();
                    var selector = toolbar.Items.OfType<ToolStripComboBox>().Single();
                    Check(selector.Items.Cast<string>().SequenceEqual(new[] { "한국어", "English", "简体中文", "日本語" })
                        && form.Language == "ko", "four native language choices default to Korean");
                    var hosts = Descendants(pane).OfType<ShellViewHost>().ToArray();
                    var active = pane.ActiveView;
                    nint activeHandle = active!.Handle;
                    var state = pane.CaptureState();
                    var tree = form.CurrentLayout;
                    var address = Descendants(pane).OfType<TextBox>().Single();
                    address.Text = "Z:\\typed but not navigated\\日本語";
                    var favorites = Descendants(form).OfType<ListBox>().Single(l => l.AccessibleName == "Favorites");
                    favorites.SelectedIndex = 0;
                    var quickAccess = Descendants(form).OfType<ListBox>().Single(l => l.AccessibleName == "Quick access");
                    quickAccess.SelectedIndex = 2;
                    string favoriteName = favorites.Items[0]!.ToString()!;
                    foreach (var (index, language) in new[] { (1, "en"), (2, "zh-CN"), (3, "ja"), (0, "ko"), (3, "ja") })
                    {
                        await SelectLanguageFromDropdown(selector, index);
                        var text = new UiText(language);
                        Check(form.Language == language
                            && toolbar.Items.Cast<ToolStripItem>().Single(i => i.AccessibleName == "1 pane layout").Text == text.T("1 pane")
                            && toolbar.Items.Cast<ToolStripItem>().Single(i => i.AccessibleName == "Sidebar").ToolTipText == text.T("Show / hide quick access sidebar"),
                            language + " selector refreshes toolbar labels and hover text");
                        var up = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 Up (Alt+Up)");
                        var go = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 go to address");
                        var choose = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 Choose folder");
                        Check(pane.ToolTipFor(up) == text.T("Up (Alt+Up)")
                            && pane.ToolTipFor(go) == text.T("Go to typed address (Enter)")
                            && pane.ToolTipFor(choose) == text.T("Browse for a folder: open folder picker"),
                            language + " navigation tooltips retain parent, address and picker meanings");
                        Check(quickAccess.Items[0]!.ToString() == text.T("Home")
                            && favorites.Items[0]!.ToString() == favoriteName
                            && quickAccess.SelectedIndex == 2 && favorites.SelectedIndex == 0
                            && favorites.ContextMenuStrip!.Items[0].Text == text.T("Open in new tab"),
                            language + " sidebar and favorite actions translate without changing folder names");
                        Check(Descendants(pane).OfType<Label>().Single(l => l.AccessibleName == "Pane 1 status").Text
                                == text.T("Pane {0}  |  {1} tabs  |  {2}", 1, 2, active.DisplayName)
                            && Descendants(form.GetPane(1)).OfType<Label>().Any(l =>
                                l.Text == text.T("Enter a folder path above or choose a folder from Pane options.")),
                            language + " pane status and empty hints translate");
                        Check(ReferenceEquals(pane.ActiveView, active) && active.Handle == activeHandle
                            && Descendants(pane).OfType<ShellViewHost>().SequenceEqual(hosts)
                            && pane.CaptureState().Tabs.SequenceEqual(state.Tabs) && pane.SelectedIndex == state.SelectedTab
                            && form.CurrentLayout == tree && address.Text == "Z:\\typed but not navigated\\日本語"
                            && HasNativeDropTarget(active),
                            language + " switch preserves native views, tabs, typed address and drop targets");
                        Check(selector.GetCurrentParent() == toolbar && toolbar.ClientRectangle.Contains(selector.Bounds),
                            language + " selector stays visible in toolbar");
                        CheckLanguagePlacement(toolbar, selector, language);
                        var options = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 Pane options");
                        options.PerformClick();
                        var menu = (ContextMenuStrip)typeof(ExplorerPane)
                            .GetField("tabMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pane)!;
                        Check(menu.Items[0].Text == text.T("Refresh    F5")
                            && menu.Items[4].Text == text.T("Choose folder..."), language + " pane options translate");
                        menu.Close();
                        typeof(ExplorerPane).GetMethod("ShowTabMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(pane, [0]);
                        Check(menu.Items[0].Text == text.T("New tab")
                            && ((ToolStripMenuItem)menu.Items[3]).DropDownItems[0].Text == text.T("Pane {0}", 2)
                            && ((ToolStripMenuItem)menu.Items[4]).DropDownItems[0].Text == text.T("Left"),
                            language + " tab and split context menus translate");
                        menu.Close();
                        // Opening a tab menu selects it; restore the unsubmitted address before the next switch.
                        address.Text = "Z:\\typed but not navigated\\日本語";
                        CaptureLanguage(form, language + "-desktop");
                        form.Size = new Size(900, 600);
                        Check(selector.GetCurrentParent() == toolbar && toolbar.ClientRectangle.Contains(selector.Bounds),
                            language + " selector remains visible at minimum window size");
                        CheckLanguagePlacement(toolbar, selector, language + " minimum");
                        CaptureLanguage(form, language + "-minimum");
                        form.Size = new Size(1400, 900);
                    }
                    pane.SetLocalizedStatus("Folder path copied.");
                    selector.SelectedIndex = 2;
                    Check(Descendants(pane).OfType<Label>().Single(l => l.AccessibleName == "Pane 1 status").Text
                        == new UiText("zh-CN").T("Folder path copied."), "existing action status refreshes on language switch");
                    selector.SelectedIndex = 3;
                    await Navigate(active, () => active.Travel(0x8000));
                    Check(pane.CurrentPath == Path.Combine(folders[0], "한글 폴더"),
                        "native forward history survives all language switches");
                    ValidateTranslations();
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        Check(SessionStore.Load(session).Language == "ja", "selected Japanese language saves on close");
        using var reopened = new MainForm(session, []);
        Check(reopened.Language == "ja"
            && reopened.Controls.OfType<ToolStrip>().Single().Items.OfType<ToolStripComboBox>().Single().SelectedIndex == 3
            && reopened.GetPane(0).TabCount == 2, "reopened session retains language and tabs");
    }

    private static void ValidateTranslations()
    {
        var strings = (System.Collections.IDictionary)typeof(UiText)
            .GetField("Strings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            var text = new UiText(language);
            foreach (string key in strings.Keys)
            {
                string translated = text.T(key, 1, 2, 3);
                if (string.IsNullOrWhiteSpace(translated) || (language != "en" && translated == key))
                    throw new InvalidOperationException(language + " missing translation for " + key);
            }
            Check(!string.IsNullOrWhiteSpace(text.Help), language + " dictionary formats every app-owned message");
        }
    }

    private static void CaptureLanguage(Form form, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("FOLDERSPLITTER_UI_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        form.Refresh();
        using var bitmap = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(bitmap);
        nint context = graphics.GetHdc();
        try
        {
            if (!PrintWindow(form.Handle, context, 2))
                throw new InvalidOperationException("Could not capture app window.");
        }
        finally { graphics.ReleaseHdc(context); }
        string path = Path.Combine(directory, name + ".png");
        bitmap.Save(path);
        Console.WriteLine("SCREENSHOT " + path);
    }

    private static void CheckLanguagePlacement(ToolStrip toolbar, ToolStripComboBox selector, string name)
    {
        var items = toolbar.Items.Cast<ToolStripItem>().ToArray();
        var label = items.Single(i => i.AccessibleName == "Language");
        var close = items.Single(i => i.AccessibleName == "Close tab");
        var help = items.Single(i => i.AccessibleName == "Shortcuts");
        Check(new[] { label, selector, close, help }.All(i =>
                i.Alignment == ToolStripItemAlignment.Right && i.Placement == ToolStripItemPlacement.Main)
            && label.Bounds.Right <= selector.Bounds.Left && selector.Bounds.Right <= close.Bounds.Left
            && close.Bounds.Right <= help.Bounds.Left,
            name + " right group orders language, close tab and shortcuts");
        var visible = items.Where(i => i.Placement == ToolStripItemPlacement.Main && i.Available).ToArray();
        Check(!visible.SelectMany((item, index) => visible.Skip(index + 1)
                .Select(other => item.Bounds.IntersectsWith(other.Bounds))).Any(overlap => overlap)
            && selector.Width >= selector.Items.Cast<string>().Max(s => TextRenderer.MeasureText(s, selector.Font).Width)
                + SystemInformation.VerticalScrollBarWidth,
            name + " toolbar has no overlaps and native language names fit");
    }

    private static async Task SelectLanguageFromDropdown(ToolStripComboBox selector, int index)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Selected(object? sender, EventArgs e)
        {
            if (selector.SelectedIndex == index) changed.TrySetResult();
        }
        selector.SelectedIndexChanged += Selected;
        try
        {
            selector.ComboBox.Focus();
            selector.ComboBox.DroppedDown = true;
            int direction = index > selector.SelectedIndex ? 0x28 : 0x26;
            int steps = Math.Abs(index - selector.SelectedIndex);
            for (int step = 0; step < steps; step++)
                PostMessage(selector.ComboBox.Handle, 0x100, direction, 0);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            selector.ComboBox.DroppedDown = false;
        }
        finally { selector.SelectedIndexChanged -= Selected; }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    private static Task NavigationSignal(ShellViewHost host)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? completed = null;
        EventHandler<string>? failed = null;
        completed = (_, _) =>
        {
            host.Navigated -= completed;
            host.NavigationError -= failed;
            source.TrySetResult();
        };
        failed = (_, error) =>
        {
            host.Navigated -= completed;
            host.NavigationError -= failed;
            source.TrySetException(new IOException(error));
        };
        host.Navigated += completed;
        host.NavigationError += failed;
        return source.Task;
    }

    private static async Task Navigate(ShellViewHost host, Action trigger)
    {
        var navigation = NavigationSignal(host);
        trigger();
        await navigation.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static void ClickLayout(MainForm form, int count)
    {
        var button = Descendants(form).OfType<ToolStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(item => item.AccessibleName == $"{count} pane layout");
        button.PerformClick();
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new InvalidOperationException(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint context, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetProp(nint window, string property);

    private static bool HasNativeDropTarget(Control host)
    {
        bool registered = false;
        EnumChildWindows(host.Handle, (window, _) =>
        {
            registered |= GetProp(window, "OleDropTargetInterface") != 0
                || GetProp(window, "OleDropTargetMarshalHwnd") != 0;
            return true;
        }, 0);
        return registered;
    }

    private static string[] SelectedPaths(ShellViewHost host)
    {
        var view = (IShellView)typeof(ShellViewHost)
            .GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(host)!;
        Guid dataId = new("0000010e-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(view.GetItemObject(1, ref dataId, out nint pointer));
        var data = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
        };
        try
        {
            data.GetData(ref format, out var medium);
            try
            {
                uint count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
                var paths = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint length = DragQueryFile(medium.unionmember, i, null, 0);
                    var buffer = new System.Text.StringBuilder((int)length + 1);
                    DragQueryFile(medium.unionmember, i, buffer, length + 1);
                    paths[i] = buffer.ToString();
                }
                return paths;
            }
            finally { ReleaseStgMedium(ref medium); }
        }
        finally { Marshal.ReleaseComObject(data); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, System.Text.StringBuilder? path, uint size);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
}
