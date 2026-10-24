using QuadExplorer;

internal static partial class Program
{
    // Explorer-style command bar, sort/view menus, quick filter, bulk rename, pane comparison and the details pane.
    private static void RunExplorerParityScenario(string root)
    {
        string baseFolder = Directory.CreateDirectory(Path.Combine(root, "Parity")).FullName;
        string left = Directory.CreateDirectory(Path.Combine(baseFolder, "Left")).FullName;
        string right = Directory.CreateDirectory(Path.Combine(baseFolder, "Right")).FullName;
        var stamp = new DateTime(2025, 5, 1, 9, 0, 0, DateTimeKind.Local);
        void Write(string folder, string name, string content, DateTime time)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, content);
            File.SetLastWriteTime(path, time);
        }
        Write(left, "same.txt", "same", stamp);
        Write(right, "same.txt", "same", stamp);
        Write(left, "sized.txt", "short", stamp);
        Write(right, "sized.txt", "a longer text", stamp);
        Write(left, "newer.txt", "n", stamp.AddHours(1));
        Write(right, "newer.txt", "n", stamp);
        Write(right, "only-right.txt", "r", stamp);
        Write(left, "note.txt", "note", stamp);
        string photo = Path.Combine(left, "photo.png");
        using (var image = new Bitmap(200, 120)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.SteelBlue); image.Save(photo); }
        string session = Path.Combine(root, "parity.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 2, Language = "en", ExplorerFavoritesImported = true, ShowPreview = false,
            Panes = [new PaneState { Tabs = [left] }, new PaneState { Tabs = [right] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                var other = form.GetPane(1);
                foreach (var view in new[] { pane.ActiveView!, other.ActiveView! })
                    if (!view.HasNavigated) await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                var host = pane.ActiveView!;
                // Waits for the pane's own selection notification, which also refreshes the command bar.
                async Task Select(ShellViewHost view, IReadOnlyList<string> paths)
                {
                    var owner = form.GetPane(0).ActiveView == view ? form.GetPane(0) : form.GetPane(1);
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Changed(object? sender, EventArgs e) { if (view.SelectionCount == paths.Count) done.TrySetResult(); }
                    owner.SelectionChanged += Changed;
                    try
                    {
                        Check(view.SelectPaths(paths) == paths.Count, "the pane selects the requested items");
                        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    finally { owner.SelectionChanged -= Changed; }
                }
                ToolStripItem Command(string name) => Descendants(pane).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                    .Single(item => item.AccessibleName == "Pane 1 " + name);
                void ChooseMenu(string menu, string label)
                {
                    var button = (ToolStripDropDownButton)Command(menu);
                    button.ShowDropDown();
                    button.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == label).PerformClick();
                    button.HideDropDown();
                }

                // Command bar
                Check(new[] { "New folder", "Cut", "Copy", "Paste", "Rename", "Delete", "Sort", "View", "Compare", "filter" }
                    .All(name => Command(name) is not null), "every pane has File Explorer's commands above the file list");
                Check(!Command("Cut").Enabled && !Command("Delete").Enabled && Command("New folder").Enabled && Command("Paste").Enabled,
                    "selection commands wait for a selection; New folder and Paste do not");
                await Select(host, [Path.Combine(left, "note.txt")]);
                Check(Command("Cut").Enabled && Command("Copy").Enabled && Command("Rename").Enabled && Command("Delete").Enabled,
                    "selecting an item enables Cut, Copy, Rename and Delete");

                // Sort and view
                ChooseMenu("Sort", "Size");
                Check(host.FirstSortColumn is { Property.PropertyId: 12 }, "the Sort menu sorts by size");
                ChooseMenu("Sort", "Descending");
                Check(host.FirstSortColumn is { Property.PropertyId: 12, Direction: -1 }, "the Sort menu switches to descending");
                ChooseMenu("Sort", "Name");
                ChooseMenu("View", "Large icons");
                Check(host.ViewModeAndSize is (1, 96), "the View menu shows large icons");
                ChooseMenu("View", "Details");
                Check(host.ViewModeAndSize is { Mode: 4 }, "the View menu returns to details");
                var viewMenu = (ToolStripDropDownButton)Command("View");
                viewMenu.ShowDropDown();
                var items = viewMenu.DropDownItems.OfType<ToolStripMenuItem>().ToList();
                Check(items.Single(item => item.Text == "Show hidden items").Checked == ShellSettings.HiddenItemsShown
                    && items.Single(item => item.Text == "Show file name extensions").Checked == ShellSettings.ExtensionsShown,
                    "the View menu shows Windows' current hidden-item and extension settings");
                viewMenu.HideDropDown();

                // Pane comparison
                var comparison = FolderComparison.Compare(left, right).ToDictionary(entry => entry.Name, entry => entry.Kind);
                Check(comparison["same.txt"] == FolderComparison.Kind.Same && comparison["sized.txt"] == FolderComparison.Kind.SizeDiffers
                    && comparison["newer.txt"] == FolderComparison.Kind.LeftNewer && comparison["only-right.txt"] == FolderComparison.Kind.OnlyRight
                    && comparison["photo.png"] == FolderComparison.Kind.OnlyLeft, "comparison finds missing, newer and resized files");
                var compare = form.OpenCompare(pane)!;
                await compare.Loading.WaitAsync(TimeSpan.FromSeconds(10));
                var differing = compare.Differing(right: false);
                Check(differing.Count == 4 && !differing.Any(path => path.EndsWith("same.txt")), "pane 1 differs in four items");
                await Select(host, differing);
                Check(host.SelectionCount == 4, "the comparison selects the differing items in a pane");
                compare.Close();

                // Bulk rename
                var plans = BulkRename.Build([Path.Combine(left, "a b.txt"), Path.Combine(left, "c b.txt")], "b", "x", false, false,
                    "{name}-{n}", 7, 2, _ => false);
                Check(plans.Select(plan => plan.NewName).SequenceEqual(["a x-07.txt", "c x-08.txt"]) && plans.All(plan => plan.Problem is null),
                    "bulk rename replaces text, numbers items and keeps the extension");
                Check(BulkRename.Build([Path.Combine(left, "a.txt"), Path.Combine(left, "b.txt")], "", "", false, false, "same", 1, 1, _ => false)
                    .All(plan => plan.Problem == "duplicate"), "bulk rename refuses to give two items one name");
                Check(BulkRename.Build([Path.Combine(left, "a.txt")], "(", "", true, false, "{name}", 1, 1, _ => false)[0].Problem == "pattern"
                    && BulkRename.Build([Path.Combine(left, "a.txt")], "a", "x:", false, false, "{name}", 1, 1, _ => false)[0].Problem == "invalid"
                    && BulkRename.Build([Path.Combine(left, "a.txt")], "a", "b", false, false, "{name}", 1, 1, path => path.EndsWith("b.txt"))[0].Problem == "exists",
                    "bulk rename reports a bad expression, forbidden characters and existing names");
                string first = Path.Combine(left, "draft-1.txt"), second = Path.Combine(left, "draft-2.txt");
                File.WriteAllText(first, "1");
                File.WriteAllText(second, "2");
                using (var dialog = new BulkRenameDialog([first, second], new UiText()))
                {
                    var completed = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
                    dialog.Completed += (done, error) => completed.TrySetResult((done, error));
                    dialog.SetOptions("draft", "final");
                    Check(dialog.CanApply, "the bulk rename preview allows a valid rename");
                    dialog.Show(form);
                    Descendants(dialog).OfType<Button>().Single(button => button.AccessibleName == "Rename").PerformClick();
                    var (renamed, problem) = await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    Check(renamed && problem is null && File.Exists(Path.Combine(left, "final-1.txt")) && File.Exists(Path.Combine(left, "final-2.txt"))
                        && !File.Exists(first), "bulk rename renames the files through Windows");
                }

                // Quick filter
                await ItemCount(host, () => { pane.FilterBoxText = "note"; pane.ApplyFilter(); }, 1);
                Check(host.FilterText == "note" && host.ItemCount == 1, "the quick filter lists only matching names");
                await ItemCount(host, () => { pane.FilterBoxText = ""; pane.ApplyFilter(); }, Directory.GetFileSystemEntries(left).Length);
                Check(host.FilterText == "", "clearing the quick filter lists everything again");
                await ItemCount(host, () => { pane.FilterBoxText = "same"; pane.ApplyFilter(); }, 1);
                await Navigate(host, () => host.Navigate(right));
                Check(host.FilterText == "" && pane.FilterBoxText == "", "the quick filter clears when the tab opens another folder");
                await Navigate(host, () => host.Navigate(left));

                // Details pane
                form.SetDetailsPaneVisible(true);
                Check(form.DetailsPaneVisible && form.Preview.DetailsShown, "the details pane opens together with the preview area");
                var details = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void DetailsChanged(object? sender, EventArgs e) { if (form.Preview.DetailsPath == photo) details.TrySetResult(); }
                form.Preview.DetailsChanged += DetailsChanged;
                await Select(host, [photo]);
                form.UpdatePreview();
                await details.Task.WaitAsync(TimeSpan.FromSeconds(15));
                form.Preview.DetailsChanged -= DetailsChanged;
                var rows = form.Preview.DetailRows;
                Check(rows.Count >= 4 && rows.Any(row => row.Value.Contains("200") && row.Value.Contains("120")),
                    "the details pane lists the image's properties, including its dimensions");

                // New folder last: it leaves the new name being edited.
                int folders = Directory.GetDirectories(left).Length;
                using var watcher = new FileSystemWatcher(left) { NotifyFilter = NotifyFilters.DirectoryName, EnableRaisingEvents = true };
                var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                watcher.Created += (_, _) => created.TrySetResult();
                Command("New folder").PerformClick();
                await created.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(Directory.GetDirectories(left).Length == folders + 1, "New folder creates a folder in the current folder");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        Check(SessionStore.Load(session).ShowDetails, "the details pane setting persists");
    }
}
