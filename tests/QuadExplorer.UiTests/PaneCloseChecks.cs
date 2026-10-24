using QuadExplorer;

internal static partial class Program
{
    private static void RunPaneReopenScenario(string root, string[] folders)
    {
        string session = Path.Combine(root, "pane-reopen.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 4, ActivePane = 0, ExplorerFavoritesImported = true,
            LayoutTree = PaneLayout.Preset(4).SetRatio("", .37).SetRatio("0", .6).SetRatio("1", .4),
            Panes = [new PaneState { Tabs = [folders[0]] }, new PaneState { Tabs = [folders[1]] },
                new PaneState { Tabs = [folders[2]] }, new PaneState()]
        });
        using var form = new MainForm(session, []);
        var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
        var shortcut = typeof(MainForm).GetMethod("HandleShortcut",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Key(Keys key) => shortcut.Invoke(form, [key]);
        void Close() => Key(Keys.Control | Keys.W);
        void Reopen() => Key(Keys.Control | Keys.Shift | Keys.T);
        Task ReopenFolder(int pane, string path) => PaneChange(panes[pane], Reopen,
            () => panes[pane].ActiveView is { HasNavigated: true } && panes[pane].CurrentPath == path);
        form.Shown += async (_, _) =>
        {
            try
            {
                foreach (var pane in panes)
                    if (pane.ActiveView is { HasNavigated: false } view)
                        await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                ToolStripButton CloseButton(int index) => panes[index].TabStrip.Items.OfType<ToolStripButton>()
                    .Single(button => button.AccessibleName == $"Pane {index + 1} close pane");
                var startingLayout = form.CurrentLayout;
                var keptView = panes[2].ActiveView;
                panes[2].AddBlankTab();
                panes[0].SelectTab(0);
                CloseButton(2).PerformClick();
                Check(!panes[2].Visible && panes[2].TabCount == 2 && panes[2].ActiveView is null
                    && panes[0].CurrentPath == folders[0],
                    "an inactive pane's X closes that entire pane without closing its folder or blank tabs");
                Reopen();
                Check(form.CurrentLayout == startingLayout && panes[2].Visible && panes[2].TabCount == 2
                    && panes[2].SelectedIndex == 1 && panes[2].ActiveView is null,
                    "Ctrl+Shift+T restores the pane closed by X with its layout and selected blank tab");
                panes[2].SelectTab(0);
                Check(ReferenceEquals(panes[2].ActiveView, keptView),
                    "pane X and restore retain the same live folder view");
                panes[2].CloseTab(1, remember: false);
                ToolStripButton HistoryButton(int pane, string action) => Descendants(panes[pane]).OfType<ToolStrip>()
                    .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
                    .Single(button => button.AccessibleName == $"Pane {pane + 1} {action} close");
                var common = form.Controls.OfType<ToolStrip>().Single();
                ToolStripButton GlobalHistoryButton(string action) => common.Items.OfType<ToolStripButton>()
                    .Single(button => button.AccessibleName == $"{action} close");
                Check(GlobalHistoryButton("Redo").Enabled && !GlobalHistoryButton("Undo").Enabled
                    && !HistoryButton(2, "Redo").Enabled,
                    "restoring the only closed pane enables redo without inventing undo history");
                panes[0].SelectTab(0);
                GlobalHistoryButton("Redo").PerformClick();
                Check(!panes[2].Visible && panes[2].TabCount == 1 && GlobalHistoryButton("Undo").Enabled,
                    "global redo closes the restored pane rather than the active pane");
                GlobalHistoryButton("Undo").PerformClick();
                Check(panes[2].Visible && form.CurrentLayout == startingLayout && ReferenceEquals(panes[2].ActiveView, keptView),
                    "undo and redo repeatedly preserve the pane's original layout and shell view");
                panes[2].AddBlankTab();
                panes[2].CloseTab();
                Check(!GlobalHistoryButton("Redo").Enabled && GlobalHistoryButton("Undo").Enabled,
                    "a new tab close clears the redo branch and enables global undo even in a blank pane");
                panes[3].FocusAddress();
                GlobalHistoryButton("Undo").PerformClick();
                var restoredBlank = panes[2].ActiveTab;
                Check(panes[2].TabCount == 2 && restoredBlank?.Host is null && GlobalHistoryButton("Redo").Enabled,
                    "the undo button restores the most recently closed blank tab");
                GlobalHistoryButton("Redo").PerformClick();
                Check(panes[2].TabCount == 1 && panes[2].ActiveView == keptView,
                    "the redo button closes the exact restored tab without touching its neighboring folder tab");
                Reopen();
                Check(panes[2].TabCount == 2 && panes[2].ActiveView is null,
                    "Ctrl+Shift+T shares the button history after redo");
                panes[2].CloseTab(1, remember: false);
                Check(!GlobalHistoryButton("Redo").Enabled,
                    "removing a restored tab by another path disables its stale redo entry");
                panes[0].AddBlankTab();
                panes[0].CloseTab();
                CloseButton(1).PerformClick();
                GlobalHistoryButton("Undo").PerformClick();
                GlobalHistoryButton("Undo").PerformClick();
                Check(panes[1].Visible && panes[0].TabCount == 2,
                    "successive undo restores a pane then the earlier tab in reverse close order");
                GlobalHistoryButton("Redo").PerformClick();
                Check(panes[0].TabCount == 1 && panes[1].Visible && GlobalHistoryButton("Redo").Enabled,
                    "redoing a tab close retains the next pane redo entry");
                GlobalHistoryButton("Redo").PerformClick();
                Check(!panes[1].Visible && panes[0].TabCount == 1,
                    "successive redo replays the original tab then pane close order");
                GlobalHistoryButton("Undo").PerformClick();
                GlobalHistoryButton("Undo").PerformClick();
                panes[0].CloseTab(1, remember: false);
                Size startingSize = form.Size;
                foreach (string theme in new[] { "light", "dark" })
                foreach (string size in new[] { "normal", "large" })
                foreach (bool narrow in new[] { false, true })
                {
                    form.SetColorTheme(theme);
                    form.SetUiFont(null, size);
                    form.Size = narrow ? new Size(900, 600) : new Size(1500, 900);
                    form.Refresh();
                    if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } captures)
                    {
                        using var image = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(captures, $"pane-x-{theme}-{size}-{(narrow ? "minimum" : "regular")}.png"));
                    }
                    Check(panes.All(pane =>
                    {
                        var strip = pane.TabStrip;
                        var close = CloseButton(pane.PaneNumber - 1);
                        return close.Enabled && close.Available && close.Alignment == ToolStripItemAlignment.Right
                            && close.Height >= TextRenderer.MeasureText(close.Text, close.Font).Height
                            && strip.ClientRectangle.Contains(close.Bounds)
                            && strip.Items.Cast<ToolStripItem>().Where(item => item.Available && item != close)
                                .All(item => !item.Bounds.IntersectsWith(close.Bounds));
                    }), $"pane close targets stay at the right without clipping or overlapping tabs in {theme}/{size}/{narrow}");
                    Check(panes.Where(pane => pane.ActiveTab is not null).All(pane =>
                    {
                        var strip = pane.TabStrip;
                        var tab = pane.ActiveTab!.Button;
                        var close = CloseButton(pane.PaneNumber - 1);
                        using var image = new Bitmap(strip.Width, strip.Height);
                        strip.DrawToBitmap(image, new Rectangle(Point.Empty, strip.Size));
                        Size InkSize(Rectangle target, Color ink)
                        {
                            Color background = image.GetPixel(target.Left + 1, target.Top + target.Height / 2);
                            int radius = (int)Math.Ceiling(8 * strip.DeviceDpi / 96d);
                            var sample = new Rectangle(target.Left + target.Width / 2 - radius,
                                target.Top + target.Height / 2 - radius, radius * 2 + 1, radius * 2 + 1);
                            sample.Intersect(target);
                            int left = sample.Right, top = sample.Bottom, right = -1, bottom = -1;
                            for (int x = sample.Left; x < sample.Right; x++)
                            for (int y = sample.Top; y < sample.Bottom; y++)
                            {
                                Color pixel = image.GetPixel(x, y);
                                int inkDistance = Math.Abs(pixel.R - ink.R) + Math.Abs(pixel.G - ink.G) + Math.Abs(pixel.B - ink.B);
                                int backgroundDistance = Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G)
                                    + Math.Abs(pixel.B - background.B);
                                // Fractional DPI antialiases the entire stroke; count majority-ink pixels, not only near-solid ones.
                                if (inkDistance >= backgroundDistance) continue;
                                left = Math.Min(left, x); top = Math.Min(top, y);
                                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                            }
                            return right < 0 ? Size.Empty : new Size(right - left + 1, bottom - top + 1);
                        }
                        var tabTarget = tab.CloseBounds;
                        tabTarget.Offset(tab.Bounds.Location);
                        Size tabInk = InkSize(tabTarget, tab.Selected ? Theme.Text : Theme.Muted);
                        Size paneInk = InkSize(close.Bounds, close.Selected ? Theme.Text : Theme.Muted);
                        return !tabInk.IsEmpty && !paneInk.IsEmpty
                            && Math.Abs(tabInk.Width - paneInk.Width) <= 1 && Math.Abs(tabInk.Height - paneInk.Height) <= 1
                            && close.Width >= tab.CloseBounds.Width;
                    }), $"pane and tab X marks have matching rendered size while preserving the pane hit target in {theme}/{size}/{narrow}");
                    Check(panes.All(pane =>
                    {
                        var strip = Descendants(pane).OfType<ToolStrip>().Single(each => each.AccessibleName == $"Pane {pane.PaneNumber} commands");
                        var undo = HistoryButton(pane.PaneNumber - 1, "Undo");
                        var redo = HistoryButton(pane.PaneNumber - 1, "Redo");
                        var search = strip.Items.OfType<ToolStripTextBox>().Single();
                        if (pane.PaneNumber == 1 && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } iconCaptures)
                        {
                            using var row = new Bitmap(strip.Width, strip.Height);
                            strip.DrawToBitmap(row, new Rectangle(Point.Empty, strip.Size));
                            var bounds = Rectangle.Union(undo.Bounds, redo.Bounds);
                            bounds.Inflate(2, 2);
                            bounds.Intersect(strip.ClientRectangle);
                            using var icons = row.Clone(bounds, row.PixelFormat);
                            icons.Save(Path.Combine(iconCaptures, $"history-icons-{theme}-{size}-{(narrow ? "minimum" : "regular")}.png"));
                        }
                        return undo.Available && redo.Available && undo.Bounds.Right <= redo.Bounds.Left
                            && strip.ClientRectangle.Contains(undo.Bounds) && strip.ClientRectangle.Contains(redo.Bounds)
                            && strip.Items.Cast<ToolStripItem>().Where(item => item.Available)
                                .All(item => item.Placement != ToolStripItemPlacement.None)
                            && (!search.Available || redo.Bounds.Right <= Theme.SearchFieldBounds(search).Left);
                    }), $"undo and redo remain ordered to the left of the magnifier in {theme}/{size}/{narrow}");
                    var globalUndo = GlobalHistoryButton("Undo");
                    var globalRedo = GlobalHistoryButton("Redo");
                    var settings = common.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Settings");
                    int historyGap = (int)Math.Round(12 * common.DeviceDpi / 96d);
                    Check(globalUndo.Placement == ToolStripItemPlacement.Main && globalRedo.Placement == ToolStripItemPlacement.Main
                        && globalUndo.Alignment == ToolStripItemAlignment.Right && globalRedo.Alignment == ToolStripItemAlignment.Right
                        && globalUndo.Bounds.Right <= globalRedo.Bounds.Left
                        && settings.Bounds.Left - globalRedo.Bounds.Right == historyGap
                        && common.ClientRectangle.Contains(globalUndo.Bounds) && common.ClientRectangle.Contains(globalRedo.Bounds)
                        && common.Items.Cast<ToolStripItem>().Where(item => item.Available && item.Placement == ToolStripItemPlacement.Main
                            && item != globalUndo && item != globalRedo)
                            .All(item => !item.Bounds.IntersectsWith(globalUndo.Bounds) && !item.Bounds.IntersectsWith(globalRedo.Bounds)),
                        $"global undo/redo stay right-aligned with a 12-DIP gap left of Settings in {theme}/{size}/{narrow}");
                }
                form.SetUiFont(null, "normal");
                form.Size = startingSize;
                panes[0].SelectTab(0);
                var original = form.CurrentLayout;
                var originalSize = panes[0].Size;
                var survivor = panes[1].ActiveView;
                Close();
                Close();
                Reopen();
                Check(form.CurrentLayout == original && panes[0].Visible && panes[0].TabCount == 0
                    && panes[0].Size == originalSize && panes[0].ContainsFocus,
                    "undo restores an empty pane at its original position and split ratios with keyboard focus");
                Check(ReferenceEquals(panes[1].ActiveView, survivor) && panes[1].CurrentPath == folders[1],
                    "restoring a split preserves the surviving native tab");

                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                {
                    foreach (string theme in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(theme);
                        form.Refresh();
                        using var image = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(output, $"pane-reopen-{theme}.png"));
                    }
                }

                await ReopenFolder(0, folders[0]);
                Check(panes[0].TabCount == 1 && form.CurrentLayout == original,
                    "the next undo restores the tab in the restored pane without duplicating a split");

                Close();
                Close();
                Close();
                await ReopenFolder(1, folders[1]);
                Check(!panes[0].Visible && panes[1].TabCount == 1,
                    "a tab closed after a pane is restored first");
                Reopen();
                Check(panes[0].Visible && panes[0].TabCount == 0 && form.CurrentLayout == original,
                    "undo then restores the earlier empty pane");
                await ReopenFolder(0, folders[0]);
                Check(panes[0].TabCount == 1 && panes[1].TabCount == 1,
                    "interleaved pane and tab closes are undone in reverse order");

                Key(Keys.F6);
                Key(Keys.F6);
                Key(Keys.F6);
                Close();
                var withoutLast = form.CurrentLayout;
                Close();
                Close();
                Reopen();
                Check(form.CurrentLayout == withoutLast && panes[0].Visible && !panes[3].Visible,
                    "multiple closed splits restore the newest layout first");
                await ReopenFolder(0, folders[0]);
                Reopen();
                Check(form.CurrentLayout == original && panes[3].Visible && panes[3].TabCount == 0,
                    "the older split restores its original placement and proportions");
                Reopen();
                Check(form.CurrentLayout == original && panes.Sum(p => p.TabCount) == 3,
                    "undo with no remaining history neither adds a fifth pane nor duplicates tabs");

                Key(Keys.Control | Keys.T);
                Close();
                Close();
                Reopen();
                Check(panes[3].Visible && panes[3].TabCount == 0, "a closed blank-tab pane restores empty first");
                Reopen();
                Check(panes[3].TabCount == 1 && panes[3].ActiveView is null && !panes[3].RecentPaths.Any(),
                    "undo restores a blank new tab without adding a fake recent folder");
                Close();
                panes[3].ReopenTab();
                Reopen();
                Check(panes[3].TabCount == 1, "pane-local tab restore consumes the matching global undo record");

                Close();
                Close();
                form.ApplyLayout(2);
                Reopen();
                Check(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 })
                    && panes[3].TabCount == 1 && panes[2].TabCount == 1 && !panes[2].Visible,
                    "an explicit layout change invalidates old pane snapshots while hidden tab undo preserves current panes");
                while (panes[3].TabCount < 32) panes[3].AddBlankTab();
                Close();
                panes[3].AddBlankTab();
                Reopen();
                Check(panes[3].TabCount == 32, "tab undo respects the existing 32-tab limit");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        Check(panes.All(pane => pane.IsDisposed), "closing restored panes releases native views without reviving disposed UI");
        var restored = SessionStore.Load(session, out var warning);
        Check(warning is null && restored.LayoutTree!.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 })
            && restored.Panes[0].Tabs.SequenceEqual(new[] { folders[0] })
            && restored.Panes[1].Tabs.SequenceEqual(new[] { folders[1] }),
            "the restored layout and surviving folders are saved");
    }

    private static void RunPaneCloseScenario(string root, string[] folders)
    {
        string session = Path.Combine(root, "pane-close.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 2, ActivePane = 0, ExplorerFavoritesImported = true,
            Panes = [new PaneState(), new PaneState { Tabs = [folders[0], folders[1]] }]
        });
        using var form = new MainForm(session, []);
        var left = form.GetPane(0);
        var right = form.GetPane(1);
        var shortcut = typeof(MainForm).GetMethod("HandleShortcut",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Key(Keys key) => shortcut.Invoke(form, [key]);
        void Close() => Key(Keys.Control | Keys.W);
        form.Shown += async (_, _) =>
        {
            try
            {
                if (right.ActiveView is { HasNavigated: false } view)
                    await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                var originalView = right.ActiveView;
                ClickLayout(form, 1);
                Check(right.Visible && !left.Visible && right.TabCount == 2
                    && ReferenceEquals(right.ActiveView, originalView),
                    "one pane keeps the populated right pane when the active left pane is empty");
                var finalClose = right.TabStrip.Items.OfType<ToolStripButton>()
                    .Single(button => button.AccessibleName == "Pane 2 close pane");
                finalClose.PerformClick();
                Check(!finalClose.Enabled && right.Visible && right.TabCount == 2,
                    "the final pane's X is disabled and cannot close the app or its tabs");
                ClickLayout(form, 2);
                Check(right.Visible && left.Visible && right.TabCount == 2 && left.TabCount == 0,
                    "expanding again preserves both pane states");

                await PaneChange(left, () => left.Navigate(folders[2]),
                    () => left.ActiveView is { HasNavigated: true });
                right.SelectTab(0);
                Key(Keys.Control | Keys.D1);
                Check(right.Visible && !left.Visible && left.TabCount == 1,
                    "one-pane shortcut keeps the active populated pane without closing hidden tabs");
                ClickLayout(form, 4);
                Check(left.TabCount == 1 && right.TabCount == 2,
                    "restoring four panes retains hidden tabs");

                Key(Keys.F6);
                var before = form.CurrentLayout;
                Close();
                Check(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 })
                    && form.CurrentLayout.Second == before.Second && left.Visible
                    && !form.GetPane(2).Visible && left.Height > right.Height,
                    "Ctrl+W removes an empty nested pane and expands its sibling without changing the other split");
                Key(Keys.F6);
                Key(Keys.F6);
                Close();
                Check(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1 })
                    && left.TabCount == 1 && right.TabCount == 2,
                    "Ctrl+W closes another empty pane in a sparse three-pane layout");

                left.SelectTab(0);
                Close();
                Check(left.TabCount == 0 && left.Visible && right.Visible
                    && form.CurrentLayout.GetPanes().Length == 2,
                    "closing the last tab leaves its empty split visible until the next Ctrl+W");
                int splitWidth = right.Width;
                Close();
                Check(!left.Visible && right.Visible && right.Width > splitWidth
                    && form.CurrentLayout.GetPanes().SequenceEqual(new[] { 1 }) && right.TabCount == 2,
                    "the next Ctrl+W removes the empty split and expands the remaining pane");

                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                {
                    foreach (string theme in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(theme);
                        form.Refresh();
                        using var image = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(output, $"pane-close-{theme}.png"));
                    }
                }

                Close();
                Check(right.TabCount == 1, "keyboard focus transfers to the surviving pane after collapsing a split");
                Close();
                Close();
                Check(right.TabCount == 0 && right.Visible && form.Visible
                    && form.CurrentLayout.GetPanes().SequenceEqual(new[] { 1 }),
                    "repeated Ctrl+W keeps the final empty pane and application open");
                right.AddBlankTab();
                Close();
                Check(right.TabCount == 0 && right.Visible,
                    "Ctrl+W closes a blank new tab without closing the last pane");

                ClickLayout(form, 2);
                Key(Keys.F6);
                Key(Keys.Control | Keys.D1);
                Check(left.Visible && !right.Visible && left.TabCount == 0,
                    "one-pane layout keeps the active pane when all visible panes are empty");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session, out var warning);
        Check(warning is null && restored.Layout == 1 && restored.ActivePane == 0
            && restored.LayoutTree == PaneLayout.Leaf(0) && restored.Panes.All(p => p.Tabs.Count == 0),
            "collapsed layout and final empty pane persist in the session");
    }
}
