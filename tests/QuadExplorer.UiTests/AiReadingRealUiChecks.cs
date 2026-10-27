using System.IO.Compression;
using System.Reflection;
using System.Text;
using QuadExplorer;
using QuadExplorer.AiSearch;
using QuadExplorer.Search;

internal static partial class Program
{
    private static void RunAiReadingRealUiScenario(string root, bool useGpt = false)
    {
        string scope = Directory.CreateDirectory(Path.Combine(root, "Reading")).FullName;
        string first = Directory.CreateDirectory(Path.Combine(scope, "Archive-A")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(scope, "Archive-B")).FullName;
        string external = Directory.CreateDirectory(Path.Combine(root, "Additional candidate folder")).FullName;
        string evidence = Path.Combine(second, "record-02.docx");
        WriteReadingDocx(Path.Combine(first, "record-01.docx"), "This document describes cafeteria menus only.");
        WriteReadingDocx(evidence, "RAIL-42 study: platform waiting time decreased from 12 minutes to 7 minutes after dispatch changes.");
        string followup = Path.Combine(external, "record-03.docx");
        WriteReadingDocx(followup, "SOLAR-93 experiment: conversion efficiency was measured at 23.4 percent.");
        string session = Path.Combine(root, "reading-session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "ko", GlobalHotkey = false, KeepInTray = false, CheckUpdates = false,
            AutoUpdate = false, AutoRestartMemory = 0, ExplorerFavoritesImported = true,
            ShowSidebar = false, ShowFolderTree = false, WindowWidth = 1500, WindowHeight = 900,
            Panes = [new PaneState { Tabs = [scope] }]
        });
        AiSearchSettingsStore.Save(AiSearchSettingsStore.PathFor(session), new AiSearchSettings
        {
            Provider = useGpt ? AiProviderKind.CodexCli : AiProviderKind.ClaudeCli,
            Model = useGpt ? "gpt-6-luna" : "claude-haiku-5-5", Visible = true, Width = 640
        });
        var backend = new IndependentSearchBackend([scope], Path.ChangeExtension(session, ".search.index"));
        backend.BuildOrLoadAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        using var form = new MainForm(session, [], backend, treeRoots: [scope]);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var chat = form.AiSearch;
                Check(chat.CurrentSettings.Mode == AiSearchMode.FileNames, "reading GUI starts with filename-only mode");
                chat.ModePicker.SelectedIndex = 1;
                typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(chat.ModePicker, [EventArgs.Empty]);
                Check(chat.CurrentSettings.Mode == AiSearchMode.ReadDocuments, "user explicitly selects document reading");
                chat.Composer.Text = "열린 폴더와 그 하위 폴더의 문서들을 읽고, RAIL-42 연구의 대기시간 개선 결과를 근거와 함께 알려줘.";
                await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(200));
                var result = chat.Turns.Last().DocumentResult;
                if (result is null) throw new InvalidOperationException(chat.Turns.Last().Error ?? "No document result.");
                Check(result.Sources.Any(source => source.FullPath == evidence
                    && source.Quote.Contains("12") && source.Quote.Contains("7")),
                    "real model browses nested folders and cites text extracted from the actual DOCX");
                Check(result.Coverage.FoldersListed >= 2 && result.Coverage.FilesRead >= 1,
                    "real model reports observed multi-folder coverage");
                var sources = Descendants(chat).OfType<ListView>().Single(list => list.AccessibleName == "문서 근거");
                Check(sources.Items.Count >= 1, "grounded answer exposes actual source file actions");
                Capture("reading-answer-light");
                form.SetAiSearchFloating(true);
                form.SetAiSearchFloating(false);
                form.SetColorTheme("dark");
                Capture("reading-answer-dark");
                chat.ResetConversation();
                chat.Composer.Text = "열린 폴더와 하위 폴더의 문서를 확인하고 SOLAR-93 실험의 변환 효율을 알려줘. 찾을 수 없으면 후보 폴더 경로를 물어봐.";
                await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(200));
                var missing = chat.Turns.Last().DocumentResult;
                Console.WriteLine($"CANDIDATE_RESULT question={missing?.Question ?? "<none>"} " +
                    $"sources={missing?.Sources.Count ?? -1} answer={missing?.Answer[..Math.Min(missing.Answer.Length, 300)] ?? "<none>"} " +
                    $"error={chat.Turns.Last().Error ?? "<none>"}");
                Check(missing?.Question is { Length: > 0 } && missing.Sources.Count == 0,
                    "real model asks for a candidate path rather than inventing absent document evidence");
                Capture("reading-candidate-question");
                chat.Composer.Text = $"후보 폴더는 \"{external}\" 이야. 여기 문서를 읽고 앞 질문에 답해줘.";
                await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(200));
                var continued = chat.Turns.Last().DocumentResult;
                Check(continued?.Sources.Any(source => source.FullPath == followup && source.Quote.Contains("23.4")) == true,
                    "real model follows user-supplied folder path with spaces and reads new evidence");
                Capture("reading-candidate-answer");

                void Capture(string name)
                {
                    if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                        CaptureAiControl(form, Path.Combine(output, name + ".png"));
                }
            }
            catch (Exception error) { failure = error; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Real document-reading GUI", failure);
    }

    private static void WriteReadingDocx(string path, string content)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
        Entry("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        Entry("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """);
        Entry("word/document.xml", $"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>{System.Security.SecurityElement.Escape(content)}</w:t></w:r></w:p></w:body></w:document>
            """);
    }
}
