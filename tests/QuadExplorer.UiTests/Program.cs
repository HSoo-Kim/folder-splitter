using QuadExplorer;
using QuadExplorer.Search;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int failures;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        string? expectedDesktop = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_DESKTOP");
        var actualDesktop = new System.Text.StringBuilder(256);
        // Demo capture runs on the user's desktop only with their consent: Shell views paint only in a visible,
        // DWM-composed window. It never takes activation and captures the window's own content.
        bool demoCapture = args is ["--demo-capture", _, _];
        if (!demoCapture && (expectedDesktop is null || !expectedDesktop.StartsWith("FolderSplitter-QA-", StringComparison.Ordinal) ||
            !GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, actualDesktop, 512, out _) ||
            actualDesktop.ToString() != expectedDesktop))
        {
            Console.Error.WriteLine("Refusing visible GUI tests. Run through tools/PrivateDesktopQa or build.ps1 -UiTests.");
            return 2;
        }
        Console.WriteLine("PRIVATE_DESKTOP " + actualDesktop);
        // GDI+ cannot draw through a window DC on a desktop that is not the input desktop.
        // WinForms main ignores this in SplitContainer.RepaintSplitterRect (dotnet/winforms
        // SplitContainer.cs); apply the same catch until the shipped 10.0 runtime has it.
        new HarmonyLib.Harmony("FolderSplitter.UiTests").Patch(
            HarmonyLib.AccessTools.Method(typeof(SplitContainer), "RepaintSplitterRect"),
            finalizer: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(IgnoreSplitterRepaintFailure)));
        if (args is ["--demo-capture", var demoOutput, var demoRoot])
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            return RunDemoCapture(demoOutput, demoRoot);
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Path.GetTempPath(), "QuadExplorer.UiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folders = Enumerable.Range(1, 4).Select(i => Directory.CreateDirectory(Path.Combine(root, $"Pane-{i}")).FullName).ToArray();
            string child = Directory.CreateDirectory(Path.Combine(folders[0], "\ud55c\uae00 \ud3f4\ub354")).FullName;
            File.WriteAllText(Path.Combine(folders[0], "sample.txt"), "QuadExplorer integration fixture");
            string session = Path.Combine(root, "session.json");
            SessionStore.Save(session, new SessionState
            {
                Layout = 4,
                ExplorerFavoritesImported = true,
                Panes = folders.Select(path => new PaneState { Tabs = [path] }).ToList()
            });
            using var form = new MainForm(session, []);
            var panes = Descendants(form).OfType<ExplorerPane>().OrderBy(p => p.PaneNumber).ToArray();
            var initialNavigation = panes.Select(p => p.ActiveView!.HasNavigated
                ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.WhenAll(initialNavigation).WaitAsync(TimeSpan.FromSeconds(20));
                    Check(panes.Length == 4 && panes.All(p => p.Visible), "four native shell panes loaded");
                    Check(panes.Select(p => p.CurrentPath).SequenceEqual(folders, StringComparer.OrdinalIgnoreCase), "independent initial folders");
                    Check(panes.All(p => HasNativeDropTarget(p.ActiveView!)), "initial shell panes register native drop targets");

                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(child));
                    Check(panes[0].CurrentPath == child && panes[1].CurrentPath == folders[1], "Unicode folder navigation is isolated");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x4000));
                    Check(panes[0].CurrentPath == folders[0], "native back history");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x8000));
                    Check(panes[0].CurrentPath == child, "native forward history");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.Travel(0x2000));
                    Check(panes[0].CurrentPath == folders[0], "native parent navigation");

                    string selectedFile = Path.Combine(folders[0], "sample.txt");
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.NavigateAndSelect(selectedFile));
                    Check(SelectedPaths(panes[0].ActiveView!).SequenceEqual(new[] { selectedFile },
                        StringComparer.OrdinalIgnoreCase), "file candidate selects its actual native shell item");

                    panes[0].AddTab(folders[1], select: false);
                    var newView = Descendants(panes[0]).OfType<ShellViewHost>().Single(h => h.LocationPath == folders[1]);
                    await Navigate(newView, () => panes[0].SelectTab(1));
                    Check(panes[0].TabCount == 2 && panes[0].CurrentPath == folders[1], "new independent tab");
                    Check(HasNativeDropTarget(newView), "new shell tab registers a native drop target");
                    panes[0].CloseTab();
                    Check(panes[0].TabCount == 1 && panes[0].CurrentPath == folders[0], "close restores previous tab");
                    panes[0].ReopenTab();
                    Check(panes[0].TabCount == 2, "reopen closed tab");
                    panes[0].SelectTab(0);
                    panes[0].ActiveView!.SetViewMode(3);
                    panes[0].ActiveView!.SetViewMode(4);
                    panes[0].ActiveView!.RefreshFolder();
                    Check(true, "native view modes and refresh");

                    ClickLayout(form, 1);
                    Check(panes[0].Visible && panes.Skip(1).All(p => !p.Visible), "one-pane layout");
                    ClickLayout(form, 2);
                    Check(panes[0].Visible && panes[1].Visible && !panes[2].Visible && !panes[3].Visible, "two-pane layout");
                    ClickLayout(form, 4);
                    Check(panes.All(p => p.Visible) && panes[0].TabCount == 2, "four-pane restore preserves tabs");
                    form.Size = new Size(900, 600);
                    Check(panes.All(p => p.Width > 200 && p.Height > 100), "minimum-size pane layout");
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
            var restored = SessionStore.Load(session, out var warning);
            Check(warning is null && restored.Layout == 4 && restored.Panes[0].Tabs.Count == 2, "session saved on close");
            Check(restored.Panes.Skip(1).Select(p => p.Tabs[0]).SequenceEqual(folders.Skip(1)), "inactive pane paths persisted");
            RunDockingScenario(session, folders);
            RunEmptyPaneScenario(session, folders);
            RunLanguageScenario(session, folders);
            RunSearchScenario(root);
            RunAppearanceScenario(root);
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine(failures == 0 ? $"PASS {checks} native UI integration checks" : $"FAIL {failures} native UI checks ({checks} passed)");
        return failures == 0 ? 0 : 1;
    }

    private static void RunDockingScenario(string session, string[] folders)
    {
        using var form = new MainForm(session, []);
        var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
        var navigation = panes.Select(p => p.ActiveView!.HasNavigated
            ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
        PaneLayout? expectedTree = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.WhenAll(navigation).WaitAsync(TimeSpan.FromSeconds(20));
                ClickLayout(form, 3);
                Check(panes[0].Visible && panes[1].Visible && panes[2].Visible && !panes[3].Visible
                    && panes[1].Height > panes[0].Height * 1.5, "three panes: two left and one right");
                form.ApplyLayout(3, false);
                Check(panes[0].Height > panes[1].Height * 1.5 && !panes[3].Visible, "three panes: one left and two right");
                form.ApplyLayout(3, true, false);
                Check(panes[1].Width > panes[0].Width * 1.5, "three panes: horizontal arrangement");
                Check(panes.Skip(1).Select(p => p.CurrentPath).SequenceEqual(folders.Skip(1)),
                    "three-pane presets preserve hidden folder paths");

                ClickLayout(form, 1);
                var moving = panes[0].ActiveView!;
                await PaneChange(panes[1], () => Check(form.DockTab(panes[0], moving, 0, SplitEdge.Right),
                    "edge docking accepts a second pane"), () => panes[1].ActiveView!.HasNavigated && panes[1].CurrentPath == folders[0]);
                Check(form.CurrentLayout.GetPanes().Length == 2 && panes[0].TabCount == 1 && panes[1].TabCount == 2,
                    "edge docking moves the chosen tab without losing cached tabs");

                moving = panes[1].ActiveView!;
                await PaneChange(panes[2], () => Check(form.DockTab(panes[1], moving, 1, SplitEdge.Bottom),
                    "edge docking accepts a third pane"), () => panes[2].ActiveView!.HasNavigated && panes[2].CurrentPath == folders[0]);
                Check(form.CurrentLayout.First!.IsLeaf && !form.CurrentLayout.Second!.IsLeaf
                    && !form.CurrentLayout.Second.Vertical, "docking creates a one-plus-two layout");

                moving = panes[2].ActiveView!;
                await PaneChange(panes[3], () => Check(form.DockTab(panes[2], moving, 2, SplitEdge.Left),
                    "edge docking accepts a fourth pane"), () => panes[3].ActiveView!.HasNavigated && panes[3].CurrentPath == folders[0]);
                var before = form.CurrentLayout;
                int totalTabs = panes.Sum(p => p.TabCount);
                Check(!form.DockTab(panes[3], panes[3].ActiveView!, 3, SplitEdge.Bottom)
                    && form.CurrentLayout == before && panes.Sum(p => p.TabCount) == totalTabs,
                    "fifth-pane rejection keeps layout and tabs unchanged");

                moving = panes[2].ActiveView!;
                await PaneChange(panes[0], () => Check(form.DockTab(panes[2], moving, 0, null),
                    "center docking accepts a tab move"), () => panes[0].ActiveView!.HasNavigated && panes[0].CurrentPath == folders[2]);
                Check(form.CurrentLayout.GetPanes().Order().SequenceEqual(new[] { 0, 1, 3 })
                    && !panes[2].Visible, "moving the last tab collapses the empty source pane");
                panes[3].SelectTab(0);
                expectedTree = form.CurrentLayout;
                form.ToggleFavorites();
                Check(!form.FavoritesExpanded, "favorites collapse independently of the sidebar");
                form.ToggleFavorites();
                Check(form.FavoritesExpanded, "favorites expand again");
                form.ToggleFavorites();
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session, out var warning);
        Check(warning is null && restored.Layout == 3 && restored.ActivePane == 3
            && restored.LayoutTree == expectedTree, "docked layout and sparse active pane survive session save");
        Check(restored.Panes[0].Tabs.SequenceEqual(new[] { folders[1], folders[2] })
            && restored.Panes[3].Tabs.SequenceEqual(new[] { folders[3], folders[0] }),
            "moved and cached tabs survive session save");
        Check(panes.All(p => p.IsDisposed), "closing the form disposes hidden pane views");
        Check(!restored.FavoritesExpanded, "collapsed favorites survive session save");
    }

    // README/video stills: real app windows over a synthetic folder tree, captured on the private desktop.
    private static int RunDemoCapture(string output, string demoRoot)
    {
        if (Directory.Exists(demoRoot)) throw new IOException("Demo root already exists: " + demoRoot);
        Directory.CreateDirectory(output);
        try
        {
            string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([demoRoot, .. parts])).FullName;
            void Files(string folder, params string[] names)
            {
                foreach (string name in names) File.WriteAllText(Path.Combine(folder, name), "Folder Splitter demo file");
            }
            string projects = Folder("프로젝트"), launch = Folder("프로젝트", "2026 신제품 출시"), web = Folder("프로젝트", "웹사이트 개편");
            Folder("프로젝트", "고객 리서치"); Folder("프로젝트", "디자인 시안");
            Files(projects, "프로젝트 개요.txt", "연간 일정.csv");
            Folder("프로젝트", "2026 신제품 출시", "보도 사진"); Folder("프로젝트", "2026 신제품 출시", "참고 자료");
            Files(launch, "출시 계획.txt", "예산표.csv", "회의록 10월.txt", "보도자료 초안.txt");
            Files(web, "사이트맵.txt", "화면 설계.txt");
            string photos = Folder("사진");
            Folder("사진", "제주 여행"); Folder("사진", "가족"); Folder("사진", "2026 워크숍"); Folder("사진", "스크린샷");
            Files(photos, "앨범 목록.txt");
            string documents = Folder("문서");
            Folder("문서", "계약서"); Folder("문서", "세금"); Folder("문서", "보고서");
            Files(documents, "이력서.txt", "업무 메모.txt", "주간 보고.txt");
            string downloads = Folder("다운로드");
            Folder("다운로드", "설치 파일");
            Files(downloads, "매뉴얼.txt", "데이터 샘플.csv");
            string shared = Folder("공유용");
            Folder("공유용", "월별연구계획_20261019");
            Files(shared, "월별연구계획_20261019_v2.txt");
            string session = Path.Combine(output, "demo-session.json");
            SessionStore.Save(session, new SessionState
            {
                Layout = 4, Language = "ko", ExplorerFavoritesImported = true, WindowWidth = 1600, WindowHeight = 900,
                Favorites = [projects, photos, documents],
                Panes = [new PaneState { Tabs = [projects, launch], SelectedTab = 1 }, new PaneState { Tabs = [photos] },
                    new PaneState { Tabs = [documents] }, new PaneState { Tabs = [downloads] }]
            });
            using var search = new IndependentSearchBackend(new[] { demoRoot }, Path.Combine(output, "demo-index.fsix"));
            using var form = new MainForm(session, [], search, [demoRoot]);
            foreach (var window in CandidatePopups(form).Prepend<Form>(form))
                SetWindowLongPtr(window.Handle, -20, GetWindowLongPtr(window.Handle, -20) | 0x08000000); // WS_EX_NOACTIVATE: keep the user's focus
            var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
            var navigation = panes.Select(p => p.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.WhenAll(navigation).WaitAsync(TimeSpan.FromSeconds(20));
                    foreach (var pane in panes) pane.ActiveView!.SetViewMode(4);
                    foreach (var pane in panes) await ItemCountKnown(pane.ActiveView!);
                    await form.FolderTree.FollowPathAsync(launch);
                    await CaptureDemo(form, output, "01-light-panes");
                    form.SetColorTheme("dark");
                    await CaptureDemo(form, output, "02-dark-panes");
                    await form.FolderTree.FollowPathAsync(web);
                    await PaneChange(panes[0], () => form.FolderTree.RequestOpen(FolderTreeTarget.NewTab),
                        () => panes[0].TabCount == 3 && panes[0].ActiveView is { HasNavigated: true } && panes[0].CurrentPath == web);
                    panes[0].ActiveView!.SetViewMode(4);
                    await ItemCountKnown(panes[0].ActiveView!);
                    await CaptureDemo(form, output, "03-dark-tree-tab");
                    form.SetColorTheme("light");
                    await search.BuildOrLoadAsync(CancellationToken.None);
                    var address = Descendants(panes[1]).OfType<TextBox>().Single();
                    var results = SearchSignal(panes[1], predicate: () => panes[1].SearchCandidates.Count > 0);
                    address.Focus();
                    address.Text = "공유용/월별연구계획";
                    await results.WaitAsync(TimeSpan.FromSeconds(15));
                    await CaptureDemo(form, output, "04-light-search", address);
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
        finally { if (Directory.Exists(demoRoot)) Directory.Delete(demoRoot, recursive: true); }
        Console.WriteLine(failures == 0 ? "DEMO_CAPTURE_DONE" : "DEMO_CAPTURE_FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static string CountSuffix(UiText text, ShellViewHost host) => "  |  " + (host.ItemCount is int count
        ? text.T("{0} items", count) : text.T("Counting items..."));

    private static IEnumerable<Form> CandidatePopups(MainForm form) => Enumerable.Range(0, 4).Select(i => (Form)typeof(ExplorerPane)
        .GetField("candidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(form.GetPane(i))!);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    private static async Task ItemCountKnown(ShellViewHost host)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e) { if (host.ItemCount is not null) signal.TrySetResult(); }
        host.ItemCountChanged += Changed;
        try
        {
            Changed(host, EventArgs.Empty);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { host.ItemCountChanged -= Changed; }
    }

    private static async Task CaptureDemo(MainForm form, string output, string name, TextBox? anchor = null)
    {
        form.Refresh();
        string path = Path.Combine(output, name + ".png");
        await WindowCapture.SavePngAsync(form.Handle, path);
        var popup = CandidatePopups(form).SingleOrDefault(f => f.Visible);
        if (popup is not null && anchor is not null)
        {
            // WM_PRINT renders the WinForms candidate list itself without reading the screen.
            using var list = PrintForm(popup, 0);
            Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(form.Handle, 9, out NativeRect frameBounds, Marshal.SizeOf<NativeRect>()));
            Rectangle field = anchor.RectangleToScreen(anchor.ClientRectangle);
            Bitmap frame;
            using (var file = new Bitmap(path)) frame = new Bitmap(file);
            using (frame)
            {
                using (var graphics = Graphics.FromImage(frame))
                    graphics.DrawImage(list, field.Left - frameBounds.Left, field.Bottom + 2 - frameBounds.Top);
                frame.Save(path);
            }
        }
        Console.WriteLine("SCREENSHOT " + path);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);

    private static Bitmap PrintForm(Form form, uint flags)
    {
        form.Refresh();
        var bitmap = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(bitmap);
        nint context = graphics.GetHdc();
        try
        {
            if (!PrintWindow(form.Handle, context, flags)) throw new InvalidOperationException("Could not capture app window.");
        }
        finally { graphics.ReleaseHdc(context); }
        return bitmap;
    }

    private static Exception? IgnoreSplitterRepaintFailure(Exception? __exception) =>
        __exception is ExternalException ? null : __exception;

    private static void RunAppearanceScenario(string root)
    {
        string treeRoot = Directory.CreateDirectory(Path.Combine(root, "Tree")).FullName;
        string alpha = Directory.CreateDirectory(Path.Combine(treeRoot, "Alpha")).FullName;
        string beta = Directory.CreateDirectory(Path.Combine(treeRoot, "Beta")).FullName;
        File.WriteAllText(Path.Combine(treeRoot, "a.txt"), "a");
        string session = Path.Combine(root, "appearance.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, Favorites = [treeRoot],
            Panes = [new PaneState { Tabs = [treeRoot] }]
        });
        using var form = new MainForm(session, [], null, [treeRoot]);
        var pane = form.GetPane(0);
        var navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                Check(form.ColorTheme == "light" && !Theme.IsDark && form.FolderTreeVisible && form.FolderTree.Visible,
                    "light theme and folder tree are the defaults");
                var status = Descendants(pane).OfType<Label>().Single(l => l.AccessibleName == "Pane 1 status");
                var host = pane.ActiveView!;
                await ItemCount(host, () => { }, 3);
                Check(status.Text.EndsWith("3 items", StringComparison.Ordinal), "bottom status shows the native item count");
                await ItemCount(host, () => File.WriteAllText(Path.Combine(treeRoot, "b.txt"), "b"), 4);
                await ItemCount(host, () => File.Delete(Path.Combine(treeRoot, "a.txt")), 3);
                Check(status.Text.EndsWith("3 items", StringComparison.Ordinal), "item count follows created and deleted files");
                Check(Descendants(form).OfType<Button>().All(b => b.GetType().Name == "RoundedButton"),
                    "app buttons use rounded rendering");
                Check(ButtonCornersMatchBackground(pane), "light rounded button corners use the opaque background");

                await form.FolderTree.FollowPathAsync(alpha);
                Check(form.FolderTree.Tree.SelectedNode?.Name == alpha, "folder tree follows a nested path");
                await Navigate(host, () => form.FolderTree.RequestOpen(FolderTreeTarget.CurrentPane));
                Check(pane.CurrentPath == alpha && pane.TabCount == 1, "folder tree opens in the current pane");

                await form.FolderTree.FollowPathAsync(beta);
                await PaneChange(pane, () => form.FolderTree.RequestOpen(FolderTreeTarget.NewTab),
                    () => pane.TabCount == 2 && pane.ActiveView is { HasNavigated: true } && pane.CurrentPath == beta);
                Check(pane.TabCount == 2 && pane.CurrentPath == beta, "folder tree opens a new tab");

                await form.FolderTree.FollowPathAsync(alpha);
                var second = form.GetPane(1);
                await PaneChange(second, () => form.FolderTree.RequestOpen(FolderTreeTarget.NewPane),
                    () => second.ActiveView is { HasNavigated: true } && second.CurrentPath == alpha);
                Check(form.CurrentLayout.GetPanes().Length == 2 && second.Visible && pane.TabCount == 2,
                    "folder tree opens a new pane");
                CaptureLanguage(form, "theme-light");

                form.SetColorTheme("dark");
                Check(Theme.IsDark && form.BackColor == Theme.Canvas && status.BackColor != Color.White
                    && form.FolderTree.Tree.BackColor == Theme.Canvas, "dark theme recolors app surfaces");
                Check(ButtonCornersMatchBackground(pane), "dark rounded button corners use the opaque background");
                var visual = (IVisualProperties)typeof(ShellViewHost)
                    .GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(pane.ActiveView!)!;
                Check(visual.GetColor(2, out int listBack) == 0 && listBack == ColorTranslator.ToWin32(Theme.Surface)
                    && visual.GetColor(3, out int sortColumn) == 0 && sortColumn == ColorTranslator.ToWin32(Theme.Canvas)
                    && visual.GetColor(1, out int listText) == 0 && listText == ColorTranslator.ToWin32(Theme.Text),
                    "dark theme sets the Shell folder view's own text, background and sorted-column colors");
                CaptureLanguage(form, "theme-dark");
                form.SetFolderTreeVisible(false);
                Check(!form.FolderTree.Visible, "folder tree can be hidden");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session);
        Check(restored.ColorTheme == "dark" && !restored.ShowFolderTree, "theme and folder tree visibility persist");
    }

    private static bool ButtonCornersMatchBackground(ExplorerPane pane)
    {
        var button = Descendants(pane).OfType<Button>().First(b => b.Parent is TableLayoutPanel);
        Control? opaque = button.Parent;
        while (opaque is not null && opaque.BackColor.A < 255) opaque = opaque.Parent;
        using var image = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(image, new Rectangle(Point.Empty, button.Size));
        Color first = image.GetPixel(0, 0), last = image.GetPixel(button.Width - 1, button.Height - 1);
        bool matches = opaque is not null && first.ToArgb() == opaque.BackColor.ToArgb() && last.ToArgb() == opaque.BackColor.ToArgb();
        if (!matches) Console.Error.WriteLine($"CORNER {first} {last} expected {opaque?.BackColor} from {opaque?.GetType().Name}; parent {button.Parent?.BackColor}");
        return matches;
    }

    private static async Task ItemCount(ShellViewHost host, Action trigger, int expected)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e) { if (host.ItemCount == expected) signal.TrySetResult(); }
        host.ItemCountChanged += Changed;
        try
        {
            trigger();
            Changed(host, EventArgs.Empty);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { host.ItemCountChanged -= Changed; }
    }

    private static async Task PaneChange(ExplorerPane pane, Action trigger, Func<bool> complete)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e) { if (complete()) signal.TrySetResult(); }
        pane.StateChanged += Changed;
        try
        {
            trigger();
            Changed(pane, EventArgs.Empty);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { pane.StateChanged -= Changed; }
    }

    private static void RunEmptyPaneScenario(string session, string[] folders)
    {
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, ExplorerFavoritesImported = true, Favorites = [folders[0], folders[1]],
            Panes = [new PaneState { Tabs = [folders[0]] }]
        });
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                form.ApplyLayout(4);
                Check(Enumerable.Range(1, 3).All(i => form.GetPane(i).TabCount == 0),
                    "new split panes stay empty");
                Check(new[] { 2, 3 }.All(i =>
                {
                    var label = Descendants(form.GetPane(i)).OfType<Label>()
                        .Single(l => l.AccessibleName == $"Pane {i + 1} status");
                    return label.Visible && Screen.FromControl(form).WorkingArea.Contains(
                        label.RectangleToScreen(label.ClientRectangle));
                }), "bottom pane status labels remain inside the screen working area");
                var source = form.GetPane(0);
                form.ApplyLayout(1);
                Check(form.DockTab(source, source.ActiveView!, 0, SplitEdge.Right)
                    && source.TabCount == 0 && form.GetPane(1).TabCount == 1,
                    "splitting the only tab moves it without duplicating");
                var empty = form.GetPane(0);
                await PaneChange(empty, () => empty.Navigate(folders[2]),
                    () => empty.ActiveView is { HasNavigated: true });
                Check(empty.CurrentPath == folders[2], "navigation opens a folder in an empty pane");
                empty.CloseTab();
                Check(empty.TabCount == 0 && empty.CurrentPath == "", "closing the last tab leaves a blank pane");
                form.SetSidebarWidth(320);
                form.SetSidebarVisible(false);
                Check(!Descendants(form).OfType<ListBox>().Any(l => l.Visible), "quick access fully collapses");
                var version = form.Controls.OfType<Label>().Single(l => l.AccessibleName == "Application version");
                Check(version.Visible && version.Text == "v" + typeof(MainForm).Assembly.GetName().Version!.ToString(3)
                    && version.Bounds.Bottom == form.ClientRectangle.Bottom && version.Height >= version.PreferredHeight,
                    "lower-left version matches running assembly and remains visible with sidebar hidden");
                form.SetSidebarVisible(true);
                Check(form.SidebarWidth == 320, "quick access restores its resized width");
                form.ClearFavorites();
                Check(form.FavoritePaths.Count == 0 && form.DeletedFavoritePaths.Count == 2,
                    "clear all favorites retains recoverable entries");
                form.RestoreFavorite(folders[1]);
                Check(form.FavoritePaths.SequenceEqual(new[] { folders[1] }) && form.DeletedFavoritePaths.SequenceEqual(new[] { folders[0] }),
                    "restore selected favorite removes it from recovery list");
                var toolbar = form.Controls.OfType<ToolStrip>().Single();
                Check(toolbar.Items.Cast<ToolStripItem>().Where(i => i.Available).All(i =>
                    i.Bounds.Height >= TextRenderer.MeasureText(i.Text, i.Font).Height),
                    "toolbar item height fits its rendered text");
                Check(Descendants(form).OfType<TextBox>().All(t => t.Height >= t.PreferredHeight),
                    "address fields fit their preferred font height");
                Check(Descendants(form).OfType<Label>().Where(l => l.AccessibleName?.EndsWith(" status") == true)
                    .All(l => l.Height >= l.PreferredHeight), "status labels fit their preferred font height");
                Check(Descendants(form).OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<Button>())
                    .All(b => b.Width >= b.GetPreferredSize(Size.Empty).Width),
                    "navigation icon columns fit their preferred glyph width");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session);
        Check(restored.SidebarWidth == 320 && restored.ShowSidebar && restored.Panes[0].Tabs.Count == 0
            && restored.DeletedFavorites.SequenceEqual(new[] { folders[0] }),
            "sidebar width, blank pane and recovery list persist");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void RunLanguageScenario(string session, string[] folders)
    {
        SessionStore.Save(session, new SessionState
        {
            ExplorerFavoritesImported = true, Favorites = [folders[0]],
            Panes = [new PaneState { Tabs = [folders[0], folders[1]], SelectedTab = 0 }]
        });
        using (var form = new MainForm(session, []))
        {
            var pane = form.GetPane(0);
            var navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
            form.Shown += async (_, _) =>
            {
                try
                {
                    await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                    await Navigate(pane.ActiveView!, () => pane.Navigate(Path.Combine(folders[0], "한글 폴더")));
                    await Navigate(pane.ActiveView!, () => pane.ActiveView!.Travel(0x4000));
                    var toolbar = form.Controls.OfType<ToolStrip>().Single();
                    var selector = toolbar.Items.OfType<ToolStripComboBox>().Single();
                    Check(selector.Items.Cast<string>().SequenceEqual(new[] { "한국어", "English", "简体中文", "日本語" })
                        && form.Language == "ko", "four native language choices default to Korean");
                    var hosts = Descendants(pane).OfType<ShellViewHost>().ToArray();
                    var active = pane.ActiveView;
                    nint activeHandle = active!.Handle;
                    var state = pane.CaptureState();
                    var tree = form.CurrentLayout;
                    var address = Descendants(pane).OfType<TextBox>().Single();
                    address.Text = "Z:\\typed but not navigated\\日本語";
                    var favorites = Descendants(form).OfType<ListBox>().Single(l => l.AccessibleName == "Favorites");
                    favorites.SelectedIndex = 0;
                    var quickAccess = Descendants(form).OfType<ListBox>().Single(l => l.AccessibleName == "Quick access");
                    quickAccess.SelectedIndex = 2;
                    string favoriteName = favorites.Items[0]!.ToString()!;
                    foreach (var (index, language) in new[] { (1, "en"), (2, "zh-CN"), (3, "ja"), (0, "ko"), (3, "ja") })
                    {
                        await SelectLanguageFromDropdown(selector, index);
                        var text = new UiText(language);
                        Check(form.Language == language
                            && toolbar.Items.Cast<ToolStripItem>().Single(i => i.AccessibleName == "1 pane layout").Text == text.T("1 pane")
                            && toolbar.Items.Cast<ToolStripItem>().Single(i => i.AccessibleName == "Sidebar").ToolTipText == text.T("Show / hide quick access sidebar"),
                            language + " selector refreshes toolbar labels and hover text");
                        var up = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 Up (Alt+Up)");
                        var go = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 go to address");
                        var choose = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 Choose folder");
                        Check(pane.ToolTipFor(up) == text.T("Up (Alt+Up)")
                            && pane.ToolTipFor(go) == text.T("Go to typed address (Enter)")
                            && pane.ToolTipFor(choose) == text.T("Browse for a folder: open folder picker"),
                            language + " navigation tooltips retain parent, address and picker meanings");
                        Check(quickAccess.Items[0]!.ToString() == text.T("Home")
                            && favorites.Items[0]!.ToString() == favoriteName
                            && quickAccess.SelectedIndex == 2 && favorites.SelectedIndex == 0
                            && favorites.ContextMenuStrip!.Items[0].Text == text.T("Open in new tab"),
                            language + " sidebar and favorite actions translate without changing folder names");
                        string paneStatus = Descendants(pane).OfType<Label>().Single(l => l.AccessibleName == "Pane 1 status").Text;
                        string expectedStatus = text.T("Pane {0}  |  {1} tabs  |  {2}", 1, 2, active.DisplayName) + CountSuffix(text, active);
                        if (paneStatus != expectedStatus) Console.Error.WriteLine($"STATUS actual='{paneStatus}' expected='{expectedStatus}'");
                        Check(paneStatus == expectedStatus
                            && Descendants(form.GetPane(1)).OfType<Label>().Any(l =>
                                l.Text == text.T("Enter a folder path above or choose a folder from Pane options.")),
                            language + " pane status and empty hints translate");
                        Check(ReferenceEquals(pane.ActiveView, active) && active.Handle == activeHandle
                            && Descendants(pane).OfType<ShellViewHost>().SequenceEqual(hosts)
                            && pane.CaptureState().Tabs.SequenceEqual(state.Tabs) && pane.SelectedIndex == state.SelectedTab
                            && form.CurrentLayout == tree && address.Text == "Z:\\typed but not navigated\\日本語"
                            && HasNativeDropTarget(active),
                            language + " switch preserves native views, tabs, typed address and drop targets");
                        Check(selector.GetCurrentParent() == toolbar && toolbar.ClientRectangle.Contains(selector.Bounds),
                            language + " selector stays visible in toolbar");
                        CheckLanguagePlacement(toolbar, selector, language);
                        var options = Descendants(pane).OfType<Button>().Single(b => b.AccessibleName == "Pane 1 Pane options");
                        options.PerformClick();
                        var menu = (ContextMenuStrip)typeof(ExplorerPane)
                            .GetField("tabMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pane)!;
                        Check(menu.Items[0].Text == text.T("Refresh    F5")
                            && menu.Items[4].Text == text.T("Choose folder..."), language + " pane options translate");
                        menu.Close();
                        typeof(ExplorerPane).GetMethod("ShowTabMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(pane, [0]);
                        Check(menu.Items[0].Text == text.T("New tab")
                            && ((ToolStripMenuItem)menu.Items[3]).DropDownItems[0].Text == text.T("Pane {0}", 2)
                            && ((ToolStripMenuItem)menu.Items[4]).DropDownItems[0].Text == text.T("Left"),
                            language + " tab and split context menus translate");
                        menu.Close();
                        // Opening a tab menu selects it; restore the unsubmitted address before the next switch.
                        address.Text = "Z:\\typed but not navigated\\日本語";
                        CaptureLanguage(form, language + "-desktop");
                        form.Size = new Size(900, 600);
                        Check(selector.GetCurrentParent() == toolbar && toolbar.ClientRectangle.Contains(selector.Bounds),
                            language + " selector remains visible at minimum window size");
                        CheckLanguagePlacement(toolbar, selector, language + " minimum");
                        CaptureLanguage(form, language + "-minimum");
                        form.Size = new Size(1400, 900);
                    }
                    pane.SetLocalizedStatus("Folder path copied.");
                    selector.SelectedIndex = 2;
                    Check(Descendants(pane).OfType<Label>().Single(l => l.AccessibleName == "Pane 1 status").Text
                        == new UiText("zh-CN").T("Folder path copied.") + CountSuffix(new UiText("zh-CN"), active),
                        "existing action status refreshes on language switch");
                    selector.SelectedIndex = 3;
                    await Navigate(active, () => active.Travel(0x8000));
                    Check(pane.CurrentPath == Path.Combine(folders[0], "한글 폴더"),
                        "native forward history survives all language switches");
                    ValidateTranslations();
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        Check(SessionStore.Load(session).Language == "ja", "selected Japanese language saves on close");
        using var reopened = new MainForm(session, []);
        Check(reopened.Language == "ja"
            && reopened.Controls.OfType<ToolStrip>().Single().Items.OfType<ToolStripComboBox>().Single().SelectedIndex == 3
            && reopened.GetPane(0).TabCount == 2, "reopened session retains language and tabs");
    }

    private static void ValidateTranslations()
    {
        var strings = (System.Collections.IDictionary)typeof(UiText)
            .GetField("Strings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            var text = new UiText(language);
            foreach (string key in strings.Keys)
            {
                string translated = text.T(key, 1, 2, 3);
                if (string.IsNullOrWhiteSpace(translated) || (language != "en" && translated == key))
                    throw new InvalidOperationException(language + " missing translation for " + key);
            }
            Check(!string.IsNullOrWhiteSpace(text.Help), language + " dictionary formats every app-owned message");
        }
    }

    private static void CaptureLanguage(Form form, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("FOLDERSPLITTER_UI_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        form.Refresh();
        using var bitmap = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(bitmap);
        nint context = graphics.GetHdc();
        try
        {
            if (!PrintWindow(form.Handle, context, 2))
                throw new InvalidOperationException("Could not capture app window.");
        }
        finally { graphics.ReleaseHdc(context); }
        string path = Path.Combine(directory, name + ".png");
        bitmap.Save(path);
        Console.WriteLine("SCREENSHOT " + path);
    }

    private static void CheckLanguagePlacement(ToolStrip toolbar, ToolStripComboBox selector, string name)
    {
        var items = toolbar.Items.Cast<ToolStripItem>().ToArray();
        var label = items.Single(i => i.AccessibleName == "Language");
        var close = items.Single(i => i.AccessibleName == "Close tab");
        var help = items.Single(i => i.AccessibleName == "Shortcuts");
        Check(new[] { label, selector, close, help }.All(i =>
                i.Alignment == ToolStripItemAlignment.Right && i.Placement == ToolStripItemPlacement.Main)
            && label.Bounds.Right <= selector.Bounds.Left && selector.Bounds.Right <= close.Bounds.Left
            && close.Bounds.Right <= help.Bounds.Left,
            name + " right group orders language, close tab and shortcuts");
        var visible = items.Where(i => i.Placement == ToolStripItemPlacement.Main && i.Available).ToArray();
        Check(!visible.SelectMany((item, index) => visible.Skip(index + 1)
                .Select(other => item.Bounds.IntersectsWith(other.Bounds))).Any(overlap => overlap)
            && selector.Width >= selector.Items.Cast<string>().Max(s => TextRenderer.MeasureText(s, selector.Font).Width)
                + SystemInformation.VerticalScrollBarWidth,
            name + " toolbar has no overlaps and native language names fit");
    }

    private static async Task SelectLanguageFromDropdown(ToolStripComboBox selector, int index)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Selected(object? sender, EventArgs e)
        {
            if (selector.SelectedIndex == index) changed.TrySetResult();
        }
        selector.SelectedIndexChanged += Selected;
        try
        {
            selector.ComboBox.Focus();
            selector.ComboBox.DroppedDown = true;
            Check(selector.ComboBox.DroppedDown, "language dropdown opens");
            selector.ComboBox.DroppedDown = false;
            // The private QA desktop never gives this window foreground activation, and a dropped list
            // then commits keyboard moves inconsistently. Arrow keys on the closed native combo send the
            // same CBN_SELCHANGE a keyboard user triggers.
            int direction = index > selector.SelectedIndex ? 0x28 : 0x26;
            int steps = Math.Abs(index - selector.SelectedIndex);
            // SendMessage runs the native combo's key handling, and the CBN_SELCHANGE it raises, before returning.
            for (int step = 0; step < steps; step++)
                SendMessage(selector.ComboBox.Handle, 0x100, direction, 0);
            try { await changed.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Language {index} not selected: selected={selector.SelectedIndex} " +
                    $"dropped={selector.ComboBox.DroppedDown} focused={selector.ComboBox.Focused} " +
                    $"activeForm={Form.ActiveForm?.Text ?? "none"}", ex);
            }
        }
        finally { selector.SelectedIndexChanged -= Selected; }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(nint handle, int index,
        System.Text.StringBuilder value, uint length, out uint needed);

    private static void RunSearchScenario(string root)
    {
        string fixture = Path.Combine(root, "search");
        string share = Directory.CreateDirectory(Path.Combine(fixture, "공유용")).FullName;
        string folder = Directory.CreateDirectory(Path.Combine(share, "월별연구계획_20261019")).FullName;
        string file = Path.Combine(share, "월별연구계획_20261019_v2.txt");
        File.WriteAllText(file, "search fixture");
        Directory.CreateDirectory(Path.Combine(fixture, "X공유용", "월별연구계획_20261019"));
        Directory.CreateDirectory(Path.Combine(fixture, "공유용X", "월별연구계획_20261019"));
        File.WriteAllText(Path.Combine(fixture, "distinct-second-query.txt"), "stale result fixture");
        string session = Path.Combine(root, "search-session.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [fixture] }]
        });
        using var real = new IndependentSearchBackend(new[] { fixture }, Path.Combine(root, "search-index.fsix"));
        using var backend = new DelayedSearchBackend(real);
        using var form = new MainForm(session, [], backend);
        var pane = form.GetPane(0);
        var address = Descendants(pane).OfType<TextBox>().Single();
        Task navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(15));
                const string query = "공유용/월별연구계획_20261019";
                var loading = SearchSignal(pane);
                address.Focus();
                address.Text = query;
                await loading.WaitAsync(TimeSpan.FromSeconds(10));
                Check(pane.CandidatesVisible && pane.SearchCandidates.Count == 0 && !real.IsReady,
                    "first index build shows a nonselectable loading row without blocking address");
                CaptureLanguage(form.OwnedForms.OfType<AddressCandidates>().Single(), "search-building-popup");
                var initialResults = SearchSignal(pane, predicate: () => pane.SearchCandidates.Count > 0);
                await real.BuildOrLoadAsync(CancellationToken.None);
                await initialResults.WaitAsync(TimeSpan.FromSeconds(15));
                Check(pane.CandidatesVisible && pane.SearchCandidates.Count == 3
                    && pane.SearchCandidates[0].FullPath == folder
                    && pane.SearchCandidates.All(h => !h.FullPath.Contains("공유용X")),
                    "partial path candidates show matching anchors with exact ancestor first");
                Check(address.AutoCompleteMode == AutoCompleteMode.None,
                    "partial path candidates do not compete with native directory autocomplete");
                CaptureLanguage(form, "search-candidates");
                CaptureLanguage(form.OwnedForms.OfType<AddressCandidates>().Single(), "search-candidates-popup");
                var original = pane.SearchCandidates.Select(h => h.FullPath).ToArray();
                var stablePopup = form.OwnedForms.OfType<AddressCandidates>().Single();
                var stableList = stablePopup.Controls.OfType<ListBox>().Single();
                stableList.SelectedIndex = 1;
                var unrelatedUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler? unrelatedChanged = null;
                unrelatedChanged = (_, _) =>
                {
                    if (real.QueryAll(SearchQuery.Parse("unrelated-index-notification")).Count == 0) return;
                    backend.IndexChanged -= unrelatedChanged;
                    form.BeginInvoke(() =>
                    {
                        Check(pane.CandidatesVisible && stableList.SelectedIndex == 1 &&
                            pane.SearchCandidates.Select(hit => hit.FullPath).SequenceEqual(original),
                            "unrelated real index update preserves visible candidates and pointer selection");
                        unrelatedUpdated.TrySetResult();
                    });
                };
                backend.IndexChanged += unrelatedChanged;
                try
                {
                    File.WriteAllText(Path.Combine(fixture, "unrelated-index-notification.txt"), "unrelated change");
                    await unrelatedUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { backend.IndexChanged -= unrelatedChanged; }
                foreach (string variant in new[] { query.Replace('/', '\\'), query.Replace("/", "//"), "  " + query + "  " })
                {
                    await Search(pane, address, variant);
                    Check(pane.SearchCandidates.Select(h => h.FullPath).SequenceEqual(original),
                        "partial path slash and padding variant preserves candidate list");
                }
                await Navigate(pane.ActiveView!, () => PostMessage(address.Handle, 0x100, (nint)Keys.Enter, 0));
                Check(pane.CurrentPath == folder, "Enter opens selected partial-path folder candidate");

                await Search(pane, address, query);
                var popup = form.OwnedForms.OfType<AddressCandidates>().Single();
                var list = popup.Controls.OfType<ListBox>().Single();
                int row = list.Items.Cast<SearchHit>().ToList().FindIndex(hit => hit.FullPath == file);
                Check(row >= 0, "file candidate is present in native popup list");
                int point = (list.ItemHeight * row + list.ItemHeight / 2) << 16 | 20;
                await Navigate(pane.ActiveView!, () =>
                {
                    PostMessage(list.Handle, 0x201, 1, point);
                    PostMessage(list.Handle, 0x202, 0, point);
                });
                Check(pane.CurrentPath == share && SelectedPaths(pane.ActiveView!).SequenceEqual(new[] { file }),
                    "click file candidate opens parent and selects native file");
                CaptureLanguage(form, "search-file-selected");

                await Search(pane, address, "no-such-search-entry-97d31e");
                Check(!pane.CandidatesVisible && pane.SearchCandidates.Count == 0, "zero matches closes candidate popup");
                await Search(pane, address, query);
                form.SetLanguage("ja");
                Check(form.Language == "ja", "language switch with candidates open does not throw");
                await Search(pane, address, query);
                var recreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler handleCreated = (_, _) => recreated.TrySetResult();
                address.HandleCreated += handleCreated;
                try
                {
                    address.Text = fixture;
                    await recreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { address.HandleCreated -= handleCreated; }
                Check(!pane.CandidatesVisible && address.AutoCompleteMode == AutoCompleteMode.SuggestAppend,
                    "rooted paths keep native autocomplete without partial search");

                backend.DelayNext();
                address.Focus();
                address.Text = query;
                await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Search(pane, address, "distinct-second-query");
                var before = pane.SearchCandidates.Select(h => h.FullPath).ToArray();
                var oldSettled = SearchSignal(pane, completed: true);
                backend.Release();
                await oldSettled.WaitAsync(TimeSpan.FromSeconds(10));
                Check(pane.SearchCandidates.Select(h => h.FullPath).SequenceEqual(before)
                    && before.Length == 1 && before[0].EndsWith("distinct-second-query.txt"),
                    "late uncancellable old query cannot replace newer candidates");

                await Search(pane, address, query);
                pane.CloseTab();
                Check(pane.TabCount == 0 && !pane.CandidatesVisible, "closing active tab dismisses candidates");
                var changed = PaneChange(pane, () => pane.AddTab(file), () => pane.ActiveView?.HasNavigated == true);
                await changed;
                Check(pane.CurrentPath == share && SelectedPaths(pane.ActiveView!).SequenceEqual(new[] { file }),
                    "file candidate in an empty pane selects file on first host creation");

                await Search(pane, address, query);
                list = form.OwnedForms.OfType<AddressCandidates>().Single().Controls.OfType<ListBox>().Single();
                list.SelectedIndex = list.Items.Cast<SearchHit>().ToList().FindIndex(hit => hit.FullPath == file);
                backend.HoldChanges = true; // Real index updates continue; simulate a delayed notification to this UI.
                var status = Descendants(pane).OfType<Label>().Single(label => label.AccessibleName == "Pane 1 status");
                var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler statusChanged = (_, _) =>
                {
                    if (status.Text.Contains(file, StringComparison.Ordinal)) unavailable.TrySetResult();
                };
                status.TextChanged += statusChanged;
                try
                {
                    File.Delete(file);
                    PostMessage(address.Handle, 0x100, (nint)Keys.Enter, 0);
                    await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Check(!pane.CandidatesVisible && pane.CurrentPath == share && pane.ActiveView!.ErrorStatus is null,
                        "deleted cached candidate reports unavailable without navigating or crashing");
                }
                finally { status.TextChanged -= statusChanged; backend.HoldChanges = false; }

                backend.Disabled = true;
                address.Text = query + " ";
                Check(!pane.CandidatesVisible, "unavailable search backend exposes no candidate popup");
                await Navigate(pane.ActiveView!, () => pane.Navigate(fixture));
                Check(pane.CurrentPath == fixture && !pane.CandidatesVisible,
                    "direct folder navigation remains usable without search backend");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { backend.Release(); form.Close(); }
        };
        Application.Run(form);
    }

    private static async Task Search(ExplorerPane pane, TextBox address, string value)
    {
        var signal = SearchSignal(pane);
        address.Focus();
        address.Text = value;
        await signal.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static Task SearchSignal(ExplorerPane pane, bool completed = false, Func<bool>? predicate = null)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? changed = null;
        changed = (_, _) =>
        {
            if (predicate is not null && !predicate()) return;
            if (completed) pane.SearchSettled -= changed;
            else pane.CandidatesUpdated -= changed;
            source.TrySetResult();
        };
        if (completed) pane.SearchSettled += changed;
        else pane.CandidatesUpdated += changed;
        return source.Task;
    }

    private sealed class DelayedSearchBackend : IPathSearchBackend
    {
        private readonly IPathSearchBackend inner;
        private bool delayNext;
        internal volatile bool HoldChanges;
        internal volatile bool Disabled;
        private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DelayedSearchBackend(IPathSearchBackend inner)
        {
            this.inner = inner;
            inner.IndexChanged += Forward;
        }
        private void Forward(object? sender, EventArgs e)
        {
            if (!HoldChanges) IndexChanged?.Invoke(this, e);
        }
        public bool IsAvailable => !Disabled && inner.IsAvailable;
        public bool IsReady => inner.IsReady;
        public event EventHandler? IndexChanged;
        internal void DelayNext()
        {
            delayNext = true;
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        internal void Release() => gate.TrySetResult();
        public async Task<IReadOnlyList<SearchHit>> QueryAsync(string text, IReadOnlyList<string> roots, int limit, CancellationToken ct)
        {
            if (!delayNext) return await inner.QueryAsync(text, roots, limit, ct);
            delayNext = false;
            var hits = await inner.QueryAsync(text, roots, limit, CancellationToken.None);
            Started.TrySetResult();
            await gate.Task;
            return hits;
        }
        public void Dispose() { inner.IndexChanged -= Forward; Release(); }
    }

    private static Task NavigationSignal(ShellViewHost host)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? completed = null;
        EventHandler<string>? failed = null;
        completed = (_, _) =>
        {
            host.Navigated -= completed;
            host.NavigationError -= failed;
            source.TrySetResult();
        };
        failed = (_, error) =>
        {
            host.Navigated -= completed;
            host.NavigationError -= failed;
            source.TrySetException(new IOException(error));
        };
        host.Navigated += completed;
        host.NavigationError += failed;
        return source.Task;
    }

    private static async Task Navigate(ShellViewHost host, Action trigger)
    {
        var navigation = NavigationSignal(host);
        trigger();
        await navigation.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static void ClickLayout(MainForm form, int count)
    {
        var button = Descendants(form).OfType<ToolStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(item => item.AccessibleName == $"{count} pane layout");
        button.PerformClick();
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new InvalidOperationException(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint context, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetProp(nint window, string property);

    private static bool HasNativeDropTarget(Control host)
    {
        bool registered = false;
        EnumChildWindows(host.Handle, (window, _) =>
        {
            registered |= GetProp(window, "OleDropTargetInterface") != 0
                || GetProp(window, "OleDropTargetMarshalHwnd") != 0;
            return true;
        }, 0);
        return registered;
    }

    private static string[] SelectedPaths(ShellViewHost host)
    {
        var view = (IShellView)typeof(ShellViewHost)
            .GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(host)!;
        Guid dataId = new("0000010e-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(view.GetItemObject(1, ref dataId, out nint pointer));
        var data = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
        };
        try
        {
            data.GetData(ref format, out var medium);
            try
            {
                uint count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
                var paths = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint length = DragQueryFile(medium.unionmember, i, null, 0);
                    var buffer = new System.Text.StringBuilder((int)length + 1);
                    DragQueryFile(medium.unionmember, i, buffer, length + 1);
                    paths[i] = buffer.ToString();
                }
                return paths;
            }
            finally { ReleaseStgMedium(ref medium); }
        }
        finally { Marshal.ReleaseComObject(data); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, System.Text.StringBuilder? path, uint size);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
}
