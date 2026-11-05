using QuadExplorer;

internal static partial class Program
{
    private static ListBox SavedGroupList(MainForm window) => Descendants(window).OfType<ListBox>()
        .Single(list => list.AccessibleName == "Saved groups");

    private static void SavedGroupToggle(ExplorerPane pane, TabGroup group)
    {
        var menu = TgOpenGroupMenu(pane, group);
        var toggle = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Save group");
        Check(toggle.Checked == (group.SavedId is not null), "saved-group chip toggle reflects the current saved link");
        menu.Close();
        toggle.PerformClick();
    }

    private static void SavedGroupOpen(MainForm window, int index = 0)
    {
        var list = SavedGroupList(window);
        list.SelectedIndex = index;
        list.Focus();
        TgKey(list, Keys.Enter);
    }

    private static void RunSavedGroupScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "SavedGroups")).FullName;
        string[] paths = Enumerable.Range(0, 8).Select(i => Directory.CreateDirectory(Path.Combine(fixture, "Folder-" + i)).FullName).ToArray();
        string session = Path.Combine(fixture, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", GlobalHotkey = false, StartWithWindows = false, CheckUpdates = false,
            AutoRestartMemory = 0, ExplorerFavoritesImported = true, KeepInTray = true,
            Windows = [new() { Panes = [new() { Tabs = paths.Take(3).ToList(),
                Groups = [new() { Name = "Research", Color = "Blue", Count = 2 }] }], ShowFolderTree = false },
                new() { Panes = [new() { Tabs = [paths[3]] }], ShowFolderTree = false }]
        });
        string savedId = "";
        string[] expectedPaths = [];
        using (var context = MultiWindowContext(session))
        {
            RunMultiWindowContext(context, async () =>
            {
                var first = context.Windows[0];
                var second = context.Windows[1];
                await MultiWindowReady(first);
                await MultiWindowReady(second);
                var pane = first.GetPane(0);
                var group = pane.Groups.Single();
                Check(context.Windows.All(window => !SavedGroupList(window).Visible),
                    "saved-group sidebars hide their empty sections in both windows");
                SavedGroupToggle(pane, group);
                savedId = group.SavedId!;
                Check(savedId.Length > 0 && context.SavedGroups.Single().Id == savedId
                    && context.Windows.All(window => SavedGroupList(window).Visible && SavedGroupList(window).Items.Count == 1),
                    "saved-group saving creates one stable app-wide entry and refreshes both sidebars");
                var manageFavorites = Descendants(first).OfType<Button>().Single(button => button.AccessibleName == "Manage favorites");
                Check(SavedGroupList(first).Parent!.Top >= manageFavorites.Bottom,
                    "saved-group section follows the complete Favorites section including its management controls");
                var header = Descendants(first).OfType<Button>().Single(button => button.AccessibleName == "Saved groups");
                header.PerformClick();
                Check(!SavedGroupList(first).Visible && SavedGroupList(second).Visible,
                    "saved-group sidebar collapses independently in one window");
                header.PerformClick();
                Check(SavedGroupList(first).Visible, "saved-group sidebar expands again without changing its entries");
                Check(context.SaveSession() && SessionStore.Load(session).SavedGroups.Single().Id == savedId
                    && SessionStore.Load(session).Windows[0].Panes[0].Groups.Single().SavedId == savedId,
                    "saved-group saving persists both the shared snapshot and open-group stable link");
                pane.RenameGroup(group, "Renamed research");
                pane.SetGroupColor(group, TabGroupColor.Purple);
                Check(context.SavedGroups.Single() is { Name: "Renamed research", Color: "Purple" }
                    && context.Windows.All(window => SavedGroupList(window).Items[0]!.ToString()!.Contains("Renamed research")),
                    "saved-group chip rename and recolor synchronize catalog and every sidebar");
                var outside = pane.Tabs.Single(tab => tab.Group is null);
                pane.AddToGroup(outside, group);
                Check(context.SavedGroups.Single().Paths.SequenceEqual(paths.Take(3)),
                    "saved-group adding a member synchronizes all ordered paths");
                pane.RemoveFromGroup(outside);
                Check(context.SavedGroups.Single().Paths.SequenceEqual(paths.Take(2)),
                    "saved-group removing a member synchronizes without deleting its entry");
                pane.AddToGroup(outside, group);
                pane.CloseTabs([outside]);
                Check(context.SavedGroups.Single().Paths.SequenceEqual(paths.Take(2)),
                    "saved-group partial selection close synchronizes surviving members only after the batch settles");
                var members = pane.GroupMembers(group);
                pane.ReorderTabs([members[1]], pane.IndexOfTab(members[0]), group);
                Check(context.SavedGroups.Single().Paths.SequenceEqual([paths[1], paths[0]]),
                    "saved-group member reorder synchronizes strip order rather than original order");
                pane.SelectTab(pane.IndexOfTab(members[1]));
                await Navigate(pane.ActiveView!, () => pane.Navigate(paths[4]));
                Check(context.SavedGroups.Single().Paths.SequenceEqual([paths[4], paths[0]]),
                    "saved-group member navigation synchronizes its new folder path");
                pane.SetGroupCollapsed(group, true);
                int before = pane.TabCount;
                SavedGroupOpen(second);
                Check(context.Windows.Count == 2 && pane.TabCount == before && !group.Collapsed
                    && pane.ActiveTab == pane.GroupMembers(group)[0] && first.Visible,
                    "saved-group opening from a peer focuses and expands the existing group without duplication");
                context.RenameSavedGroup(savedId, "Sidebar rename");
                Check(group.Name == "Sidebar rename" && context.SavedGroups.Single().Name == "Sidebar rename",
                    "saved-group catalog rename updates the linked open chip");
                expectedPaths = context.SavedGroups.Single().Paths.ToArray();
                pane.CloseGroup(group);
                Check(context.SavedGroups.Single().Paths.SequenceEqual(expectedPaths)
                    && context.Windows.All(window => SavedGroupList(window).Items.Count == 1),
                    "saved-group closing an open group keeps its complete snapshot through intermediate member removals");
                pane.ReopenTab();
                group = pane.Groups.Single(candidate => candidate.SavedId == savedId);
                Check(pane.GroupMembers(group).Count == expectedPaths.Length && context.SavedGroups.Single().Paths.SequenceEqual(expectedPaths),
                    "saved-group close undo restores the stable link without rewriting its saved snapshot");
                pane.CloseGroup(group);
                SavedGroupOpen(second);
                var peer = second.GetPane(0);
                var reopened = peer.Groups.Single(candidate => candidate.SavedId == savedId);
                Check(reopened is { Name: "Sidebar rename", Color: TabGroupColor.Purple }
                    && peer.GroupMembers(reopened).Select(tab => tab.Host!.LocationPath).SequenceEqual(expectedPaths),
                    "saved-group sidebar reopen restores all members and metadata in the invoking pane");
                peer.CloseGroup(reopened);
                while (peer.TabCount < 31) peer.AddBlankTab();
                before = peer.TabCount;
                SavedGroupOpen(second);
                Check(peer.TabCount == before && peer.Groups.Count == 0 && context.SavedGroups.Single().Paths.SequenceEqual(expectedPaths),
                    "saved-group pane-capacity refusal opens nothing partial and preserves its saved entry");
                // Open the sidebar context menu through the same selected-row command used by mouse and keyboard.
                var list = SavedGroupList(second);
                list.SelectedIndex = 0;
                list.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Open in new window").PerformClick();
                var third = context.Windows.Single(window => window != first && window != second);
                await MultiWindowReady(third);
                var thirdPane = third.GetPane(0);
                var thirdGroup = thirdPane.Groups.Single();
                Check(thirdGroup.SavedId == savedId && thirdPane.CaptureState().Tabs.SequenceEqual(expectedPaths)
                    && SavedGroupList(third).Items.Count == 1,
                    "saved-group context menu opens a complete linked group in a new shared-catalog window");
                SavedGroupToggle(thirdPane, thirdGroup);
                Check(context.SavedGroups.Count == 0 && thirdGroup.SavedId is null && thirdPane.GroupMembers(thirdGroup).Count == 2
                    && context.Windows.All(window => !SavedGroupList(window).Visible),
                    "saved-group unsave toggle forgets the catalog everywhere but leaves the open group intact");
                SavedGroupToggle(thirdPane, thirdGroup);
                savedId = thirdGroup.SavedId!;
                var cappedIds = new List<string>();
                for (int index = 1; index < SessionStore.SavedGroupLimit; index++)
                {
                    var candidate = thirdPane.AddGroup([paths[5]], "Saved " + index, TabGroupColor.Green)!;
                    context.ToggleSavedGroup(thirdPane, candidate);
                    cappedIds.Add(candidate.SavedId!);
                    thirdPane.CloseGroup(candidate, remember: false);
                }
                var rejected = thirdPane.AddGroup([paths[6]], "Thirty-first", TabGroupColor.Red)!;
                Check(context.SavedGroups.Count == 30 && context.SavedGroups[0].Id == cappedIds[^1]
                    && !context.ToggleSavedGroup(thirdPane, rejected) && rejected.SavedId is null
                    && context.Windows.All(window => SavedGroupList(window).Items.Count == 30),
                    "saved-group catalog is newest-first, caps at thirty, and refuses the thirty-first without eviction");
                thirdPane.CloseGroup(rejected, remember: false);
                foreach (string id in cappedIds) context.DeleteSavedGroup(id);
                var deleteList = SavedGroupList(third);
                deleteList.SelectedIndex = 0;
                deleteList.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Delete").PerformClick();
                Check(thirdGroup.SavedId is null && thirdPane.GroupMembers(thirdGroup).Count == expectedPaths.Length
                    && context.Windows.All(window => SavedGroupList(window).Items.Count == 0),
                    "saved-group delete clears every sidebar and unsaves linked open groups without closing tabs");
                SavedGroupToggle(thirdPane, thirdGroup);
                savedId = thirdGroup.SavedId!;
                foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                {
                    third.SetLanguage(language);
                    var localized = new UiText(language);
                    Check(context.Windows.All(window => SavedGroupList(window).Items[0]!.ToString() ==
                        localized.T("{0} ({1} tabs)", thirdGroup.Name, expectedPaths.Length)),
                        $"saved-group {language} updates every sidebar's label and count");
                }
                third.SetLanguage("en");
                foreach (string theme in new[] { "dark", "light" })
                {
                    third.SetColorTheme(theme);
                    Check(context.Windows.All(window => SavedGroupList(window).BackColor == Theme.Sidebar),
                        $"saved-group {theme} refreshes both sidebar surfaces");
                    var capturedList = SavedGroupList(third);
                    using var rows = new Bitmap(capturedList.Width, capturedList.Height);
                    capturedList.DrawToBitmap(rows, capturedList.ClientRectangle);
                    int dots = 0;
                    Color dot = Theme.GroupColor(thirdGroup.Color);
                    for (int y = 0; y < Math.Min(rows.Height, capturedList.ItemHeight); y++)
                        for (int x = 0; x < Math.Min(rows.Width, capturedList.ItemHeight); x++)
                            if (rows.GetPixel(x, y).ToArgb() == dot.ToArgb()) dots++;
                    Check(dots > 5, $"saved-group {theme} sidebar paints its group-color dot in the first visible row");
                    int countInk = 0;
                    for (int y = 0; y < Math.Min(rows.Height, capturedList.ItemHeight); y++)
                        for (int x = Math.Max(0, rows.Width - (int)Math.Round(25 * third.DeviceDpi / 96d)); x < rows.Width; x++)
                            if (rows.GetPixel(x, y).ToArgb() == Theme.Text.ToArgb()
                                || rows.GetPixel(x, y).ToArgb() == Theme.Accent.ToArgb()) countInk++;
                    Check(countInk > 3, $"saved-group {theme} keeps the member count visible independently of an ellipsized name");
                    string output = Path.Combine(Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR")
                        ?? AppContext.BaseDirectory, "artifacts", "saved-groups");
                    Directory.CreateDirectory(output);
                    Control section = capturedList.Parent!;
                    using var image = new Bitmap(section.Width, section.Height);
                    section.DrawToBitmap(image, section.ClientRectangle);
                    image.Save(Path.Combine(output, $"saved-groups-{theme}-sidebar.png"));
                    using var windowImage = new Bitmap(third.Width, third.Height);
                    third.DrawToBitmap(windowImage, new Rectangle(Point.Empty, third.Size));
                    windowImage.Save(Path.Combine(output, $"saved-groups-{theme}-window.png"));
                }
                Check(context.SaveSession(), "saved-group aggregate session saves before restart");
            });
        }
        using (var restored = MultiWindowContext(session))
        {
            RunMultiWindowContext(restored, async () =>
            {
                foreach (var window in restored.Windows) await MultiWindowReady(window);
                var owner = restored.Windows.Single(window => window.GetPane(0).Groups.Any(group => group.SavedId == savedId));
                Check(restored.SavedGroups.Single().Id == savedId
                    && restored.SavedGroups.Single().Paths.SequenceEqual(expectedPaths)
                    && restored.Windows.All(window => SavedGroupList(window).Items.Count == 1),
                    "saved-group restart restores stable links, complete saved paths, and all window sidebars");
                int tabs = restored.Windows.Sum(window => window.GetPane(0).TabCount);
                SavedGroupOpen(restored.Windows.First(window => window != owner));
                Check(restored.Windows.Sum(window => window.GetPane(0).TabCount) == tabs
                    && owner.GetPane(0).ActiveTab?.Group?.SavedId == savedId,
                    "saved-group already-open lookup still focuses across windows after restart");
                var group = owner.GetPane(0).Groups.Single(candidate => candidate.SavedId == savedId);
                owner.GetPane(0).CloseGroup(group);
                string missing = Path.Combine(fixture, "Missing-folder");
                var state = restored.SavedGroups.Single();
                restored.ChangeState(current => current with { SavedGroups = [state with { Paths = [missing] }] });
                Check(restored.OpenSavedGroup(owner, savedId) && owner.GetPane(0).Groups.Single().SavedId == savedId
                    && owner.GetPane(0).GroupMembers(owner.GetPane(0).Groups.Single()).Count == 1,
                    "saved-group missing folders are still opened as tabs instead of silently omitted");
            });
        }
    }
}
