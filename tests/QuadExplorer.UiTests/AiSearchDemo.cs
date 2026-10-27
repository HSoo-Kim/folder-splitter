using QuadExplorer;
using QuadExplorer.AiSearch;
using QuadExplorer.Search;

internal static partial class Program
{
    // Explicit visible demo: own fixture/session, no global hotkey, update or real-session writes.
    private static int RunAiSearchDemo(string output, string root)
    {
        Directory.CreateDirectory(output);
        string files = Directory.CreateDirectory(Path.Combine(root, "Files")).FullName;
        WritePdfFixture(Path.Combine(files, "제안서_최종본.pdf"));
        WritePdfFixture(Path.Combine(files, "제안서_초안.pdf"));
        File.WriteAllText(Path.Combine(files, "제안서_최종본.docx"), "synthetic fixture");
        File.WriteAllText(Path.Combine(files, "회의록.txt"), "synthetic fixture");
        string session = Path.Combine(root, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "ko", CheckUpdates = false, AutoUpdate = false, GlobalHotkey = false,
            KeepInTray = false, AutoRestartMemory = 0, ExplorerFavoritesImported = true,
            ShowSidebar = false, ShowFolderTree = false, WindowWidth = 1500, WindowHeight = 900,
            Panes = [new PaneState { Tabs = [files] }]
        });
        AiSearchSettingsStore.Save(AiSearchSettingsStore.PathFor(session), new AiSearchSettings
        {
            Provider = AiProviderKind.ClaudeCli, Model = "claude-haiku-5-5", Visible = true, Width = 460
        });
        var backend = new IndependentSearchBackend([files], Path.ChangeExtension(session, ".search.index"));
        backend.BuildOrLoadAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        using var form = new MainForm(session, [], backend, treeRoots: [files]);
        form.Text = "Folder Splitter - AI search QA (sample files)";
        SetWindowLongPtr(form.Handle, -20, GetWindowLongPtr(form.Handle, -20) | 0x08000000); // WS_EX_NOACTIVATE
        form.AiSearch.SettingsChanged += settings =>
            Console.WriteLine($"AI_SEARCH_DEMO_SETTINGS model={settings.Model} effort={settings.ReasoningEffort}");
        form.AiSearch.StateChanged += (_, _) =>
        {
            if (form.AiSearch.Busy) return;
            var reply = form.AiSearch.Turns.LastOrDefault();
            Console.WriteLine($"AI_SEARCH_DEMO_REPLY hits={reply?.Result?.Hits.Count ?? -1} clarification={reply?.Result?.Plan.Clarification is not null} error={reply?.Error is not null}");
        };
        form.Shown += (_, _) =>
        {
            Console.WriteLine("AI_SEARCH_DEMO_READY " + session);
            Console.WriteLine("FIXTURE_EXPECTED first=2 PDF; refine=1 final PDF");
        };
        Application.Run(form);
        Console.WriteLine("AI_SEARCH_DEMO_CLOSED");
        return 0;
    }
}
