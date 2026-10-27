using QuadExplorer;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static void RunUnverifiedEvidenceScenario(string root)
    {
        string file = Path.Combine(root, "unverified-source.txt");
        const string quote = "The budget is 120 dollars.";
        File.WriteAllText(file, quote);
        foreach (string theme in new[] { "light", "dark" })
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            Theme.Set(theme);
            var text = new UiText(language);
            using var host = new Form { ClientSize = new Size(520, 640) };
            using var pane = new AiSearchPane(text, new AiSearchSettings { Mode = AiSearchMode.ReadDocuments }) { Dock = DockStyle.Fill };
            host.Controls.Add(pane);
            Exception? failure = null;
            host.Shown += async (_, _) =>
            {
                try
                {
                    var result = new DocumentSearchResult("", [new("s1", file, "characters 1-26", quote)],
                        new DocumentCoverage(0, 1, 1, 1, 0, false, []), DocumentSearch.UnverifiedEvidenceMessage);
                    pane.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, result.Question!, DocumentResult: result));
                    pane.Composer.Text = "Find the budget";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    var bodies = Descendants(pane.Transcript).OfType<RichTextBox>().ToArray();
                    var list = Descendants(pane.Transcript).OfType<ListView>().Single();
                    Check(bodies.Any(body => body.Text.Contains(quote, StringComparison.Ordinal))
                        && list.Items.Cast<ListViewItem>().Any(item => item.Tag is AiHit hit && hit.FullPath == file)
                        && bodies.All(body => !body.Text.Contains("250 dollars", StringComparison.Ordinal)),
                        $"unverified {theme}/{language} shows the original excerpt without the unsupported answer");
                    var labels = Descendants(pane.Transcript).OfType<Label>().ToArray();
                    Check(labels.Any(label => label.Text == text.T("Unverified answer · {0} files", 1))
                        && labels.Any(label => label.Text == text.T("Check the excerpts below.")),
                        $"unverified {theme}/{language} labels the excerpt separately from an answer");
                    if (language == "ko" && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                        CaptureAiControl(host, Path.Combine(output, $"unverified-{theme}.png"));
                    string deniedPath = Path.GetDirectoryName(file)!;
                    var outside = new DocumentSearchResult("", [], new DocumentCoverage(0, 0, 1, 0, 0, false, []),
                        DocumentSearch.OutsideContentScopeMessage + Environment.NewLine + deniedPath);
                    pane.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, outside.Question!, DocumentResult: outside));
                    pane.Composer.Text = "Search another folder";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(Descendants(pane.Transcript).OfType<RichTextBox>()
                        .Any(body => body.Text.StartsWith(text.T(DocumentSearch.OutsideContentScopeMessage), StringComparison.Ordinal)
                            && body.Text.Contains(deniedPath, StringComparison.OrdinalIgnoreCase)),
                        $"outside scope {theme}/{language} explains how to authorize the folder");
                    if (language == "ko" && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } scopeOutput)
                        CaptureAiControl(host, Path.Combine(scopeOutput, $"out-of-scope-{theme}.png"));
                }
                catch (Exception error) { failure = error; }
                finally { host.Close(); }
            };
            Application.Run(host);
            if (failure is not null) throw new InvalidOperationException($"Unverified evidence {theme}/{language}", failure);
        }
    }
}
