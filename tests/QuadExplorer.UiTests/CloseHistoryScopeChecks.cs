using QuadExplorer;

internal static partial class Program
{
    private static void RunCloseHistoryScopeScenario(string root, string[] folders)
    {
        string session = Path.Combine(root, "history-scopes.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 2, ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [folders[0]] }, new PaneState { Tabs = [folders[1]] }]
        });
        using var form = new MainForm(session, []);
        var a = form.GetPane(0);
        var b = form.GetPane(1);
        var common = form.Controls.OfType<ToolStrip>().Single();
        ToolStripButton Global(string action) => common.Items.OfType<ToolStripButton>()
            .Single(item => item.AccessibleName == $"{action} close");
        ToolStripButton Local(ExplorerPane pane, string action) => Descendants(pane).OfType<ToolStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(item => item.AccessibleName == $"Pane {pane.PaneNumber} {action} close");
        form.Shown += async (_, _) =>
        {
            try
            {
                foreach (var pane in new[] { a, b })
                    if (pane.ActiveView is { HasNavigated: false } view)
                        await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                // ToolStrip shows its own item tooltips, disabled items included; the text must exist from startup.
                var localStrip = Local(a, "Undo").Owner!;
                Check(!Global("Undo").Enabled && common.ShowItemToolTips && localStrip.ShowItemToolTips
                    && Global("Undo").ToolTipText is { } globalTip && globalTip.Contains("Ctrl+Shift+T")
                    && Global("Redo").ToolTipText is { Length: > 0 }
                    && Local(a, "Undo").ToolTipText is { Length: > 0 } localTip && !localTip.Contains("Ctrl+Shift+T"),
                    "unavailable undo buttons explain their scope from startup; only the global one names Ctrl+Shift+T");
                var aView = a.ActiveView;
                var bView = b.ActiveView;
                a.AddBlankTab(); a.CloseTab();
                b.AddBlankTab(); b.CloseTab();
                Check(Local(a, "Undo").Enabled && Local(b, "Undo").Enabled && Global("Undo").Enabled,
                    "each pane tracks its own closed tabs while the global undo tracks both");
                Local(a, "Undo").PerformClick();
                Check(a.TabCount == 2 && a.ActiveView is null && b.TabCount == 1 && Local(b, "Undo").Enabled,
                    "local undo restores pane A even though pane B was closed more recently");
                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                {
                    foreach (string theme in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(theme);
                        form.Refresh();
                        using var image = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(output, $"history-scopes-{theme}.png"));
                    }
                }
                Global("Undo").PerformClick();
                Check(a.TabCount == 2 && b.TabCount == 2 && b.ActiveView is null
                    && !Local(a, "Undo").Enabled && !Local(b, "Undo").Enabled && !Global("Undo").Enabled,
                    "global undo skips the tab already restored locally and consumes pane B's matching local record");
                Local(a, "Redo").PerformClick();
                Check(a.TabCount == 1 && a.ActiveView == aView && b.TabCount == 2 && Global("Redo").Enabled,
                    "local redo consumes only pane A's restored tab and leaves pane B's global redo");
                Global("Redo").PerformClick();
                Check(a.TabCount == 1 && b.TabCount == 1 && b.ActiveView == bView
                    && !Local(a, "Redo").Enabled && !Local(b, "Redo").Enabled,
                    "global redo skips the tab already closed locally and consumes pane B's local redo record");
                Global("Undo").PerformClick();
                Local(a, "Undo").PerformClick();
                Global("Undo").PerformClick();
                Check(a.TabCount == 2 && b.TabCount == 2,
                    "mixing local and global undo never duplicates an already restored tab");
                b.CloseTab();
                Check(Local(a, "Redo").Enabled && !Local(b, "Redo").Enabled && !Global("Redo").Enabled,
                    "a new close in pane B forks global redo but preserves pane A's independent local redo");
                Local(a, "Redo").PerformClick();
                Check(a.TabCount == 1 && b.TabCount == 1 && a.ActiveView == aView && b.ActiveView == bView,
                    "pane A can redo its own close after unrelated activity in pane B");
                Global("Undo").PerformClick();
                Global("Undo").PerformClick();
                a.CloseTab(1, remember: false);
                b.CloseTab(1, remember: false);
                var layout = form.CurrentLayout;
                a.TabStrip.Items.OfType<ToolStripButton>().Single(item => item.AccessibleName == "Pane 1 close pane").PerformClick();
                Check(!a.Visible && Global("Undo").Enabled && !Local(b, "Undo").Enabled,
                    "an entire pane close appears only in global history, not another pane's local tab history");
                Local(b, "Undo").PerformClick();
                Check(!a.Visible, "a local undo button cannot restore an unrelated closed pane");
                Global("Undo").PerformClick();
                Check(a.Visible && form.CurrentLayout == layout && a.TabCount == 1 && a.ActiveView == aView
                    && Global("Redo").Enabled && !Local(a, "Redo").Enabled,
                    "global undo restores an entire pane with its live tabs without creating local tab redo");
                Global("Redo").PerformClick();
                Global("Undo").PerformClick();
                Check(a.Visible && b.Visible && a.ActiveView == aView && b.ActiveView == bView,
                    "global pane undo and redo preserve both live folder views");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }
}
