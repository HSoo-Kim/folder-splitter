using QuadExplorer;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static void RunAiSearchIntegrationScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "AI-host")).FullName;
        string file = Path.Combine(folder, "proposal-final.pdf");
        WritePdfFixture(file);
        string alternate = Directory.CreateDirectory(Path.Combine(folder, "alternate")).FullName;
        string otherFile = Path.Combine(alternate, "proposal-final.pdf");
        WritePdfFixture(otherFile);
        string session = Path.Combine(folder, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ExplorerFavoritesImported = true, GlobalHotkey = false,
            KeepInTray = false, CheckUpdates = false, AutoRestartMemory = 0, ShowSidebar = false,
            WindowWidth = 1500, WindowHeight = 900, Panes = [new PaneState { Tabs = [folder] }]
        });
        Exception? failure = null;
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                if (pane.ActiveView is { HasNavigated: false } initial)
                    await NavigationSignal(initial).WaitAsync(TimeSpan.FromSeconds(20));
                var toolbar = form.Controls.OfType<ToolStrip>().Single();
                int Position(string name) => toolbar.Items.IndexOf(toolbar.Items.Cast<ToolStripItem>()
                    .Single(item => item.AccessibleName == name));
                Check(Position("Preview") < Position("AI search") && Position("AI search") < Position("1 pane layout"),
                    "AI search button sits between Preview and 1-pane");
                form.SetAiSearchVisible(true);
                var chat = form.AiSearch;
                var helpChildren = chat.HelpContent.Controls.Cast<Control>().Where(control => control.Visible).ToArray();
                Check(helpChildren.All(control => control.Top >= 0 && control.Bottom <= chat.HelpContent.Height)
                    && helpChildren.SelectMany((left, index) => helpChildren.Skip(index + 1)
                        .Select(right => !left.Bounds.IntersectsWith(right.Bounds))).All(fits => fits),
                    "first hosted display lays out help without overlaps from hidden-parent visibility");
                Check(chat.Visible && form.AiSearchVisible && chat.Parent == form.Preview.Parent,
                    "AI conversation opens in the main workspace");
                Check(chat.Width >= (int)Math.Round(320 * form.DeviceDpi / 96d),
                    "AI sidecar keeps its logical minimum width at the current DPI");
                form.SetPreviewVisible(true);
                Check(form.PreviewVisible && form.AiSearchVisible, "preview and AI coexist when the workspace is wide");
                var plan = new SearchPlan(["proposal"], [], [], [".pdf"], ItemKind.File,
                    SearchScope.Everywhere, null, null, null, null, false, null);
                chat.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, "two results",
                    new AiSearchResult(plan,
                    [
                        new(file, false, new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)),
                        new(otherFile, false, new FileInfo(otherFile).Length, File.GetLastWriteTimeUtc(otherFile))
                    ], 2, false, null)));
                chat.Composer.Text = "Find proposal PDF files";
                await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(chat.Turns.Count == 2 && !chat.Busy, "hosted chat renders a completed real-file result");
                var input = Message.Create(chat.Composer.Handle, 0x100, (nint)Keys.Enter, 0);
                Check(!form.PreFilterMessage(ref input), "chat Enter is not forwarded to the Shell file view");
                form.SetAiSearchFloating(true);
                Check(form.FloatingAiSearch is { Visible: true } && ReferenceEquals(form.AiSearch, chat),
                    "detach keeps the same chat control");
                Check(form.FloatingAiSearch!.MinimumSize.Width >= (int)Math.Round(360 * form.DeviceDpi / 96d),
                    "floating AI minimum width scales with the current DPI");
                form.SetAiSearchVisible(false);
                form.SetAiSearchVisible(true);
                Check(chat.Turns.Count == 2 && form.FloatingAiSearch!.Visible, "hide/reopen preserves floating conversation");
                form.SetAiSearchFloating(false);
                Check(form.FloatingAiSearch is null && chat.Turns.Count == 2, "redock preserves conversation");
                form.SetColorTheme("dark");
                form.SetColorTheme("light");
                Check(chat.HintText.Length > 0 && chat.Composer.Enabled, "theme changes preserve usable conversation");
                var list = Descendants(chat).OfType<ListView>().Single();
                async Task OpenResult(string path)
                {
                    var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Selection(object? sender, EventArgs e)
                    {
                        if (pane.ActiveView?.SelectionInfo().Item1 == path) selected.TrySetResult();
                    }
                    pane.SelectionChanged += Selection;
                    try
                    {
                        foreach (ListViewItem item in list.Items) item.Selected = false;
                        list.Items.Cast<ListViewItem>().Single(item => item.Tag is AiHit hit && hit.FullPath == path).Selected = true;
                        list.Focus();
                        PostMessage(list.Handle, 0x100, (nint)Keys.Enter, 0);
                        if (pane.ActiveView?.SelectionInfo().Item1 == path) selected.TrySetResult();
                        await selected.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    }
                    finally { pane.SelectionChanged -= Selection; }
                }
                await OpenResult(file);
                await OpenResult(otherFile);
                Check(pane.CurrentPath == alternate && pane.ActiveView?.SelectionInfo().Item1 == otherFile,
                    "selecting one of two identically named AI results opens the correct file in its actual folder");
                await OpenResult(file);
                Button Action(string name) => Descendants(chat).OfType<Button>().Single(button => button.AccessibleName == name);
                int tabs = pane.TabCount;
                await PaneChange(pane, () => Action("Open in new tab").PerformClick(),
                    () => pane.TabCount == tabs + 1 && pane.ActiveView is { HasNavigated: true });
                Check(pane.CurrentPath == folder, "AI new-tab action opens the real containing folder");
                var destination = form.GetPane(1);
                await PaneChange(destination, () => Action("Open in new pane").PerformClick(),
                    () => destination.Visible && destination.ActiveView is { HasNavigated: true });
                Check(destination.CurrentPath == folder, "AI new-pane action opens a real new split without replacing the original pane");
                var previewReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void PreviewChanged(object? sender, EventArgs e)
                {
                    if (form.Preview.CurrentPath == file && form.Preview.HasImage) previewReady.TrySetResult();
                }
                form.Preview.PreviewChanged += PreviewChanged;
                try
                {
                    Action("Preview").PerformClick();
                    PreviewChanged(null, EventArgs.Empty);
                    await previewReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    Check(form.PreviewVisible && form.Preview.Pdf?.PageCount == 3,
                        "AI preview action renders the actual PDF in the existing preview");
                }
                finally { form.Preview.PreviewChanged -= PreviewChanged; }
                chat.SelectModel(AiProviderKind.LocalEndpoint, "gpt-oss:20b");
                chat.SelectEffort("medium");
                var savedAi = AiSearchSettingsStore.Load(AiSearchSettingsStore.PathFor(session));
                Check(savedAi.Model == "gpt-oss:20b" && savedAi.ReasoningEffort == "medium",
                    "inline model and reasoning selections persist in the isolated session's AI settings");
                await CheckAiAssociatedFileOpen(chat, folder);
            }
            catch (Exception error) { failure = error; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("AI host integration", failure);
        string damaged = Path.Combine(folder, "damaged.json");
        SessionStore.Save(damaged, new SessionState { ExplorerFavoritesImported = true, GlobalHotkey = false, CheckUpdates = false });
        File.WriteAllText(AiSearchSettingsStore.PathFor(damaged), "{broken");
        using var recovery = new MainForm(damaged, []);
        Check(File.ReadAllText(AiSearchSettingsStore.PathFor(damaged)) == "{broken",
            "damaged optional AI settings do not crash or overwrite the user's configuration");
    }
}
