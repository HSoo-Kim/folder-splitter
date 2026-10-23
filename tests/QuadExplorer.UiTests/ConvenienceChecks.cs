using QuadExplorer;

internal static partial class Program
{
    private static void RunConvenienceScenario(string root)
    {
        string baseFolder = Directory.CreateDirectory(Path.Combine(root, "Convenience")).FullName;
        string left = Directory.CreateDirectory(Path.Combine(baseFolder, "Left")).FullName;
        string right = Directory.CreateDirectory(Path.Combine(baseFolder, "Right")).FullName;
        string deep = Directory.CreateDirectory(Path.Combine(left, "Sub1", "Sub2")).FullName;
        string middle = Path.GetDirectoryName(deep)!;
        string copyMe = Path.Combine(left, "copy-me.txt");
        string moveMe = Path.Combine(left, "move-me.txt");
        File.WriteAllText(copyMe, "copy");
        File.WriteAllText(moveMe, "move");
        string session = Path.Combine(root, "convenience.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 2, Language = "en", ExplorerFavoritesImported = true,
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

                var segments = ExplorerPane.PathSegments(@"C:\Users\Someone\Data");
                Check(segments.Select(segment => segment.Label).SequenceEqual(["C:", "Users", "Someone", "Data"])
                    && segments[2].Path == @"C:\Users\Someone", "breadcrumb splits a drive path into folders");
                var share = ExplorerPane.PathSegments(@"\\server\share\reports");
                Check(share.Count == 2 && share[0].Label == @"\\server\share" && share[1].Path == @"\\server\share\reports",
                    "breadcrumb keeps a network share as one root");
                Check(ExplorerPane.PathSegments("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}").Count == 0,
                    "shell locations without a path have no breadcrumb folders");

                pane.SelectTab(0);
                await Navigate(pane.ActiveView!, () => pane.Navigate(deep));
                pane.FocusFiles();
                var crumbs = pane.Breadcrumb.Items.Cast<ToolStripItem>().Where(item => item is ToolStripButton).Select(item => item.Text).ToList();
                Check(pane.Breadcrumb.Visible && crumbs.Contains("Sub1") && crumbs.Contains("Sub2"),
                    "the address bar shows clickable path segments");
                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_TAB_CAPTURE") is { Length: > 0 } captures)
                {
                    foreach (string mode in new[] { "light", "dark" })
                    {
                        form.SetColorTheme(mode);
                        form.Refresh();
                        using var shot = PrintForm(form, 2);
                        shot.Save(Path.Combine(captures, $"breadcrumb-{mode}.png"));
                    }
                    form.SetColorTheme("light");
                }
                var chevron = pane.Breadcrumb.Items.OfType<ToolStripDropDownButton>().Single(item => item.AccessibleName == "Subfolders of Sub1");
                chevron.ShowDropDown();
                Check(chevron.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Sub2"), "a segment's arrow lists its subfolders");
                chevron.HideDropDown();
                var sub1 = pane.Breadcrumb.Items.OfType<ToolStripButton>().Single(item => item.Text == "Sub1");
                await Navigate(pane.ActiveView!, () => sub1.PerformClick());
                Check(string.Equals(pane.CurrentPath, middle, StringComparison.OrdinalIgnoreCase), "clicking a segment opens that folder");
                Check(pane.RecentLocationChoices.Contains(deep, StringComparer.OrdinalIgnoreCase)
                    && pane.RecentLocationChoices.Contains(left, StringComparer.OrdinalIgnoreCase)
                    && !pane.RecentLocationChoices.Contains(middle, StringComparer.OrdinalIgnoreCase),
                    "recent locations list earlier folders but not the current one");
                pane.FocusAddress();
                Check(!pane.Breadcrumb.Visible, "editing the address shows the plain text box");
                await Navigate(pane.ActiveView!, () => pane.Navigate(left));
                pane.FocusFiles();

                var plus = pane.TabStrip.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Pane 1 new tab");
                int before = pane.TabCount;
                plus.PerformClick();
                Check(pane.TabCount == before + 1 && pane.ActiveView is null && pane.TabButtons[^1].Title == "New tab",
                    "the new tab button opens a blank tab");
                Check(pane.CaptureState().Tabs.Count == before, "a blank tab is not saved");
                pane.Navigate(right);
                var filled = pane.ActiveView!;
                if (!filled.HasNavigated) await NavigationSignal(filled).WaitAsync(TimeSpan.FromSeconds(20));
                Check(pane.TabCount == before + 1 && string.Equals(pane.CurrentPath, right, StringComparison.OrdinalIgnoreCase),
                    "typing a path in a blank tab opens it in that tab");
                pane.CloseTab();
                pane.SelectTab(0);

                var selected = SelectionSignal(pane.ActiveView!);
                await Navigate(pane.ActiveView!, () => pane.ActiveView!.NavigateAndSelect(copyMe));
                await selected.WaitAsync(TimeSpan.FromSeconds(10));
                await pane.UpdateSelectionSummary();
                Check(pane.SelectionSummary == "1 selected \u00B7 " + ExplorerPane.FormatSize(4),
                    "the status line shows the selection count and size");
                Check(ExplorerPane.PathsForClipboard([copyMe, moveMe]) == "\"" + copyMe + "\"\r\n\"" + moveMe + "\"",
                    "copy path quotes each selected item on its own line");
                var terminal = ExplorerPane.TerminalStartInfo(left, windowsTerminal: true);
                var shell = ExplorerPane.TerminalStartInfo(left, windowsTerminal: false);
                Check(terminal.FileName == "wt.exe" && terminal.ArgumentList.SequenceEqual(["-d", left])
                    && shell.FileName == "powershell.exe" && shell.WorkingDirectory == left,
                    "open terminal starts Windows Terminal in the folder, with PowerShell as fallback");

                Check(form.OtherPane() == other, "the other pane is the remaining visible pane");
                async Task Transfer(bool move)
                {
                    var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Done(bool done, string? error) => finished.TrySetResult(done && error is null);
                    form.FileOperationFinished += Done;
                    try
                    {
                        form.TransferSelection(move);
                        Check(await finished.Task.WaitAsync(TimeSpan.FromSeconds(30)), move ? "move to the other pane completes" : "copy to the other pane completes");
                    }
                    finally { form.FileOperationFinished -= Done; }
                }
                await Transfer(move: false);
                Check(File.Exists(Path.Combine(right, "copy-me.txt")) && File.Exists(copyMe), "copy leaves the original and adds it to the other pane's folder");
                selected = SelectionSignal(pane.ActiveView!);
                await Navigate(pane.ActiveView!, () => pane.ActiveView!.NavigateAndSelect(moveMe));
                await selected.WaitAsync(TimeSpan.FromSeconds(10));
                await Transfer(move: true);
                Check(File.Exists(Path.Combine(right, "move-me.txt")) && !File.Exists(moveMe), "move takes the item to the other pane's folder");

                form.SaveWorkspace("Research");
                form.SaveWorkspace("Scratch");
                form.DeleteWorkspace("Scratch");
                Check(form.Workspaces.Select(workspace => workspace.Name).SequenceEqual(["Research"]), "workspaces can be saved and deleted");
                form.ApplyLayout(1);
                await Navigate(pane.ActiveView!, () => pane.Navigate(deep));
                form.LoadWorkspace("Research");
                var restored = pane.ActiveView!;
                if (!restored.HasNavigated) await NavigationSignal(restored).WaitAsync(TimeSpan.FromSeconds(20));
                Check(other.Visible && string.Equals(pane.CurrentPath, left, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(other.CurrentPath, right, StringComparison.OrdinalIgnoreCase),
                    "opening a workspace restores its panes and folders");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        Check(SessionStore.Load(session).Workspaces.Select(workspace => workspace.Name).SequenceEqual(["Research"]),
            "saved workspaces persist");
    }
}
