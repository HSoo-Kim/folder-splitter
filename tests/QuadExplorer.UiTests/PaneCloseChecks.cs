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
