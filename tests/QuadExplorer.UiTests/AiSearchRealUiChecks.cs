using System.Reflection;
using QuadExplorer;
using QuadExplorer.AiSearch;
using QuadExplorer.Search;

internal static partial class Program
{
    // Explicit opt-in only: sends synthetic names to the installed, authenticated Claude CLI.
    private static void RunAiSearchRealUiScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "real-model-files")).FullName;
        WritePdfFixture(Path.Combine(folder, "제안서_최종본.pdf"));
        WritePdfFixture(Path.Combine(folder, "제안서_초안.pdf"));
        File.WriteAllText(Path.Combine(folder, "회의록.txt"), "synthetic fixture");
        string session = Path.Combine(root, "real-model-session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "ko", GlobalHotkey = false, KeepInTray = false,
            CheckUpdates = false, AutoUpdate = false, AutoRestartMemory = 0,
            ExplorerFavoritesImported = true, ShowSidebar = false, ShowFolderTree = false,
            WindowWidth = 1500, WindowHeight = 900, Panes = [new PaneState { Tabs = [folder] }]
        });
        AiSearchSettingsStore.Save(AiSearchSettingsStore.PathFor(session), new AiSearchSettings
        {
            Provider = AiProviderKind.ClaudeCli, Model = "claude-haiku-5-5", Visible = true, Width = 460
        });
        var backend = new IndependentSearchBackend([folder], Path.ChangeExtension(session, ".search.index"));
        backend.BuildOrLoadAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        using var form = new MainForm(session, [], backend, treeRoots: [folder]);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var chat = form.AiSearch;
                chat.ModelButton.PerformClick();
                await chat.PendingModelList.WaitAsync(TimeSpan.FromSeconds(60));
                Check(chat.DiscoveredModels.Any(model => model.Id == "claude-haiku-5-5" && model.Available),
                    "real GUI catalog contains the installed provider's resolved model");
                Check(chat.ModelMenu.Visible && AiModelCatalog.RequestedModels.All(requested =>
                    chat.DiscoveredModels.Any(actual => actual.Provider == requested.Provider
                        && actual.Id == requested.Id && actual.Available)),
                    "live anchored model menu offers all six verified subscription models");
                if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } modelOutput)
                    CaptureAiControl(chat.ModelMenu, Path.Combine(modelOutput, "live-model-menu.png"));
                chat.ModelMenu.Close();
                void Effort(int index)
                {
                    chat.SelectEffort(new[] { "default", "low", "medium", "high" }[index]);
                }
                void Capture(string name)
                {
                    if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                        CaptureAiControl(form, Path.Combine(output, name + ".png"));
                }
                Effort(1);
                chat.Composer.Text = "제안서 PDF 파일 찾아줘";
                await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Check(chat.Turns.Last().Result?.Hits.Count == 2, "real GUI low-effort search finds both actual PDFs");
                Capture("real-ui-first");
                chat.ModelMenu.Items.OfType<ToolStripMenuItem>().Single(item =>
                    item.Tag is AiModelDescriptor { Provider: AiProviderKind.CodexCli, Id: "gpt-6.1-sol" }).PerformClick();
                Check(chat.CurrentSettings.Provider == AiProviderKind.CodexCli
                    && chat.CurrentSettings.Model == "gpt-6.1-sol", "live menu selection switches to the exact GPT subscription model");
                Effort(3);
                chat.Composer.Text = "그중 최종본만";
                await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(60));
                var last = chat.Turns.Last().Result;
                Check(last?.Hits.Count == 1 && last.Hits[0].FullPath == Path.Combine(folder, "제안서_최종본.pdf"),
                    "real GUI high-effort follow-up preserves context and returns the actual final PDF");
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void PreviewChanged(object? sender, EventArgs e)
                {
                    if (form.Preview.HasImage) ready.TrySetResult();
                }
                form.Preview.PreviewChanged += PreviewChanged;
                try
                {
                    var list = Descendants(chat).OfType<ListView>().Last();
                    list.Items[0].Selected = true;
                    Descendants(chat).OfType<Button>().Last(button => button.AccessibleName == "Preview").PerformClick();
                    PreviewChanged(null, EventArgs.Empty);
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    Check(form.Preview.Pdf?.PageCount == 3, "real GUI result previews the actual three-page PDF");
                }
                finally { form.Preview.PreviewChanged -= PreviewChanged; }
                form.SetAiSearchFloating(true);
                Check(ReferenceEquals(form.AiSearch, chat) && chat.Turns.Count == 4, "real GUI detach preserves both turns");
                form.SetAiSearchFloating(false);
                form.SetColorTheme("dark");
                Capture("real-ui-refined-dark");
                Check(chat.Turns.Count == 4 && !chat.Busy, "real GUI redock and dark theme preserve conversation");
            }
            catch (Exception error) { failure = error; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Real-model AI GUI", failure);
    }
}
