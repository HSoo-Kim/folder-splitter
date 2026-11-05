using QuadExplorer;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

internal static partial class Program
{
    private static void RunUpdateProgressScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "UpdateProgress")).FullName;
        string session = Path.Combine(folder, "session.json");
        string target = Path.Combine(folder, "FolderSplitter.exe");
        string captures = Environment.GetEnvironmentVariable("FOLDERSPLITTER_UPDATE_PROGRESS_CAPTURE")
            ?? Path.Combine(Path.GetTempPath(), "FolderSplitter.UpdateProgressCaptures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(captures);
        byte[] application = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "FolderSplitter.exe"));
        byte[] package;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            using (var entry = zip.CreateEntry("FolderSplitter.exe", CompressionLevel.NoCompression).Open())
                entry.Write(application);
            package = memory.ToArray();
        }
        var release = new ReleaseInfo(new Version(99, 0, 0), "v99.0.0", "https://github.com/x",
            new Uri("https://github.com/x/folder-splitter-win-x64.zip"), Convert.ToHexString(SHA256.HashData(package)));
        SessionStore.Save(session, new SessionState
        {
            Language = "en", ColorTheme = "light", Layout = 1, ExplorerFavoritesImported = true,
            GlobalHotkey = false, StartWithWindows = false, KeepInTray = false, CheckUpdates = false,
            AutoUpdate = true, AutoRestartMemory = 0, ShowFolderTree = false,
            Panes = [new PaneState { Tabs = [folder] }]
        });
        using var context = new FolderSplitterApplicationContext(session, [], latestRelease: _ => Task.FromResult<ReleaseInfo?>(release));
        context.UpdateExecutablePath = () => target;
        int downloads = 0, releasePages = 0, launches = 0;
        context.ReleasePageOpener = _ => releasePages++;
        context.RestartLauncher = arguments =>
        {
            launches++;
            Check(context.Windows.All(window => window.VersionText.Contains("Restarting…", StringComparison.Ordinal)),
                "every browser shows restarting before the replacement launcher is called");
            Check(arguments.SequenceEqual(new[] { "--wait-pid", Environment.ProcessId.ToString(), "--session", session }),
                "the update restart retains its original wait-pid and scratch session arguments");
            return false;
        };
        RunMultiWindowContext(context, async () =>
        {
            var first = context.Windows.Single();
            await MultiWindowReady(first);
            var second = context.CreateWindowFromActive(first);
            await MultiWindowReady(second);
            first.SetCheckUpdates(true);
            await first.CheckForUpdateAsync();
            Check(context.AvailableUpdate is not null, "a fake latest release exposes the real update command without network access");
            var windows = new[] { first, second };
            var labels = windows.Select(window => Descendants(window).OfType<LinkLabel>()
                .Single(label => label.AccessibleName == "Application version")).ToArray();
            var bars = windows.Select(window => Descendants(window).OfType<UpdateProgressBar>().Single()).ToArray();
            foreach (var bar in bars) bar.AnimationPreference = () => true;
            var stages = new HashSet<UpdateStage>();
            var percentages = new List<int>[] { [], [] };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                labels[i].TextChanged += (_, _) =>
                {
                    if (context.CurrentUpdateProgress is not { } value || !context.Updating) return;
                    stages.Add(value.Stage);
                    if (value.Stage == UpdateStage.Downloading && value.Percentage is { } percent)
                        percentages[index].Add(percent);
                };
            }
            // The packaged apphost is this test build. Only its expected version is aligned after the
            // fake newer release has passed the real availability gate; all installer validation still runs.
            void AlignPackageVersion(bool badHash = false) => typeof(FolderSplitterApplicationContext)
                .GetProperty("AvailableUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context,
                    release with { Version = UpdateChecker.Current, PackageSha256 = badHash ? new string('0', 64) : release.PackageSha256 });
            async Task RunDownload(bool badHash, bool knownLength)
            {
                stages.Clear();
                foreach (var values in percentages) values.Clear();
                File.WriteAllText(target, "scratch original");
                AlignPackageVersion(badHash);
                using var source = new SteppedUpdateStream(package, knownLength);
                context.UpdateDownload = (_, _) => { downloads++; return Task.FromResult<Stream>(source); };
                Task run = context.OpenAvailableUpdateAsync(first);
                try
                {
                    await WaitUpdateUi(first, () => bars.All(bar => bar.Visible));
                    Check(windows.All(window => !window.UpdateLinkShown) && labels.All(label => label.Links.Cast<LinkLabel.Link>().All(link => !link.Enabled)),
                        "the real update link is disabled in both windows while the installer runs");
                    int requests = downloads;
                    await context.OpenAvailableUpdateAsync(second);
                    Check(downloads == requests, "a second browser cannot start a duplicate update");
                    for (int step = 1; step <= source.Steps; step++)
                    {
                        source.ReleaseStep();
                        int expected = (int)((long)Math.Min(step * source.StepSize, package.Length) * 100 / package.Length);
                        await WaitUpdateUi(first, () => knownLength
                            ? context.CurrentUpdateProgress is { Stage: UpdateStage.Downloading } progress
                                && progress.BytesReceived >= Math.Min(step * source.StepSize, package.Length)
                            : source.Position >= Math.Min(step * source.StepSize, package.Length));
                        if (knownLength)
                            Check(bars.All(bar => bar.Percentage == expected) && windows.All(window => window.VersionText.Contains(expected + "%", StringComparison.Ordinal)),
                                $"step {step} shows real byte percentage {expected}% in both browsers");
                        else Check(bars.All(bar => bar.Percentage is null) && windows.All(window => !window.VersionText.Contains('%')),
                            $"unknown Content-Length remains indeterminate at step {step}");
                        if (!badHash && knownLength && step == source.Steps / 2)
                        {
                            Check(bars.All(bar => bar.AnimationRunning), "the visible progress strips run their animation timers");
                            foreach (var mode in new[] { "light", "dark", "custom" })
                            {
                                if (mode == "custom") first.SetCustomColors(Color.Teal, Color.FromArgb(247, 243, 232));
                                else first.SetColorTheme(mode);
                                await NativeAppearanceDispatch(first);
                                foreach (var language in new[] { "ko", "en", "zh-CN", "ja" })
                                {
                                    first.SetLanguage(language);
                                    Check(windows.All(window => window.VersionText.Contains(new UiText(language).T("Downloading update… {0}%", expected), StringComparison.Ordinal)),
                                        mode + " progress is localized in " + language + " in both windows");
                                }
                                first.SetLanguage("en");
                                CheckUpdateProgressGeometry(first, labels[0], bars[0], mode);
                                var status = labels[0].Parent!;
                                using var image = new Bitmap(status.Width, status.Height);
                                status.DrawToBitmap(image, status.ClientRectangle);
                                string path = Path.Combine(captures, "update-progress-" + mode + ".png");
                                image.Save(path);
                                Console.WriteLine("SCREENSHOT " + path);
                            }
                            var bar = bars[0];
                            bar.AdvanceAnimation(TimeSpan.FromMilliseconds(300));
                            using var before = new Bitmap(bar.Width, bar.Height);
                            bar.DrawToBitmap(before, bar.ClientRectangle);
                            double position = bar.BandPosition;
                            bar.AdvanceAnimation(TimeSpan.FromMilliseconds(650));
                            using var after = new Bitmap(bar.Width, bar.Height);
                            bar.DrawToBitmap(after, bar.ClientRectangle);
                            Check(bar.BandPosition > position && !UpdatePixelsEqual(before, after),
                                "driving animation frames moves the soft wave while download bytes are paused");
                            bar.AnimationPreference = () => false;
                            bar.RefreshAnimation();
                            bar.AdvanceAnimation(TimeSpan.FromMilliseconds(1000));
                            Check(!bar.AnimationRunning && bar.BandPosition == 0 && bar.Percentage == expected,
                                "injected Windows animation-effects-off produces a static strip without losing percentage");
                            bar.AnimationPreference = () => true;
                            bar.RefreshAnimation();
                            second.Hide();
                            Check(!bars[1].AnimationRunning, "a hidden peer stops and disposes its animation timer");
                            second.Show();
                            Check(bars[1].AnimationRunning, "the peer resumes animation when shown during the download");
                        }
                    }
                    source.Finish();
                    await run.WaitAsync(TimeSpan.FromSeconds(30));
                }
                finally
                {
                    if (!run.IsCompleted)
                    {
                        source.Finish();
                        source.ReleaseRemaining();
                        await run.WaitAsync(TimeSpan.FromSeconds(30));
                    }
                }
                await NativeAppearanceDispatch(first);
                Check(bars.All(bar => !bar.Visible && !bar.AnimationRunning), "completion or failure hides both bars and disposes animation timers");
                Check(stages.Contains(UpdateStage.Verifying), "the real SHA-256 stage is shown in the footer");
                if (badHash)
                {
                    Check(!stages.Contains(UpdateStage.Installing) && File.ReadAllText(target) == "scratch original",
                        "bad SHA-256 cannot enter installation or modify the scratch executable");
                    Check(PnStatusText(first.GetPane(0)).Contains("Automatic update failed:", StringComparison.Ordinal),
                        "bad SHA-256 shows the existing automatic update error message");
                    Check(windows.All(window => window.UpdateLinkShown && window.VersionText.StartsWith("v" + UpdateChecker.Current.ToString(3), StringComparison.Ordinal)),
                        "failure restores the version and actionable update link in both browsers");
                }
                else
                {
                    Check(stages.Contains(UpdateStage.Installing) && stages.Contains(UpdateStage.Restarting),
                        "the real file-swap and restarting stages are shown in the footer");
                    Check(File.ReadAllBytes(target).SequenceEqual(application) && File.ReadAllText(target + ".old") == "scratch original",
                        "a successful run installs only into its scratch target");
                    if (knownLength) Check(percentages.All(values => values.Count >= source.Steps
                        && values.Zip(values.Skip(1)).All(pair => pair.First <= pair.Second)),
                        "both browsers receive monotonic percentages throughout download");
                }
                Check(!File.Exists(target + ".download") && !File.Exists(target + ".new"), "progress runs retain installer temporary-file cleanup");
            }
            await RunDownload(false, true);
            await RunDownload(false, false);
            await RunDownload(true, true);
            Check(downloads == 3 && launches == 2 && releasePages == 1,
                "all three runs use local streams, intercepted restart and intercepted failure fallback only");
        });
    }

    private static async Task WaitUpdateUi(Control control, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.BeginInvoke(() => turn.TrySetResult());
            await turn.Task.WaitAsync(timeout.Token);
            await Task.Yield();
        }
    }

    private static void CheckUpdateProgressGeometry(MainForm window, LinkLabel version, UpdateProgressBar bar, string theme)
    {
        var labelBounds = version.RectangleToScreen(version.ClientRectangle);
        var barBounds = bar.RectangleToScreen(bar.ClientRectangle);
        var footer = version.Parent!.Parent!;
        var other = footer.Controls.Cast<Control>().Single(control => control != version.Parent);
        var otherBounds = other.RectangleToScreen(other.ClientRectangle);
        Check(window.DeviceDpi == 120 && bar.Width == (100 * window.DeviceDpi + 48) / 96
            && bar.Height == (12 * window.DeviceDpi + 48) / 96, theme + " progress geometry and strip metrics scale on the real 120 DPI QA desktop");
        Check(barBounds.Left >= labelBounds.Right && barBounds.Right <= otherBounds.Left
            && footer.RectangleToScreen(footer.ClientRectangle).Contains(barBounds),
            theme + " slim strip sits beside the bottom-left version without overlapping author or footer bounds");
        bar.AdvanceAnimation(TimeSpan.Zero);
        using var image = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(image, bar.ClientRectangle);
        int y = image.Height / 2;
        Check(image.GetPixel(1, y).ToArgb() == Theme.Accent.ToArgb()
            && image.GetPixel(image.Width - 1, y).ToArgb() == Theme.Border.ToArgb()
            && Theme.Accent.ToArgb() != Theme.Border.ToArgb(), theme + " pixels use a distinct theme accent and muted track");
        Check(version.DisabledLinkColor.ToArgb() == Theme.Text.ToArgb(), theme + " disabled progress text uses readable theme foreground");
    }

    private static bool UpdatePixelsEqual(Bitmap first, Bitmap second)
    {
        for (int y = 0; y < first.Height; y++)
            for (int x = 0; x < first.Width; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
        return true;
    }

    private sealed class SteppedUpdateStream(byte[] bytes, bool knownLength) : MemoryStream(bytes, writable: false)
    {
        private readonly int length = bytes.Length;
        private readonly SemaphoreSlim steps = new(0);
        private readonly TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int StepSize => (length + 7) / 8;
        internal int Steps => (length + StepSize - 1) / StepSize;
        public override bool CanSeek => knownLength;
        internal void ReleaseStep() => steps.Release();
        internal void ReleaseRemaining() => steps.Release(Steps);
        internal void Finish() => finish.TrySetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= Length) { await finish.Task.WaitAsync(token); return 0; }
            await steps.WaitAsync(token);
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, StepSize)], token);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { finish.TrySetResult(); steps.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
