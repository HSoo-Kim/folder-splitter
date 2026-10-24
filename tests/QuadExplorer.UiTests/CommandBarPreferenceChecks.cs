using QuadExplorer;

internal static partial class Program
{
    private static void RunCommandBarPreferenceScenario(string root)
    {
        string left = Directory.CreateDirectory(Path.Combine(root, "CommandBarLeft")).FullName;
        string right = Directory.CreateDirectory(Path.Combine(root, "CommandBarRight")).FullName;
        File.WriteAllText(Path.Combine(left, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(left, "other.txt"), "other");
        File.WriteAllText(Path.Combine(right, "reference.txt"), "reference");
        string session = Path.Combine(root, "command-bar.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 3, ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [left] }, new PaneState { Tabs = [right] }, new PaneState()]
        });
        using (var form = new MainForm(session, []))
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    var pane = form.GetPane(0);
                    var host = pane.ActiveView!;
                    foreach (var view in new[] { host, form.GetPane(1).ActiveView! })
                        if (!view.HasNavigated) await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                    var bar = Descendants(pane).OfType<ToolStrip>().Single(s => s.AccessibleName == "Pane 1 commands");
                    var common = form.Controls.OfType<ToolStrip>().Single();
                    var settings = common.Items.OfType<ToolStripDropDownButton>().Single(i => i.AccessibleName == "Settings");
                    ContextMenuStrip Options(ExplorerPane owner)
                    {
                        Descendants(owner).OfType<Button>().Single(b => b.AccessibleName == $"Pane {owner.PaneNumber} Pane options").PerformClick();
                        return (ContextMenuStrip)typeof(ExplorerPane).GetField("tabMenu",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
                    }
                    ToolStripMenuItem ViewMenu(ExplorerPane owner) => Options(owner).Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "View");
                    void ToggleBar(ExplorerPane owner)
                    {
                        var menu = ViewMenu(owner);
                        menu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.AccessibleName == "Show command bar").PerformClick();
                        ((ContextMenuStrip)menu.Owner!).Close();
                    }
                    var initialMenu = ViewMenu(pane);
                    var toggle = initialMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.AccessibleName == "Show command bar");
                    int oldHeight = pane.FileAreaOnScreen.Height;
                    Check(toggle.Checked && bar.Visible, "command bars remain visible by default");
                    Check(!settings.DropDownItems.Cast<ToolStripItem>().Any(i => i.AccessibleName == "Show command bar"),
                        "command bar visibility is placed in View rather than the unrelated Settings menu");
                    toggle.PerformClick();
                    ((ContextMenuStrip)initialMenu.Owner!).Close();
                    Check(!bar.Visible && pane.FileAreaOnScreen.Height > oldHeight
                        && !Descendants(form.GetPane(1)).OfType<ToolStrip>().Single(s => s.AccessibleName == "Pane 2 commands").Visible,
                        "the View toggle hides every command bar and gives the space to the file lists");
                    var shortcut = typeof(MainForm).GetMethod("HandleShortcut",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    void FilterDialog(string value, bool accept)
                    {
                        form.BeginInvoke(() =>
                        {
                            var dialog = Application.OpenForms.Cast<Form>().Single(f => f.AccessibleName == "Folder filter");
                            try
                            {
                                var input = Descendants(dialog).OfType<TextBox>().Single();
                                input.Text = value;
                                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                                {
                                    using var image = new Bitmap(dialog.Width, dialog.Height);
                                    dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                                    image.Save(Path.Combine(output, $"folder-filter-{Theme.IsDark}.png"));
                                }
                                dialog.DialogResult = accept ? DialogResult.OK : DialogResult.Cancel;
                            }
                            finally { dialog.Close(); }
                        });
                        shortcut.Invoke(form, [Keys.Control | Keys.F]);
                    }
                    await ItemCount(host, () => FilterDialog("keep", true), 1);
                    Check(host.FilterText == "keep" && !bar.Visible, "Ctrl+F filters the native list while the command bar stays hidden");
                    FilterDialog("other", false);
                    Check(host.FilterText == "keep", "cancelling the hidden-bar filter leaves the active filter unchanged");
                    form.SetColorTheme("dark");
                    await ItemCount(host, () => FilterDialog("", true), 2);
                    Check(host.FilterText == "", "the hidden-bar filter can clear the active filter");
                    var options = Options(pane);
                    Check(new[] { "New folder", "Filter this folder", "Compare with the other pane", "Sort", "View",
                        "Undo tab close in this pane", "Redo tab close in this pane" }
                        .All(label => options.Items.Cast<ToolStripItem>().Any(i => i.Text == label)),
                        "pane options retain hidden-bar commands including local history, filtering, comparison, sort and view");
                    options.Items.Cast<ToolStripItem>().Single(i => i.Text == "Compare with the other pane").PerformClick();
                    options.Close();
                    var comparison = Application.OpenForms.OfType<CompareDialog>().Single();
                    Check(comparison.Visible, "pane options open the real comparison dialog with the command bar hidden");
                    comparison.Close();
                    options = Options(pane);
                    var viewMenu = options.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "View");
                    var separatorItem = viewMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Vertical separators");
                    var spacingItem = viewMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Row spacing");
                    Check(!separatorItem.Checked && spacingItem.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Comfortable").Checked,
                        "detail appearance defaults to no separators and comfortable rows");
                    host.SetViewModeAndSize(4, 16);
                    var otherHost = form.GetPane(1).ActiveView!;
                    otherHost.SetViewModeAndSize(4, 16);
                    int oldRows = NativeAppearanceRowHeight(NativeAppearanceList(host));
                    int otherOldRows = NativeAppearanceRowHeight(NativeAppearanceList(otherHost));
                    separatorItem.PerformClick();
                    spacingItem.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Compact").PerformClick();
                    options.Close();
                    var otherMenu = ViewMenu(form.GetPane(1));
                    Check(otherMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Vertical separators").Checked
                        && otherMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Row spacing")
                            .DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Compact").Checked,
                        "view preferences changed from one pane are reflected in the other pane's menu");
                    Check(NativeAppearanceRowHeight(NativeAppearanceList(host)) < oldRows
                        && NativeAppearanceRowHeight(NativeAppearanceList(otherHost)) < otherOldRows,
                        "common compact spacing changes both actual native file lists");
                    ((ContextMenuStrip)otherMenu.Owner!).Close();
                    var blank = form.GetPane(2);
                    var blankMenu = ViewMenu(blank);
                    blankMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Vertical separators").PerformClick();
                    blankMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Row spacing")
                        .DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "Comfortable").PerformClick();
                    ((ContextMenuStrip)blankMenu.Owner!).Close();
                    Check(NativeAppearanceRowHeight(NativeAppearanceList(host)) == oldRows
                        && NativeAppearanceRowHeight(NativeAppearanceList(otherHost)) == otherOldRows,
                        "common view settings remain usable from an empty pane and restore both native lists");
                    foreach (string theme in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(theme);
                        form.Refresh();
                        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                        {
                            using var image = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                            image.Save(Path.Combine(output, $"command-bar-hidden-{theme}.png"));
                            var menu = ViewMenu(pane);
                            menu.ShowDropDown();
                            using var menuImage = new Bitmap(menu.DropDown.Width, menu.DropDown.Height);
                            menu.DropDown.DrawToBitmap(menuImage, new Rectangle(Point.Empty, menu.DropDown.Size));
                            menuImage.Save(Path.Combine(output, $"common-view-menu-{theme}.png"));
                            menu.HideDropDown();
                            ((ContextMenuStrip)menu.Owner!).Close();
                        }
                    }
                    ToggleBar(form.GetPane(1));
                    Check(bar.Visible && pane.FileAreaOnScreen.Height == oldHeight, "showing command bars restores the original list height");
                    pane.SelectTab(0);
                    shortcut.Invoke(form, [Keys.Control | Keys.F]);
                    Check(bar.Items.OfType<ToolStripTextBox>().Single().Focused, "Ctrl+F focuses the visible command bar filter");
                    ToggleBar(pane);
                    using var watcher = new FileSystemWatcher(left) { NotifyFilter = NotifyFilters.DirectoryName, EnableRaisingEvents = true };
                    var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    watcher.Created += (_, _) => created.TrySetResult();
                    options = Options(pane);
                    options.Items.Cast<ToolStripItem>().Single(i => i.Text == "New folder").PerformClick();
                    options.Close();
                    await created.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Check(Directory.GetDirectories(left).Length == 1, "pane options create a real folder while the command bar is hidden");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        Check(!SessionStore.Load(session).ShowCommandBar, "hidden command bar preference is saved");
        Check(!SessionStore.Load(session).ShowVerticalSeparators && SessionStore.Load(session).ComfortableRows,
            "detail appearance preferences are saved globally");
        using var reopened = new MainForm(session, []);
        reopened.Shown += (_, _) =>
        {
            try
            {
                Check(!Descendants(reopened.GetPane(0)).OfType<ToolStrip>().Single(s => s.AccessibleName == "Pane 1 commands").Visible,
                    "hidden command bar preference is applied after restarting");
            }
            finally { reopened.ExitApplication(); }
        };
        Application.Run(reopened);
    }
}
