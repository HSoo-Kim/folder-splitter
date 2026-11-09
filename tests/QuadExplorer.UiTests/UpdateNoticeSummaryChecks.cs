using QuadExplorer;

internal static partial class Program
{
    private static void RunUpdateNoticeSummaryScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "UpdateSummary")).FullName;
        string session = Path.Combine(folder, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, ExplorerFavoritesImported = true, GlobalHotkey = false,
            StartWithWindows = false, KeepInTray = false, CheckUpdates = false, AutoUpdate = true,
            Panes = [new PaneState { Tabs = [folder] }]
        });
        var summaries = new Dictionary<string, IReadOnlyList<string>>
        {
            ["en"] = ["First improvement", "A longer improvement that wraps cleanly across the available width of the notification.", "Third improvement"],
            ["ko"] = ["여러 창·영역에서 고른 파일과 폴더를 함께 끌기", "다음 업데이트부터 알림에 주요 변경 내용 표시"],
            ["zh-CN"] = ["第一项改进", "第二项改进"],
            ["ja"] = ["一つ目の改善", "二つ目の改善"]
        };
        var release = new ReleaseInfo(new Version(99, 0, 0), "v99.0.0", "https://github.com/example/releases/tag/v99.0.0",
            new Uri("https://github.com/example/releases/download/v99.0.0/folder-splitter-win-x64.zip"), new string('0', 64), summaries);
        using var context = new FolderSplitterApplicationContext(session, [], latestRelease: _ => Task.FromResult<ReleaseInfo?>(release));
        context.UpdateExecutablePath = () => Path.Combine(folder, "FolderSplitter.exe");
        int pages = 0, downloads = 0;
        ReleaseInfo? opened = null;
        context.ReleasePageOpener = item => { pages++; opened = item; };
        context.UpdateDownload = (_, _) => { downloads++; throw new IOException("Test download stopped"); };
        RunMultiWindowContext(context, async () =>
        {
            var owner = context.Windows.Single();
            await MultiWindowReady(owner);
            owner.SetCheckUpdates(true);
            await owner.CheckForUpdateAsync();
            var notice = owner.OwnedForms.OfType<UpdateNotice>().Single();
            Check(notice.Visible, "a release with notes creates a visible notification");
            foreach (string theme in new[] { "light", "dark", "custom" })
            {
                if (theme == "custom") owner.SetCustomColors(Color.Teal, Color.FromArgb(247, 243, 232));
                else owner.SetColorTheme(theme);
                foreach (string language in new[] { "en", "ko", "zh-CN", "ja" })
                {
                    owner.SetLanguage(language);
                    string content = string.Join("\n", Descendants(notice).OfType<Label>().Where(label => label.Visible).Select(label => label.Text));
                    Check(summaries[language].All(content.Contains), $"{theme} notification displays authored {language} bullets");
                    Check(Descendants(notice).OfType<Button>().Any(button => button.Visible && button.Text == new UiText(language).T("View details")),
                        $"{theme} notification localizes its {language} details action");
                    CheckSummaryGeometry(notice, theme + " " + language);
                    if (language == "ko" && (theme == "light" || theme == "dark")
                        && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                    {
                        Directory.CreateDirectory(output);
                        using var image = new Bitmap(notice.Width, notice.Height);
                        notice.DrawToBitmap(image, new Rectangle(Point.Empty, notice.Size));
                        image.Save(Path.Combine(output, $"update-summary-{theme}.png"));
                    }
                }
            }
            owner.SetLanguage("en");
            owner.SetUiFont(null, "large");
            CheckSummaryGeometry(notice, "large font");
            owner.SetUiFont(null, "normal");
            Descendants(notice).OfType<Button>().Single(button => button.Text == "View details").PerformClick();
            Check(pages == 1 && downloads == 0 && ReferenceEquals(opened, context.AvailableUpdate) && notice.Visible,
                "details opens the current release page without starting installation or dismissing the notice");
            notice.RefreshNotice(release with { Summaries = new Dictionary<string, IReadOnlyList<string>> { ["en"] = ["Replacement note"] } }, true);
            Check(Descendants(notice).OfType<Label>().Any(label => label.Text.Contains("Replacement note")) &&
                !Descendants(notice).OfType<Label>().Any(label => label.Text.Contains("First improvement")),
                "refreshing a visible notice replaces its release summary");
            notice.RefreshNotice(release with { Summaries = new Dictionary<string, IReadOnlyList<string>> { ["ko"] = ["한국어 전용"] } }, true);
            Check(!Descendants(notice).OfType<Label>().Any(label => label.Text.Contains("한국어 전용")) &&
                !Descendants(notice).OfType<Button>().Any(button => button.Text == "View details"),
                "a missing locale restores the compact notice without borrowing another language");
            owner.SetAutoUpdate(false);
            Check(Descendants(notice).OfType<Label>().Any(label => label.Text.Contains("First improvement")) &&
                Descendants(notice).OfType<Button>().Any(button => button.Text == "Download") &&
                !Descendants(notice).OfType<Button>().Any(button => button.Text == "View details"),
                "download-only notices show the summary without a duplicate release-page action");
            Descendants(notice).OfType<Button>().Single(button => button.Text == "Download").PerformClick();
            Check(pages == 2 && downloads == 0, "the download action opens the release page without starting an install");
            owner.SetAutoUpdate(true);
            notice.RefreshNotice(release, true, false);
            Check(!Descendants(notice).OfType<Button>().Any(button => button.Text == "View details"),
                "no duplicate details action appears when the update executable is unavailable");
            notice.RefreshNotice(release, true);
            Descendants(notice).OfType<Button>().Single(button => button.Text == "Update").PerformClick();
            await WaitUpdateUi(owner, () => downloads == 1 && pages == 3);
            Check(downloads == 1 && pages == 3, "the primary update action starts the installer rather than merely opening details");

            int dpi = owner.DeviceDpi;
            int Scale(int value) => (value * dpi + 48) / 96;
            using var tinyOwner = new Form { StartPosition = FormStartPosition.Manual, ClientSize = new Size(Scale(470), Scale(155)), Location = owner.Location };
            tinyOwner.Show();
            using var tinyNotice = new UpdateNotice(new UiText("en"), tinyOwner.Font, release, true);
            tinyNotice.Present(tinyOwner);
            Check(tinyNotice.Visible && !Descendants(tinyNotice).OfType<Label>().Any(label => label.Text.Contains("First improvement"))
                && Descendants(tinyNotice).OfType<Button>().Any(button => button.Visible && button.Text == "Update")
                && Descendants(tinyNotice).OfType<Button>().Any(button => button.Visible && button.Text == "Close"),
                "a small owner retains the actionable compact notification when the expanded note does not fit");
            tinyOwner.ClientSize = new Size(Scale(800), Scale(600));
            Check(tinyNotice.Visible && Descendants(tinyNotice).OfType<Label>().Any(label => label.Text.Contains("First improvement")),
                "the summary returns when the owner grows");
            tinyOwner.Close();
        });
    }

    private static void CheckSummaryGeometry(UpdateNotice notice, string description)
    {
        var labels = Descendants(notice).OfType<Label>().Where(label => label.Visible).ToArray();
        var buttons = Descendants(notice).OfType<Button>().Where(button => button.Visible).ToArray();
        var client = notice.RectangleToScreen(notice.ClientRectangle);
        var bounds = labels.Cast<Control>().Concat(buttons).Select(control => control.RectangleToScreen(control.ClientRectangle)).ToArray();
        bool contained = bounds.All(client.Contains);
        bool separate = !bounds.Where((_, i) => bounds.Skip(i + 1).Any(other => bounds[i].IntersectsWith(other))).Any();
        bool textFits = labels.All(label => TextRenderer.MeasureText(label.Text, label.Font,
            new Size(label.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl).Height <= label.ClientSize.Height)
            && buttons.All(button => TextRenderer.MeasureText(button.Text, button.Font).Width <= button.ClientSize.Width);
        Check(contained && separate && textFits, description + " summary labels and actions fit without clipping or overlap");
    }
}
