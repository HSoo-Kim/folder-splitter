using System.Text.Json;
using QuadExplorer;
using QuadExplorer.Search;

internal static partial class Program
{
    private static void RunFrequentFolderScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "frequent-folders")).FullName;
        string alpha = Directory.CreateDirectory(Path.Combine(fixture, "fx-alpha")).FullName;
        string beta = Directory.CreateDirectory(Path.Combine(fixture, "fx-beta")).FullName;
        string away = Directory.CreateDirectory(Path.Combine(fixture, "fx-away")).FullName;
        string parent = Directory.CreateDirectory(Path.Combine(fixture, "count-parent")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(parent, "count-child")).FullName;
        const string query = "freq-unique-7e19-ranking";
        string alphaFile = Path.Combine(alpha, query + ".txt");
        string betaFile = Path.Combine(beta, query + ".txt");
        File.WriteAllText(alphaFile, "alpha");
        File.WriteAllText(betaFile, "beta");
        string session = Path.Combine(root, "frequent-count-session.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 2, Language = "en", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [away + "\\", alpha.ToUpperInvariant() + "\\"] }, new PaneState { Tabs = [beta] }]
        });
        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var form = new MainForm(session, []))
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    var pane = form.GetPane(0);
                    var other = form.GetPane(1);
                    await FreqReady(pane);
                    pane.SelectTab(1);
                    await FreqReady(pane);
                    pane.SelectTab(0);
                    await FreqReady(other);
                    FreqCounts(form, expected, "three startup tabs, including background case/trailing-slash variants, do not count as visits");
                    form.SaveWorkspace("Visit-free restore");
                    async Task Visit(string path, string message)
                    {
                        await Navigate(pane.ActiveView!, () => pane.Navigate(path));
                        FreqIncrement(expected, pane.CurrentPath);
                        FreqCounts(form, expected, message);
                    }
                    await Visit(child + Path.DirectorySeparatorChar, "address navigation counts exactly one normalized folder visit");
                    pane.FocusFiles();
                    var crumb = pane.Breadcrumb.Items.OfType<ToolStripButton>().Single(item => item.Text == "count-parent");
                    await Navigate(pane.ActiveView!, crumb.PerformClick);
                    FreqIncrement(expected, parent);
                    FreqCounts(form, expected, "breadcrumb navigation counts exactly once");
                    await Navigate(pane.ActiveView!, () => pane.ActiveView!.Travel(0x4000));
                    FreqIncrement(expected, child);
                    FreqCounts(form, expected, "back travel records the destination once");
                    await Navigate(pane.ActiveView!, () => pane.ActiveView!.Travel(0x8000));
                    FreqIncrement(expected, parent);
                    FreqCounts(form, expected, "forward travel records the destination once");
                    await ItemCountKnown(pane.ActiveView!);
                    Check(pane.ActiveView!.SelectPaths([child]) == 1, "native folder fixture can be selected");
                    pane.ActiveView.FocusView();
                    nint list = 0;
                    EnumChildWindows(pane.ActiveView.Handle, (window, _) =>
                    {
                        var name = new System.Text.StringBuilder(64);
                        GetClassName(window, name, name.Capacity);
                        if (name.ToString() == "SysListView32") list = window;
                        return true;
                    }, 0);
                    Check(list != 0, "native folder list exists for Enter navigation");
                    await Navigate(pane.ActiveView, () => PostMessage(list, 0x100, (nint)Keys.Enter, 0));
                    Check(string.Equals(pane.CurrentPath, child, StringComparison.OrdinalIgnoreCase), "Enter on a native folder selection navigates inside the pane");
                    FreqIncrement(expected, child);
                    FreqCounts(form, expected, "native folder opening counts exactly one visit");
                    pane.ActiveView!.RefreshFolder();
                    await FreqIdle();
                    FreqCounts(form, expected, "F5 refresh does not count the same location again");
                    await Navigate(pane.ActiveView, () => pane.Navigate(child));
                    FreqCounts(form, expected, "a completed second navigation to the same folder does not add a visit");
                    await Navigate(pane.ActiveView, () => pane.Navigate(child.ToUpperInvariant() + "\\"));
                    FreqCounts(form, expected, "completed same-folder navigation with different casing and a trailing slash does not add a visit");
                    await PaneChange(pane, () => pane.AddTab(beta), () => pane.ActiveView?.HasNavigated == true && pane.CurrentPath == beta);
                    FreqIncrement(expected, beta);
                    FreqCounts(form, expected, "a user-created folder tab counts exactly once");
                    pane.CloseTab();
                    await PaneChange(pane, pane.ReopenTab, () => pane.ActiveView?.HasNavigated == true && pane.CurrentPath == beta);
                    FreqIncrement(expected, beta);
                    FreqCounts(form, expected, "reopening a closed tab counts exactly once");
                    await PaneChange(other, () => Check(form.DockTab(pane, pane.ActiveTab!, 1, null), "tab can move to the other pane"),
                        () => other.ActiveView?.HasNavigated == true && other.CurrentPath == beta);
                    FreqCounts(form, expected, "moving a tab between panes does not count a visit");
                    form.LoadWorkspace("Visit-free restore");
                    await FreqReady(pane);
                    pane.SelectTab(1);
                    await FreqReady(pane);
                    pane.SelectTab(0);
                    await FreqReady(other);
                    FreqCounts(form, expected, "workspace restoration does not count any restored folder");
                    await Navigate(pane.ActiveView!, () => pane.Navigate(Path.GetPathRoot(fixture)!));
                    FreqCounts(form, expected, "a drive root is never recorded");
                    await Navigate(pane.ActiveView!, () => pane.Navigate("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
                    FreqCounts(form, expected, "This PC is never recorded as a filesystem folder");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        Check(FreqEqual(SessionStore.Load(session).FrequentFolders, expected), "exit persists the exact visit counts");
        FreqInspectJson(session, query);
        using (var restored = new MainForm(session, []))
        {
            restored.Shown += async (_, _) =>
            {
                try
                {
                    await FreqReady(restored.GetPane(0));
                    await FreqReady(restored.GetPane(1));
                    FreqCounts(restored, expected, "a second MainForm loads saved counts without recounting startup tabs");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { restored.ExitApplication(); }
            };
            Application.Run(restored);
        }
        FreqLoadBoundaries(root, away, alpha);
        FreqFailedRestore(root, away);

        string rankingSession = Path.Combine(root, "frequent-ranking-session.json");
        SessionStore.Save(rankingSession, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [away] }]
        });
        using var backend = new IndependentSearchBackend([fixture], Path.Combine(root, "frequent-index.fsix"));
        using var ranking = new MainForm(rankingSession, [], backend);
        ranking.Shown += async (_, _) =>
        {
            try
            {
                var pane = ranking.GetPane(0);
                await FreqReady(pane);
                await backend.BuildOrLoadAsync(CancellationToken.None);
                var address = Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName?.EndsWith(" address") == true);
                int searches = 0;
                async Task Ranked(string first, string message)
                {
                    await Search(pane, address, query + new string(' ', ++searches));
                    Check(pane.CandidatesVisible && pane.SearchCandidates.Count == 2
                        && pane.SearchCandidates[0].FullPath == first
                        && pane.SearchCandidates.Select(hit => hit.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([alphaFile, betaFile]), message);
                    pane.FocusFiles();
                }
                async Task Visits(string path, int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        await Navigate(pane.ActiveView!, () => pane.Navigate(path));
                        await Navigate(pane.ActiveView!, () => pane.Navigate(away));
                    }
                }
                await Ranked(alphaFile, "without visits equal-name address hits use path order");
                await Visits(beta, 2);
                Check(ranking.FolderVisitEntries.Single(entry => entry.Path == beta).Count == 2, "ranking fixture has exactly two beta visits");
                await Ranked(alphaFile, "two visits do not cross the frequent-folder threshold");
                await Visits(beta, 1);
                await Ranked(betaFile, "the third visit promotes beta above the alphabetically earlier alpha hit");
                await Visits(alpha, 4);
                await Ranked(alphaFile, "four visits outrank a different folder with three visits");
                await Visits(beta, 5);
                await Ranked(betaFile, "the most-visited folder wins even when its path sorts last");
                await Navigate(pane.ActiveView!, () => pane.Navigate(alpha));
                await Ranked(alphaFile, "the current folder outranks a more frequently visited folder");
                await Navigate(pane.ActiveView!, () => pane.Navigate(away));
                await PaneChange(pane, () => pane.AddTab(alpha), () => pane.ActiveView?.HasNavigated == true && pane.CurrentPath == alpha);
                pane.SelectTab(0);
                await Ranked(alphaFile, "a background open tab outranks a more frequently visited folder");
                pane.CloseTab(1, remember: false);
                ranking.RestoreFavorite(alpha);
                await Ranked(alphaFile, "a favorite outranks a more frequently visited folder");
                ranking.ClearFavorites();
                await Ranked(betaFile, "removing higher-priority roots exposes frequent-folder ranking again");
                var settings = Descendants(ranking).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>())
                    .Single(item => item.AccessibleName == "Settings");
                foreach (var (language, text) in new[]
                {
                    ("ko", "자주 연 폴더 기록 지우기"), ("en", "Forget frequently opened folders"),
                    ("zh-CN", "清除常用文件夹记录"), ("ja", "よく開くフォルダーの履歴を消去")
                })
                {
                    ranking.SetLanguage(language);
                    settings.ShowDropDown();
                    Check(settings.DropDownItems.Cast<ToolStripItem>().Count(item => item.Text == text && item.Enabled) == 1,
                        language + " Settings exposes one translated forget-frequent-folders command");
                    settings.HideDropDown();
                }
                ranking.SetLanguage("en");
                foreach (string theme in new[] { "light", "dark" })
                {
                    ranking.SetColorTheme(theme);
                    settings.ShowDropDown();
                    await FreqIdle();
                    var item = settings.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == "Forget frequently opened folders");
                    Check(item.Available && item.Bounds.Height > 0 && settings.DropDown.ClientRectangle.Contains(item.Bounds),
                        theme + " Settings command fits inside the visible dropdown");
                    FreqCapture(settings.DropDown, "frequent-settings-" + theme);
                    settings.HideDropDown();
                }
                settings.ShowDropDown();
                settings.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == "Forget frequently opened folders").PerformClick();
                settings.HideDropDown();
                Check(ranking.FolderVisitEntries.Count == 0, "Settings forget command clears all folder counts");
                var status = Descendants(pane).OfType<Label>().Single(label => label.AccessibleName == "Pane 1 status");
                Check(status.Text.Split("  |  ", StringSplitOptions.None).Contains("Frequently opened folders were cleared."),
                    "forget command reports completion on the active pane");
                await Ranked(alphaFile, "forgetting frequent folders restores address-search path ordering");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { ranking.ExitApplication(); }
        };
        Application.Run(ranking);
        Check(SessionStore.Load(rankingSession).FrequentFolders.Count == 0, "cleared visits remain empty after the next save");
        FreqInspectJson(rankingSession, query);
    }

    private static async Task FreqReady(ExplorerPane pane)
    {
        if (pane.ActiveView is { HasNavigated: false } view)
            await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static async Task FreqIdle()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Idle(object? sender, EventArgs args) => completion.TrySetResult();
        Application.Idle += Idle;
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { Application.Idle -= Idle; }
    }

    private static void FreqIncrement(Dictionary<string, int> counts, string path)
    {
        string key = path.TrimEnd('\\', '/');
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static bool FreqEqual(IReadOnlyList<FolderVisit> actual, Dictionary<string, int> expected) =>
        actual.Count == expected.Count && actual.All(entry => expected.TryGetValue(entry.Path, out int count) && count == entry.Count)
        && actual.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Count;

    private static void FreqCounts(MainForm form, Dictionary<string, int> expected, string message)
    {
        bool equal = FreqEqual(form.FolderVisitEntries, expected);
        Check(equal, message);
        if (!equal) Console.Error.WriteLine("VISIT_EVIDENCE expected=" + JsonSerializer.Serialize(expected) + " actual=" + JsonSerializer.Serialize(form.FolderVisitEntries));
    }

    private static void FreqInspectJson(string session, string query)
    {
        string json = File.ReadAllText(session);
        using var document = JsonDocument.Parse(json);
        var visits = document.RootElement.GetProperty("FrequentFolders");
        Check(visits.ValueKind == JsonValueKind.Array && visits.EnumerateArray().All(entry =>
            entry.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(new[] { "Count", "Path" })
            && entry.GetProperty("Count").GetInt32() > 0 && Directory.Exists(entry.GetProperty("Path").GetString())),
            "FrequentFolders JSON stores only folder paths and positive counts");
        Check(!json.Contains(query, StringComparison.Ordinal) && !json.Contains(".txt", StringComparison.OrdinalIgnoreCase),
            "session JSON does not persist search text or matched file names");
    }

    private static void FreqFailedRestore(string root, string destination)
    {
        string session = Path.Combine(root, "frequent-failed-restore-session.json");
        string missing = Path.Combine(root, "unavailable-restored-folder");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [missing] }]
        });
        using var form = new MainForm(session, []);
        var pane = form.GetPane(0);
        var host = pane.ActiveView!;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Failed(object? sender, string message) => failed.TrySetResult();
        host.NavigationError += Failed;
        form.Shown += async (_, _) =>
        {
            try
            {
                if (host.ErrorStatus is null) await failed.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Check(host.ErrorStatus is not null && !host.HasNavigated,
                    "an unavailable saved folder reports a real startup navigation failure");
                Check(form.FolderVisitEntries.Count == 0, "a missing restored folder is never recorded");
                await Navigate(host, () => pane.Navigate(destination));
                var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [destination] = 1 };
                FreqCounts(form, expected, "the first real navigation after failed restoration counts exactly once");
                await Navigate(host, () => pane.Navigate(destination.ToUpperInvariant() + "\\"));
                FreqCounts(form, expected, "a duplicate canonical destination after recovery is not counted again");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { host.NavigationError -= Failed; form.ExitApplication(); }
        };
        Application.Run(form);
        Check(FreqEqual(SessionStore.Load(session).FrequentFolders,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [destination] = 1 }),
            "recovered restored tab persists only its one successful user visit");
    }

    private static void FreqLoadBoundaries(string root, string away, string alpha)
    {
        string legacy = Path.Combine(root, "frequent-legacy-session.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new { Language = "en", ExplorerFavoritesImported = true,
            Panes = new[] { new { Tabs = new[] { away } } } }));
        Check(SessionStore.Load(legacy).FrequentFolders.Count == 0, "a legacy session without FrequentFolders loads an empty history");
        using (var form = new MainForm(legacy, []))
        {
            form.Shown += async (_, _) =>
            {
                try { await FreqReady(form.GetPane(0)); Check(form.FolderVisitEntries.Count == 0, "MainForm keeps legacy startup history empty"); }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        string invalid = Path.Combine(root, "frequent-invalid-session.json");
        var bad = new SessionState
        {
            FrequentFolders = [new() { Path = "relative", Count = 3 }, new() { Path = "", Count = 3 },
                new() { Path = away, Count = 0 }, new() { Path = away, Count = -2 },
                new() { Path = Path.GetPathRoot(away)!, Count = 9 }, new() { Path = alpha + "\\", Count = 3 },
                new() { Path = alpha.ToUpperInvariant(), Count = 5 }]
        };
        File.WriteAllText(invalid, JsonSerializer.Serialize(bad));
        var loaded = SessionStore.Load(invalid).FrequentFolders;
        Check(loaded.Count == 1 && string.Equals(loaded[0].Path, alpha, StringComparison.OrdinalIgnoreCase) && loaded[0].Count == 5,
            "loading drops invalid/root entries and merges case-insensitive duplicates without adding their counts");
        var normalized = SessionStore.Normalize(bad).FrequentFolders;
        Check(normalized.SequenceEqual(loaded), "in-memory session normalization matches file-load validation");
        var many = Enumerable.Range(0, 105).Select(index => new FolderVisit { Path = Path.Combine(root, "saved-folder-" + index), Count = 1 }).ToList();
        File.WriteAllText(invalid, JsonSerializer.Serialize(new SessionState { FrequentFolders = many }));
        Check(SessionStore.Load(invalid).FrequentFolders.Select(entry => entry.Path).SequenceEqual(many.Take(100).Select(entry => entry.Path)),
            "session loading truncates more than 100 saved folder entries");
    }

    private static void FreqCapture(Control control, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        bitmap.Save(Path.Combine(directory, name + ".png"));
    }
}
