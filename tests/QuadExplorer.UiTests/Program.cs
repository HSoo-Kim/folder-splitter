using QuadExplorer;
using QuadExplorer.Search;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static int failures;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--ai-open-receipt", var pipe, var openedFile])
            return WriteAiOpenReceipt(pipe, openedFile);
        string? expectedDesktop = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_DESKTOP");
        var actualDesktop = new System.Text.StringBuilder(256);
        // Demo capture runs on the user's desktop only with their consent: Shell views paint only in a visible,
        // DWM-composed window. It never takes activation and captures the window's own content.
        bool demoCapture = args is ["--demo-capture", _, _];
        bool aiSearchDemo = args is ["--ai-search-demo", _, _];
        bool scrollbarDemo = args is ["--scrollbar-demo"];
        if (!demoCapture && !scrollbarDemo && !aiSearchDemo && (expectedDesktop is null || !expectedDesktop.StartsWith("FolderSplitter-QA-", StringComparison.Ordinal) ||
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
        if (args is ["--native-open-receipt", var receiptPipe])
            return RunNativeOpenReceipt(receiptPipe);
        if (args is ["--ai-search-demo", var aiOutput, var aiRoot])
            return RunAiSearchDemo(aiOutput, aiRoot);
        // Design review: captures the app chrome in several themes instead of running the checks.
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_DESIGN_CAPTURE") is { Length: > 0 } designOutput)
            return RunDesignCapture(designOutput);
        string root = Path.Combine(Path.GetTempPath(), "QuadExplorer.UiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folders = Enumerable.Range(1, 4).Select(i => Directory.CreateDirectory(Path.Combine(root, $"Pane-{i}")).FullName).ToArray();
            if (scrollbarDemo) return RunScrollbarDemo(root);
            if (args is ["--ai-reading-gpt-ui"])
            {
                RunAiReadingRealUiScenario(root, useGpt: true);
                Console.WriteLine($"PASS {checks} GPT document-reading GUI checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--ai-model-menu"])
            {
                RunAiModelMenuScenario(Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR"));
                Console.WriteLine($"PASS {checks} AI model menu checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--breadcrumb-dpi", var breadcrumbOutput])
            {
                RunBreadcrumbDpiScenario(root, breadcrumbOutput);
                Console.WriteLine($"BREADCRUMB_DPI_RESULT checks={checks} failures={failures}");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--native-open-selection"])
            {
                RunNativeOpenSelectionScenario(root);
                Console.WriteLine($"NATIVE_OPEN_RESULT checks={checks} failures={failures}");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--ai-reading-real-ui"])
            {
                RunAiReadingRealUiScenario(root);
                Console.WriteLine($"PASS {checks} real document-reading GUI checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--ai-search-real-ui"])
            {
                RunAiSearchRealUiScenario(root);
                Console.WriteLine($"PASS {checks} real-model AI GUI checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--ai-search"])
            {
                RunAiSearchPaneScenario(root);
                RunAiSearchIntegrationScenario(root);
                Console.WriteLine($"PASS {checks} AI search native checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--navigation-theme"])
            {
                RunNativeNavigationThemeScenario(root);
                Console.WriteLine($"PASS {checks} native navigation theme checks");
                return 0;
            }
            if (args is ["--native-groups"])
            {
                RunNativeGroupThemeScenario(root);
                Console.WriteLine($"PASS {checks} native group theme checks");
                return 0;
            }
            if (args is ["--native-scrollbars"])
            {
                RunNativeScrollbarScenario(root);
                Console.WriteLine($"PASS {checks} native scrollbar checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--shortcut-menu"])
            {
                RunNativeShortcutMenuScenario(root);
                Console.WriteLine($"PASS {checks} native shortcut-menu checks");
                return 0;
            }
            if (args is ["--active-pane"])
            {
                RunActivePaneAppearanceScenario(root);
                Console.WriteLine($"PASS {checks} active pane checks");
                return failures == 0 ? 0 : 1;
            }
            if (args is ["--native-theme"])
            {
                RunNativeDetailsThemeScenario(root);
                Console.WriteLine($"PASS {checks} native theme checks");
                return 0;
            }
            if (args is ["--native-appearance"])
            {
                RunNativeDetailsAppearanceScenario(root, folders);
                RunNativeDetailsThemeScenario(root);
                Console.WriteLine($"PASS {checks} native appearance checks");
                return 0;
            }
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
                    Check(new Font(QuadExplorer.Theme.IconFont, 10).Name == QuadExplorer.Theme.IconFont,
                        "navigation icons use an installed symbol font");
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
                    panes[0].FocusFiles();
                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(child));
                    var mouseHost = panes[0].ActiveView!;
                    await Navigate(mouseHost, () => PostMessage(mouseHost.Handle, 0x20C, 1 << 16, 0));
                    Check(panes[0].CurrentPath == folders[0], "mouse back button navigates the pane back");
                    await Navigate(mouseHost, () => PostMessage(mouseHost.Handle, 0x20C, 2 << 16, 0));
                    Check(panes[0].CurrentPath == child, "mouse forward button navigates the pane forward");
                    await Navigate(mouseHost, () => PostMessage(mouseHost.Handle, 0x20C, 1 << 16, 0));

                    var recentText = new UiText(form.Language).T("Recent items");
                    Check(Descendants(form).OfType<ListBox>().Single(l => l.AccessibleName == "Quick access").Items
                        .Cast<object>().Any(item => item.ToString() == recentText), "quick access lists recent items");
                    string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(recent));
                    Check(ShellViewHost.IsRecent(panes[0].CurrentPath) && panes[0].ActiveView!.SortedNewestFirst,
                        "recent items open sorted newest first");
                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(folders[0]));

                    var byType = new PropertyKey { FormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = 4 };
                    bool GroupedByType(ShellViewHost host) => host.GroupKey is { } key && key.FormatId == byType.FormatId && key.PropertyId == byType.PropertyId;
                    panes[0].ActiveView!.GroupBy(byType);
                    Check(GroupedByType(panes[0].ActiveView!), "folder can be grouped by type");
                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(child));
                    Check(!GroupedByType(panes[0].ActiveView!), "another folder keeps its own grouping");
                    await Navigate(panes[0].ActiveView!, () => panes[0].Navigate(folders[0]));
                    Check(GroupedByType(panes[0].ActiveView!), "a folder's grouping is remembered after leaving it");
                    panes[0].ActiveView!.GroupBy(default);

                    string selectedFile = Path.Combine(folders[0], "sample.txt");
                    var selectionSettled = SelectionSignal(panes[0].ActiveView!);
                    await Navigate(panes[0].ActiveView!, () => panes[0].ActiveView!.NavigateAndSelect(selectedFile));
                    await selectionSettled.WaitAsync(TimeSpan.FromSeconds(10));
                    Check(SelectedPaths(panes[0].ActiveView!).SequenceEqual(new[] { selectedFile },
                        StringComparer.OrdinalIgnoreCase), "file candidate selects its actual native shell item");

                    panes[0].AddTab(folders[1], select: false);
                    var newView = Descendants(panes[0]).OfType<ShellViewHost>().Single(h => h.LocationPath == folders[1]);
                    await Navigate(newView, () => panes[0].SelectTab(1));
                    Check(panes[0].TabCount == 2 && panes[0].CurrentPath == folders[1], "new independent tab");
                    var tabItems = Descendants(panes[0]).OfType<ToolStrip>().Single(s => s.AccessibleName == "Pane 1 tabs").Items;
                    var plus = tabItems.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Pane 1 new tab");
                    int gap = plus.Bounds.Left - tabItems[tabItems.IndexOf(plus) - 1].Bounds.Right;
                    Check(plus.AccessibleName == "Pane 1 new tab" && plus.Visible && gap is >= 10 and <= 20,
                        "new tab button sits just after the last tab with a gap");
                    var strip = plus.Owner!;
                    using (var image = new Bitmap(strip.Width, strip.Height))
                    {
                        strip.DrawToBitmap(image, new Rectangle(Point.Empty, strip.Size));
                        Color edge = image.GetPixel(plus.Bounds.Left + 3, plus.Bounds.Top + plus.Bounds.Height / 2);
                        Color gapColor = image.GetPixel(plus.Bounds.Left - 5, plus.Bounds.Top + plus.Bounds.Height / 2);
                        Check(edge.ToArgb() == gapColor.ToArgb(), "new tab button has no outline when idle");
                        plus.Select();
                        strip.DrawToBitmap(image, new Rectangle(Point.Empty, strip.Size));
                        Color hoverEdge = image.GetPixel(plus.Bounds.Left + 3, plus.Bounds.Top + plus.Bounds.Height / 2);
                        Check(plus.Selected && hoverEdge.ToArgb() != gapColor.ToArgb(),
                            "new tab button shows an outline when selected by pointer or keyboard");
                    }
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
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
            var restored = SessionStore.Load(session, out var warning);
            Check(warning is null && restored.Layout == 4 && restored.Panes[0].Tabs.Count == 2, "session saved on close");
            Check(restored.Panes.Skip(1).Select(p => p.Tabs[0]).SequenceEqual(folders.Skip(1)), "inactive pane paths persisted");
            RunDockingScenario(session, folders);
            RunEmptyPaneScenario(session, folders);
            RunBlankTabDockingScenario(root);
            RunPaneCloseScenario(root, folders);
            RunPaneReopenScenario(root, folders);
            RunCloseHistoryScopeScenario(root, folders);
            RunCommandBarPreferenceScenario(root);
            RunNativeDetailsAppearanceScenario(root, folders);
            RunNativeDetailsThemeScenario(root);
            RunNativeGroupThemeScenario(root);
            RunNativeNavigationThemeScenario(root);
            RunNativeScrollbarScenario(root);
            RunNativeOpenSelectionScenario(root);
            RunNativeShortcutMenuScenario(root);
            RunHelpDialogScenario(root);
            RunStatusChromeScenario(root);
            RunActivePaneAppearanceScenario(root);
            RunLanguageScenario(session, folders);
            RunSearchScenario(root);
            RunAiSearchPaneScenario(root);
            RunAiSearchIntegrationScenario(root);
            RunAppearanceScenario(root);
            RunTrayScenario(root);
            RunUpdateScenario(root);
            RunPreviewScenario(root);
            RunPdfScenario(root);
            RunTabStripScenario(root);
            RunConvenienceScenario(root);
            RunBreadcrumbDpiScenario(root, null);
            RunExplorerParityScenario(root);
            RunResizeMetric(root);
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

                panes[0].SelectTab(0);
                ClickLayout(form, 1);
                var moving = panes[0].ActiveTab!;
                await PaneChange(panes[1], () => Check(form.DockTab(panes[0], moving, 0, SplitEdge.Right),
                    "edge docking accepts a second pane"), () => panes[1].ActiveView!.HasNavigated && panes[1].CurrentPath == folders[0]);
                Check(form.CurrentLayout.GetPanes().Length == 2 && panes[0].TabCount == 1 && panes[1].TabCount == 2,
                    "edge docking moves the chosen tab without losing cached tabs");
                int tabsAfterMove = panes.Sum(p => p.TabCount);
                typeof(MainForm).GetMethod("HandleShortcut",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, [Keys.Control | Keys.Shift | Keys.T]);
                Check(panes.Sum(p => p.TabCount) == tabsAfterMove && form.CurrentLayout.GetPanes().Length == 2,
                    "moving a tab does not create a closed-tab undo entry or duplicate the moved tab");

                moving = panes[1].ActiveTab!;
                await PaneChange(panes[2], () => Check(form.DockTab(panes[1], moving, 1, SplitEdge.Bottom),
                    "edge docking accepts a third pane"), () => panes[2].ActiveView!.HasNavigated && panes[2].CurrentPath == folders[0]);
                Check(form.CurrentLayout.First!.IsLeaf && !form.CurrentLayout.Second!.IsLeaf
                    && !form.CurrentLayout.Second.Vertical, "docking creates a one-plus-two layout");

                moving = panes[2].ActiveTab!;
                await PaneChange(panes[3], () => Check(form.DockTab(panes[2], moving, 2, SplitEdge.Left),
                    "edge docking accepts a fourth pane"), () => panes[3].ActiveView!.HasNavigated && panes[3].CurrentPath == folders[0]);
                var before = form.CurrentLayout;
                int totalTabs = panes.Sum(p => p.TabCount);
                Check(!form.DockTab(panes[3], panes[3].ActiveTab!, 3, SplitEdge.Bottom)
                    && form.CurrentLayout == before && panes.Sum(p => p.TabCount) == totalTabs,
                    "fifth-pane rejection keeps layout and tabs unchanged");

                moving = panes[2].ActiveTab!;
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
            finally { form.ExitApplication(); }
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
        // FOLDERSPLITTER_DEMO_LANGUAGE=en captures an English app, English sample names, and English Shell columns.
        bool en = Environment.GetEnvironmentVariable("FOLDERSPLITTER_DEMO_LANGUAGE") == "en";
        string L(string ko, string english) => en ? english : ko;
        // The Shell draws Details columns, dates and byte units in the Windows display language, so an English
        // capture uses large icons and kilobyte-sized files to show no Windows-localized text.
        uint viewMode = en ? 1u : 4u;
        int viewSize = en ? 48 : 16;
        if (en)
        {
            SetProcessPreferredUILanguages(0x8, "en-US\0", out _); // MUI_LANGUAGE_NAME
            SetThreadLocale(0x0409);
        }
        Directory.CreateDirectory(output);
        try
        {
            string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([demoRoot, .. parts])).FullName;
            void Files(string folder, params string[] names)
            {
                foreach (string name in names) File.WriteAllText(Path.Combine(folder, name), en
                    ? string.Concat(Enumerable.Repeat("Folder Splitter demo file\n", 60)) : "Folder Splitter demo file");
            }
            string projects = Folder(L("프로젝트", "Projects")), launch = Folder(L("프로젝트", "Projects"), L("2026 신제품 출시", "2026 Product Launch")), web = Folder(L("프로젝트", "Projects"), L("웹사이트 개편", "Website Redesign"));
            Folder(L("프로젝트", "Projects"), L("고객 리서치", "Customer Research")); Folder(L("프로젝트", "Projects"), L("디자인 시안", "Design Drafts"));
            Files(projects, L("프로젝트 개요.txt", "Project Overview.txt"), L("연간 일정.csv", "Annual Schedule.csv"));
            Folder(L("프로젝트", "Projects"), L("2026 신제품 출시", "2026 Product Launch"), L("보도 사진", "Press Photos")); Folder(L("프로젝트", "Projects"), L("2026 신제품 출시", "2026 Product Launch"), L("참고 자료", "References"));
            Files(launch, L("출시 계획.txt", "Launch Plan.txt"), L("예산표.csv", "Budget.csv"), L("회의록 10월.txt", "October Meeting Notes.txt"), L("보도자료 초안.txt", "Press Release Draft.txt"));
            string productPhoto = Path.Combine(launch, L("제품 사진.png", "Product Photo.png"));
            using (var image = new Bitmap(1200, 800))
            {
                using var g = Graphics.FromImage(image);
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 1200, 800),
                    Color.FromArgb(120, 170, 215), Color.FromArgb(225, 236, 246), 35f);
                g.FillRectangle(brush, 0, 0, 1200, 800);
                using var shape = new SolidBrush(Color.FromArgb(240, 250, 252, 255));
                g.FillEllipse(shape, 420, 220, 360, 360);
                image.Save(productPhoto);
            }
            Files(web, L("사이트맵.txt", "Sitemap.txt"), L("화면 설계.txt", "Screen Design.txt"));
            string photos = Folder(L("사진", "Photos"));
            Folder(L("사진", "Photos"), L("제주 여행", "Jeju Trip")); Folder(L("사진", "Photos"), L("가족", "Family")); Folder(L("사진", "Photos"), L("2026 워크숍", "2026 Workshop")); Folder(L("사진", "Photos"), L("스크린샷", "Screenshots"));
            Files(photos, L("앨범 목록.txt", "Album List.txt"));
            string documents = Folder(L("문서", "Documents"));
            Folder(L("문서", "Documents"), L("계약서", "Contracts")); Folder(L("문서", "Documents"), L("세금", "Taxes")); Folder(L("문서", "Documents"), L("보고서", "Reports"));
            Files(documents, L("이력서.txt", "Resume.txt"), L("업무 메모.txt", "Work Notes.txt"), L("주간 보고.txt", "Weekly Report.txt"));
            string downloads = Folder(L("다운로드", "Downloads"));
            Folder(L("다운로드", "Downloads"), L("설치 파일", "Installers"));
            Files(downloads, L("매뉴얼.txt", "Manual.txt"), L("데이터 샘플.csv", "Data Sample.csv"));
            string shared = Folder(L("공유용", "Shared"));
            Folder(L("공유용", "Shared"), L("월별연구계획_20261019", "Monthly_Plan_20261019"));
            Files(shared, L("월별연구계획_20261019_v2.txt", "Monthly_Plan_20261019_v2.txt"));
            string session = Path.Combine(output, "demo-session.json");
            SessionStore.Save(session, new SessionState
            {
                Layout = 4, Language = en ? "en" : "ko", ExplorerFavoritesImported = true, WindowWidth = 1600, WindowHeight = 900,
                Favorites = [projects, photos, documents],
                // Four panes, each with its own folder.
                Panes = [new PaneState { Tabs = [projects, launch], SelectedTab = 1 }, new PaneState { Tabs = [documents] },
                    new PaneState { Tabs = [photos] }, new PaneState { Tabs = [downloads] }]
            });
            using var search = new IndependentSearchBackend(new[] { demoRoot }, Path.Combine(output, "demo-index.fsix"));
            using var form = new MainForm(session, [], search, [demoRoot]);
            foreach (var window in CandidatePopups(form).Prepend<Form>(form))
                SetWindowLongPtr(window.Handle, -20, GetWindowLongPtr(window.Handle, -20) | 0x08000000); // WS_EX_NOACTIVATE: keep the user's focus
            var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
            var shown = panes.Where(p => p.ActiveView is not null).ToArray();
            var navigation = shown.Select(p => p.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(p.ActiveView)).ToArray();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.WhenAll(navigation).WaitAsync(TimeSpan.FromSeconds(20));
                    foreach (var pane in shown) pane.ActiveView!.SetViewModeAndSize(viewMode, viewSize);
                    foreach (var pane in shown) await ItemCountKnown(pane.ActiveView!);
                    await form.FolderTree.FollowPathAsync(launch);
                    // The first English frame still carries the Details header; a throwaway capture settles it.
                    if (en) await CaptureDemo(form, output, "00-warm-up");
                    await CaptureDemo(form, output, "01-light-panes");
                    form.SetColorTheme("dark");
                    await CaptureDemo(form, output, "02-dark-panes");
                    await form.FolderTree.FollowPathAsync(web);
                    await PaneChange(panes[0], () => form.FolderTree.RequestOpen(FolderTreeTarget.NewTab),
                        () => panes[0].TabCount == 3 && panes[0].ActiveView is { HasNavigated: true } && panes[0].CurrentPath == web);
                    panes[0].ActiveView!.SetViewModeAndSize(viewMode, viewSize);
                    await ItemCountKnown(panes[0].ActiveView!);
                    await CaptureDemo(form, output, "03-dark-tree-tab");
                    // Sky blue theme with the details pane showing a photo's properties.
                    form.SetColorTheme("sky");
                    panes[0].SelectTab(1, focus: false);
                    form.SetDetailsPaneVisible(true);
                    var details = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void DetailsChanged(object? sender, EventArgs e) { if (form.Preview.DetailsPath == productPhoto) details.TrySetResult(); }
                    form.Preview.DetailsChanged += DetailsChanged;
                    panes[0].ActiveView!.SelectPaths([productPhoto]);
                    form.UpdatePreview();
                    await details.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    form.Preview.DetailsChanged -= DetailsChanged;
                    await CaptureDemo(form, output, "05-sky-details");
                    form.SetDetailsPaneVisible(false);
                    form.SetPreviewVisible(false);
                    form.SetColorTheme("light");
                    await search.BuildOrLoadAsync(CancellationToken.None);
                    var address = Descendants(panes[1]).OfType<TextBox>().Single(box => box.AccessibleName?.EndsWith(" address") == true);
                    var results = SearchSignal(panes[1], predicate: () => panes[1].SearchCandidates.Count > 0);
                    address.Focus();
                    address.Text = L("공유용/월별연구계획", "shared/monthly");
                    await results.WaitAsync(TimeSpan.FromSeconds(15));
                    await CaptureDemo(form, output, "04-light-search", address);
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProcessPreferredUILanguages(uint flags, string languages, out uint count);
    [DllImport("kernel32.dll")]
    private static extern bool SetThreadLocale(uint locale);
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

    // The Shell leaves the Details column header visible in List and icon views. Its text is drawn in the
    // Windows display language, so an English capture hides it.
    private static void HideDetailsHeaders(MainForm form)
    {
        for (int i = 0; i < 4; i++)
        {
            if (form.GetPane(i).ActiveView is not { IsHandleCreated: true } host) continue;
            nint list = NativeAppearanceList(host);
            nint header = SendMessage(list, 0x101F, 0, 0); // LVM_GETHEADER
            Console.WriteLine($"DEMO_HEADER pane={i + 1} visible={DemoIsVisible(header)} list={DemoIsVisible(list)}");
            DemoShowWindow(header, 0); // SW_HIDE
            DemoRedraw(list, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0100); // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW
        }
    }

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool DemoIsVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool DemoShowWindow(nint window, int command);
    [DllImport("user32.dll", EntryPoint = "RedrawWindow")]
    private static extern bool DemoRedraw(nint window, nint rect, nint region, uint flags);

    private static async Task CaptureDemo(MainForm form, string output, string name, TextBox? anchor = null)
    {
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_DEMO_LANGUAGE") == "en")
        {
            // A view change can show the header again once its queued messages run, so hide it twice.
            HideDetailsHeaders(form);
            await NativeAppearanceDispatch(form);
            HideDetailsHeaders(form);
        }
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
                Check(status.Text.Contains("  |  3 items", StringComparison.Ordinal), "bottom status shows the native item count");
                await ItemCount(host, () => File.WriteAllText(Path.Combine(treeRoot, "b.txt"), "b"), 4);
                await ItemCount(host, () => File.Delete(Path.Combine(treeRoot, "a.txt")), 3);
                Check(status.Text.Contains("  |  3 items", StringComparison.Ordinal), "item count follows created and deleted files");
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
                    && visual.GetColor(3, out int sortColumn) == 0 && sortColumn == ColorTranslator.ToWin32(Theme.ListBack)
                    && visual.GetColor(1, out int listText) == 0 && listText == ColorTranslator.ToWin32(Theme.Text),
                    "dark theme sets the Shell folder view's own text, background and sorted-column colors");
                CaptureLanguage(form, "theme-dark");
                foreach (var (mode, dark) in new[] { ("beige", false), ("green", false), ("dim", true) })
                {
                    form.SetColorTheme(mode);
                    Check(form.ColorTheme == mode && Theme.IsDark == dark && form.BackColor == Theme.Canvas
                        && form.FolderTree.Tree.BackColor == Theme.Canvas
                        && visual.GetColor(2, out int back) == 0 && back == ColorTranslator.ToWin32(Theme.Surface)
                        && visual.GetColor(1, out int fore) == 0 && fore == ColorTranslator.ToWin32(Theme.Text),
                        $"{mode} theme recolors the app and the file list");
                }
                foreach (var (mode, dark) in new[] { ("sky", false), ("pastel", false), ("solarized", false), ("everforest", false), ("gruvbox", false), ("rosepine", false),
                    ("latte", false), ("everforest-dark", true), ("nord", true), ("mocha", true) })
                {
                    form.SetColorTheme(mode);
                    Check(form.ColorTheme == mode && Theme.IsDark == dark && form.FolderTree.Tree.BackColor == Theme.Sidebar
                        && visual.GetColor(2, out int back) == 0 && back == ColorTranslator.ToWin32(Theme.ListBack)
                        && visual.GetColor(3, out int sort) == 0 && sort == ColorTranslator.ToWin32(Theme.ListBack)
                        && visual.GetColor(1, out int fore) == 0 && fore == ColorTranslator.ToWin32(Theme.Text),
                        $"{mode} palette preserves sidebar and list colors with sorted-column highlighting off");
                }
                Check(Theme.Sidebar != Theme.Canvas || Theme.ListBack != Theme.Surface, "mixed palettes use more than one background color");
                var mainToolbar = (ToolStrip)typeof(MainForm).GetField("toolbar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
                Check(ReferenceEquals(mainToolbar.Tag, Theme.OutlineItems), "top toolbar buttons are drawn with a rounded outline");
                var author = Descendants(form).OfType<Label>().Single(label => label.AccessibleName == "Author");
                var authorBounds = form.RectangleToClient(author.RectangleToScreen(author.ClientRectangle));
                Check(author.Visible && author is not LinkLabel && author.Text == "Made by HSoo-Kim (github.com/HSoo-Kim)"
                    && authorBounds.Right >= form.ClientSize.Width - 2 && authorBounds.Bottom >= form.ClientSize.Height - 2 && authorBounds.Left > form.ClientSize.Width / 2
                    && authorBounds.Width > 0 && authorBounds.Height > 0,
                    "the author credit is plain text at the bottom right of the window");
                string ListFace() => ListWindowFace(pane.ActiveView!.Handle);
                string standardFace = SystemFonts.IconTitleFont!.Name;
                form.SetUiFont("Malgun Gothic", "large");
                // GDI+ reports a family by its localized name (맑은 고딕 on Korean Windows).
                string malgun;
                using (var family = new FontFamily("Malgun Gothic")) malgun = family.Name;
                string chosenFace = ListFace();

                Check(form.Font.Name == malgun && Math.Abs(form.Font.SizeInPoints - 10.5f) < 0.01f && (chosenFace == "Malgun Gothic" || chosenFace == malgun),
                    "a chosen font and large text apply to the app and the file list");
                var tabButtons = Descendants(pane).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<FolderTabButton>()).Where(button => button.Available).ToList();
                var plus = Descendants(pane).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>()).Single(item => item.AccessibleName == "Pane 1 new tab");
                Check(tabButtons.Count > 0 && tabButtons.All(button => button.Height >= TextRenderer.MeasureText(button.Text, button.Font).Height + 4)
                    && plus.Height == tabButtons[0].Height, "tabs and the new-tab button grow with a larger font instead of clipping");
                form.SetUiFont(null, "normal");
                string restoredFace = ListFace();
                Check(form.Font.Name == Theme.UiFace && Theme.ListFont is null && restoredFace == standardFace,
                    "the standard font restores the file list's own font");
                form.SetUiFont("Not An Installed Font", "huge");
                Check(form.UiFontFamily is null && form.TextSize == "normal", "an unknown font or size falls back to the standard");
                string sizes = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fs-size-" + Guid.NewGuid().ToString("N"))).FullName;
                try
                {
                    Check(ExplorerPane.FolderFilesSize(sizes, 10) is null, "an empty folder shows no file total");
                    File.WriteAllBytes(Path.Combine(sizes, "a.bin"), new byte[1000]);
                    File.WriteAllBytes(Path.Combine(sizes, "b.bin"), new byte[2000]);
                    Directory.CreateDirectory(Path.Combine(sizes, "sub"));
                    File.WriteAllBytes(Path.Combine(sizes, "sub", "c.bin"), new byte[5000]);
                    Check(ExplorerPane.FolderFilesSize(sizes, 10) == ExplorerPane.FormatSize(3000)
                        && ExplorerPane.FolderFilesSize(sizes, 1) == ExplorerPane.FormatSize(1000) + "+",
                        "the folder total adds this folder's files only, and marks a capped count");
                }
                finally { Directory.Delete(sizes, true); }
                if (ExplorerPane.FolderFilesSize(pane.CurrentPath, 50_000) is { } here)
                {
                    await pane.UpdateFolderSize();
                    Check(status.Text.Contains(here), "the status line shows the current folder's file total");
                }
                form.SetColorTheme("dark");
                await WindowRestored(form, () => PostMessage(form.Handle, 0x0312, 0x4653, 0)); // WM_HOTKEY Win+Alt+E
                Check(form.WindowState != FormWindowState.Minimized, "Win+Alt+E hotkey restores a minimized window");
                await WindowRestored(form, () => PostMessage(form.Handle, QuadExplorer.Program.ActivationMessage(session), 0, 0));
                Check(form.WindowState != FormWindowState.Minimized, "a second launch brings the running window forward");
                form.SetGlobalHotkey(false);
                Check(!form.GlobalHotkeyEnabled, "Win+Alt+E hotkey can be turned off");
                form.SetFolderTreeVisible(false);
                var treeHeader = Descendants(form).OfType<Button>().Single(b => b.AccessibleName == "Expand folder tree");
                Check(!form.FolderTree.Visible && treeHeader.Visible && treeHeader.Text.StartsWith("\u25B8", StringComparison.Ordinal),
                    "folder tree collapses to its sidebar header");
                treeHeader.PerformClick();
                Check(form.FolderTreeVisible && form.FolderTree.Visible && treeHeader.AccessibleName == "Collapse folder tree"
                    && treeHeader.Bottom <= form.FolderTree.Top, "sidebar header expands the folder tree above it");
                var treeSplitter = Descendants(form).OfType<Splitter>().Single(s => s.AccessibleName == "Resize favorites and folder tree");
                var favoritesList = Descendants(form).OfType<ListBox>().Single(l => l.AccessibleName == "Favorites");
                int favoritesBefore = favoritesList.Height;
                int treeBody = (int)Math.Round(200 * form.DeviceDpi / 96d);
                treeSplitter.SplitPosition = treeHeader.Height + treeBody;
                Check(Math.Abs(form.FolderTree.Height - treeBody) <= 1 && favoritesList.Height != favoritesBefore
                    && favoritesList.Parent == treeSplitter.Parent && treeSplitter.Bottom <= form.FolderTree.Parent!.Top + 1,
                    "dragging the sidebar splitter resizes the folder tree against favorites");
                treeHeader.PerformClick();
                Check(!form.FolderTree.Visible, "sidebar header collapses the folder tree again");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        var restored = SessionStore.Load(session);
        Check(restored.ColorTheme == "dark" && !restored.ShowFolderTree, "theme and folder tree visibility persist");
        Check(Math.Abs(restored.FolderTreeHeight - 200) <= 1, "resized folder tree height persists");
        Check(!restored.GlobalHotkey, "Win+Alt+E setting persists");
        RunWinEScenario();
        Task.Run(RunUpdateInstallScenario).GetAwaiter().GetResult();
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

    private static void RunTrayScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "Tray")).FullName;
        string session = Path.Combine(root, "tray.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [folder] }]
        });
        using var form = new MainForm(session, []);
        bool closedToTray = false;
        form.Shown += async (_, _) =>
        {
            try
            {
                Check(form.TrayIconVisible && !form.TrayNoticeShown, "keeping the app in the notification area is the default");
                var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object? sender, EventArgs e) { if (form.Visible) return; hidden.TrySetResult(); }
                form.VisibleChanged += Changed;
                form.Close();
                await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
                form.VisibleChanged -= Changed;
                closedToTray = !form.Visible && !form.IsDisposed;
                Check(closedToTray && form.TrayNoticeShown, "closing the window hides it to the notification area with a one-time notice");
                var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Shown(object? sender, EventArgs e) { if (form.Visible) shown.TrySetResult(); }
                form.VisibleChanged += Shown;
                PostMessage(form.Handle, QuadExplorer.Program.ActivationMessage(session), 0, 0);
                await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
                form.VisibleChanged -= Shown;
                Check(form.Visible, "Win+E or a second launch shows the hidden window again");
                form.DismissFromHotkey();
                Check(!form.Visible && !form.IsDisposed, "pressing Win+Alt+E again hides the window to the notification area");
                var reshown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Reshown(object? sender, EventArgs e) { if (form.Visible) reshown.TrySetResult(); }
                form.VisibleChanged += Reshown;
                PostMessage(form.Handle, 0x0312, 0x4653, 0); // WM_HOTKEY while hidden
                await reshown.Task.WaitAsync(TimeSpan.FromSeconds(10));
                form.VisibleChanged -= Reshown;
                Check(form.Visible, "Win+Alt+E shows the hidden window again");
                form.DismissFromHotkey();
                var toggledIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ToggledIn(object? sender, EventArgs e) { if (form.Visible) toggledIn.TrySetResult(); }
                form.VisibleChanged += ToggledIn;
                PostMessage(form.Handle, QuadExplorer.Program.ActivationMessage(session), 1, 0); // Win+E toggle while hidden
                await toggledIn.Task.WaitAsync(TimeSpan.FromSeconds(10));
                form.VisibleChanged -= ToggledIn;
                Check(form.Visible, "Win+E toggle shows the hidden window");
                form.SetCloseToTray(false);
                form.DismissFromHotkey();
                Check(form.Visible && form.WindowState == FormWindowState.Minimized,
                    "without the notification area, pressing Win+Alt+E again minimizes instead");
                form.SetCloseToTray(true);
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        var saved = SessionStore.Load(session);
        Check(closedToTray && form.IsDisposed && saved.KeepInTray && saved.TrayNoticeShown, "Exit closes the app; tray setting and notice state persist");
    }

    private static void RunPreviewScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "Preview")).FullName;
        string textFile = Path.Combine(folder, "preview-note.txt");
        File.WriteAllText(textFile, string.Join("\r\n", Enumerable.Range(1, 200).Select(i => $"Preview line {i}")));
        string photo = Path.Combine(folder, "preview-photo.png");
        using (var image = new Bitmap(96, 64))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.SteelBlue);
            image.Save(photo, System.Drawing.Imaging.ImageFormat.Png);
        }
        string unknown = Path.Combine(folder, "preview-data.fsxyz");
        File.WriteAllText(unknown, "x");
        string session = Path.Combine(root, "preview.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, ShowPreview = true, PreviewWidth = 420,
            Panes = [new PaneState { Tabs = [folder] }]
        });
        using var form = new MainForm(session, []);
        var text = new UiText("en");
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                if (!pane.ActiveView!.HasNavigated) await NavigationSignal(pane.ActiveView!);
                Check(form.PreviewVisible && form.Preview.Visible && Math.Abs(form.Preview.Width - 420) <= 2,
                    "preview pane opens at its saved width");
                Check(form.Preview.MessageText == text.T("Select a file to preview it."), "preview asks for a file when nothing is selected");
                async Task Select(string file)
                {
                    var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Changed(object? sender, EventArgs e) { if (string.Equals(form.Preview.CurrentPath, file, StringComparison.OrdinalIgnoreCase)) shown.TrySetResult(); }
                    form.Preview.PreviewChanged += Changed;
                    try
                    {
                        await Navigate(pane.ActiveView!, () => pane.ActiveView!.NavigateAndSelect(file));
                        await shown.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    }
                    finally { form.Preview.PreviewChanged -= Changed; }
                }
                await Select(textFile);
                bool textHandler = QuadExplorer.PreviewHandlers.Find(".txt") is not null;
                Check(!textHandler || form.Preview.HasHandler || form.Preview.MessageText == text.T("Could not show a preview of this file."),
                    "selecting a file previews it with the Windows preview handler");
                Console.WriteLine($"METRIC preview_txt_handler_registered={textHandler} handler_loaded={form.Preview.HasHandler}");
                nint document = 0;
                EnumChildWindows(form.Preview.Handle, (window, _) =>
                {
                    var name = new System.Text.StringBuilder(256);
                    GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "RICHEDIT50W") document = window;
                    return true;
                }, 0);
                Check(document != 0, "text preview exposes its native scrollable document");
                if (document != 0)
                {
                    pane.FocusFiles();
                    var point = form.Preview.PointToScreen(new Point(form.Preview.Width / 2, form.Preview.Height / 2));
                    nint coordinates = (nint)((point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16));
                    long before = (long)SendMessage(document, 0xCE, 0, 0); // EM_GETFIRSTVISIBLELINE
                    SendMessage(document, 0xB6, 0, 5); // EM_LINESCROLL
                    Check((long)SendMessage(document, 0xCE, 0, 0) > before, "native preview contains scrollable document content");
                    SendMessage(document, 0xB6, 0, -5);
                    // The Windows handler ignores wheel input on a non-input desktop, even when sent directly.
                    // A native receiver checks the app's delivery contract without replacing the preview handler.
                    using var parent = new Panel { Dock = DockStyle.Fill };
                    using var receiver = new WheelReceiver { Dock = DockStyle.Fill };
                    Control.FromHandle(WheelGetParent(document))!.Controls.Add(parent);
                    parent.Controls.Add(receiver);
                    parent.BringToFront();
                    for (int i = 0; i < 3; i++)
                    {
                        int messageId = i == 2 ? 0x20E : 0x20A;
                        nint wheel = (i == 0 ? -120 << 16 : i == 1 ? 120 << 16 : 60 << 16) | 0x8;
                        var message = Message.Create(pane.ActiveView!.Handle, messageId, wheel, coordinates);
                        bool routed = form.PreFilterMessage(ref message);
                        Check(routed && receiver.Count == i + 1 && receiver.LastMessage.Msg == messageId &&
                            receiver.LastMessage.WParam == wheel && receiver.LastMessage.LParam == coordinates,
                            "wheel over the preview reaches its deepest document child with direction and modifiers intact");
                    }
                    var delivered = Message.Create(document, 0x20A, -120 << 16, coordinates);
                    Check(!form.PreFilterMessage(ref delivered), "wheel already delivered to the document is not sent twice");
                    var list = pane.ActiveView.RectangleToScreen(pane.ActiveView.ClientRectangle);
                    var outside = new Point(list.Left + list.Width / 2, list.Top + list.Height / 2);
                    var listWheel = Message.Create(pane.ActiveView.Handle, 0x20A, -120 << 16,
                        (nint)((outside.X & 0xFFFF) | ((outside.Y & 0xFFFF) << 16)));
                    Check(!form.PreFilterMessage(ref listWheel), "wheel outside the preview keeps its normal destination");
                    Check(SelectedPaths(pane.ActiveView).SequenceEqual(new[] { textFile }, StringComparer.OrdinalIgnoreCase),
                        "scrolling the preview preserves the selected file");
                }
                string? previewed = form.Preview.CurrentPath;
                form.SetPreviewFloating(true);
                var floating = form.FloatingPreview!;
                Check(floating.Visible && floating.Owner == form && form.Preview.FindForm() == floating,
                    "preview detaches into its own owned window");
                Check(form.Preview.CurrentPath == previewed && form.Preview.HasHandler,
                    "detaching preserves the current document");
                form.SetColorTheme("dark");
                Check(floating.BackColor == form.BackColor, "floating preview follows the app theme");
                form.Hide();
                Check(!floating.Visible, "hiding the app also hides its floating preview");
                form.Show();
                Check(floating.Visible, "showing the app restores its floating preview");
                form.TryDockPreview(new Point(int.MinValue, int.MinValue));
                Check(form.FloatingPreview == floating, "dropping outside the main window keeps the preview floating");
                var dockPoint = form.PointToScreen(new Point(form.ClientSize.Width - 15, form.ClientSize.Height / 2));
                form.TryDockPreview(dockPoint);
                Check(form.FloatingPreview is null && form.Preview.FindForm() == form && Math.Abs(form.Preview.Width - 420) <= 2,
                    "dropping on the right side docks the preview at its previous width");
                form.SetPreviewFloating(true);
                floating = form.FloatingPreview!;
                var dock = Descendants(floating).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                    .Single(item => item.AccessibleName == "Dock preview");
                dock.PerformClick();
                Check(form.FloatingPreview is null && floating.IsDisposed, "Dock preview button returns it to the main window");
                form.SetPreviewFloating(true);
                floating = form.FloatingPreview!;
                floating.Close();
                Check(!form.PreviewVisible && !floating.Visible && !form.Preview.HasHandler,
                    "closing the floating window disables and unloads the preview");
                form.SetPreviewVisible(true);
                Check(floating.Visible && form.Preview.HasHandler, "preview can be reopened after closing its floating window");
                form.SetPreviewFloating(false);
                form.SetColorTheme("light");
                await Select(photo);
                Check(form.Preview.HasImage && !form.Preview.HasHandler && form.Preview.MessageText == "",
                    "a photo without a preview handler shows its image");
                await Select(unknown);
                Check(!form.Preview.HasHandler && !form.Preview.HasImage && form.Preview.MessageText == text.T("No preview is available for this file type."),
                    "a file type without a preview handler shows a hint");
                form.Preview.SetPinned(true);
                await Navigate(pane.ActiveView!, () => pane.ActiveView!.NavigateAndSelect(photo));
                form.UpdatePreview();
                Check(form.Preview.Pinned && string.Equals(form.Preview.CurrentPath, unknown, StringComparison.OrdinalIgnoreCase),
                    "a pinned preview keeps its file when another file is selected");
                form.Preview.SetPinned(false);
                Check(string.Equals(form.Preview.CurrentPath, photo, StringComparison.OrdinalIgnoreCase),
                    "unpinning the preview follows the current selection again");
                form.OpenPreviewWindow(textFile);
                var mainPreview = form.Preview.RectangleToScreen(form.Preview.ClientRectangle);
                var firstWindow = form.PreviewWindows[0].FindForm()!.Bounds;
                var work = Screen.FromControl(form).WorkingArea;
                bool room = work.Right - form.Right >= firstWindow.Width || form.Left - work.Left >= firstWindow.Width;
                Check(!room || !firstWindow.IntersectsWith(mainPreview), "a new preview window opens beside the main window, not over its preview");
                form.OpenPreviewWindow(photo);
                Check(form.PreviewWindows.Count == 2 && string.Equals(form.PreviewWindows[0].CurrentPath, textFile, StringComparison.OrdinalIgnoreCase)
                    && form.PreviewWindows[1].HasImage, "files open in their own preview windows");
                Check(form.PreviewWindows[0].FindForm() is { Visible: true } && string.Equals(form.Preview.CurrentPath, photo, StringComparison.OrdinalIgnoreCase),
                    "preview windows show alongside the main preview");
                form.PreviewWindows[0].FindForm()!.Close();
                Check(form.PreviewWindows.Count == 1, "closing a preview window removes only that window");
                form.SetPreviewVisible(false);
                Check(!form.Preview.Visible && !form.Preview.HasHandler, "hiding the preview pane unloads the preview");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        var saved = SessionStore.Load(session);
        Check(!saved.ShowPreview && saved.PreviewWidth == 420, "preview visibility and width persist");
    }

    private static void RunUpdateScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "Update")).FullName;
        string session = Path.Combine(root, "update.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true, Panes = [new PaneState { Tabs = [folder] }]
        });
        var current = typeof(MainForm).Assembly.GetName().Version!;
        var latest = new ReleaseInfo(new Version(current.Major, current.Minor, current.Build), "v-current", "https://github.com/HSoo-Kim/folder-splitter/releases");
        int requests = 0;
        using var form = new MainForm(session, [], latestRelease: _ => { requests++; return Task.FromResult<ReleaseInfo?>(latest); });
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.CheckForUpdateAsync();
                Check(!form.UpdateLinkShown && form.VersionText == "v" + current.ToString(3), "no update notice when the latest release is this version");
                latest = new ReleaseInfo(new Version(99, 0, 0), "v99.0.0", "https://github.com/HSoo-Kim/folder-splitter/releases/tag/v99.0.0");
                await form.CheckForUpdateAsync();
                Check(form.UpdateLinkShown && form.VersionText.StartsWith("v" + current.ToString(3) + " ", StringComparison.Ordinal)
                    && form.VersionText.Contains("New version v99.0.0 available", StringComparison.Ordinal),
                    "a newer release shows a download link next to the version");
                Check(form.AutoUpdateEnabled, "automatic update install is on by default");
                Check(form.UpdateCheckInterval == TimeSpan.FromHours(1), "a running app checks for a new release every hour");
                var activated = typeof(Form).GetMethod("OnActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                int recent = requests;
                activated.Invoke(form, [EventArgs.Empty]);
                Check(requests == recent, "showing the window again within 15 minutes of a check does not query GitHub");
                form.LastUpdateCheck = DateTime.UtcNow - TimeSpan.FromMinutes(16);
                activated.Invoke(form, [EventArgs.Empty]);
                Check(requests == recent + 1, "showing the window again after 15 minutes checks for a new release");
                form.SetAutoUpdate(false);
                form.SetCheckUpdates(false);
                int before = requests;
                await form.CheckForUpdateAsync();
                form.LastUpdateCheck = DateTime.MinValue;
                activated.Invoke(form, [EventArgs.Empty]);
                Check(!form.UpdateLinkShown && requests == before, "turning update checks off hides the notice and stops requests");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        Check(!SessionStore.Load(session).CheckUpdates && !SessionStore.Load(session).AutoUpdate, "update settings persist");
    }

    private static async Task WindowRestored(Form form, Action trigger)
    {
        form.WindowState = FormWindowState.Minimized;
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Resized(object? sender, EventArgs e) { if (form.WindowState != FormWindowState.Minimized) restored.TrySetResult(); }
        form.Resize += Resized;
        try
        {
            trigger();
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { form.Resize -= Resized; }
    }

    private static async Task RunUpdateInstallScenario()
    {
        string root = Path.Combine(Path.GetTempPath(), "FolderSplitter.UpdateTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var json = System.Text.Json.JsonDocument.Parse("""
                {"tag_name":"v9.9.9","html_url":"https://github.com/HSoo-Kim/folder-splitter/releases/tag/v9.9.9","assets":[
                 {"name":"other.zip","browser_download_url":"https://github.com/x/other.zip","digest":"sha256:00"},
                 {"name":"folder-splitter-win-x64.zip","browser_download_url":"https://github.com/HSoo-Kim/folder-splitter/releases/download/v9.9.9/folder-splitter-win-x64.zip",
                  "digest":"sha256:2b1a10366592c46e296ad97e621adc0cde413c24983dae86e3af275bbb2689d7"}]}
                """))
            {
                var parsed = UpdateChecker.Parse(json.RootElement)!;
                Check(parsed.Version == new Version(9, 9, 9) && parsed.PackageSha256 == "2b1a10366592c46e296ad97e621adc0cde413c24983dae86e3af275bbb2689d7"
                    && parsed.PackageUrl?.AbsoluteUri.EndsWith("/v9.9.9/folder-splitter-win-x64.zip", StringComparison.Ordinal) == true,
                    "release package URL and SHA-256 are read from the GitHub release");
            }
            using (var json = System.Text.Json.JsonDocument.Parse("""
                {"tag_name":"v9.9.9","html_url":"https://github.com/HSoo-Kim/folder-splitter/releases/tag/v9.9.9","assets":[
                 {"name":"folder-splitter-win-x64.zip","browser_download_url":"https://example.com/folder-splitter-win-x64.zip","digest":"sha256:2b1a10366592c46e296ad97e621adc0cde413c24983dae86e3af275bbb2689d7"}]}
                """))
                Check(UpdateChecker.Parse(json.RootElement)!.PackageUrl is null, "packages outside github.com are not installed");

            string exe = Path.Combine(root, "FolderSplitter.exe");
            byte[] Package(string? entryName, string content)
            {
                using var memory = new MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    if (entryName is not null)
                        using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write(content);
                    using (var writer = new StreamWriter(zip.CreateEntry("README.md").Open())) writer.Write("readme");
                }
                return memory.ToArray();
            }
            ReleaseInfo Release(byte[] package, string? sha = null) => new(new Version(9, 9, 9), "v9.9.9", "https://github.com/x",
                new Uri("https://github.com/x/folder-splitter-win-x64.zip"), sha ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)));
            Func<Uri, CancellationToken, Task<Stream>> Serve(byte[] package) => (_, _) => Task.FromResult<Stream>(new MemoryStream(package));
            bool Leftovers() => File.Exists(exe + ".new") || File.Exists(exe + ".download");

            File.WriteAllText(exe, "old");
            var good = Package("FolderSplitter.exe", "new");
            await UpdateInstaller.InstallAsync(Release(good), exe, Serve(good), CancellationToken.None);
            Check(File.ReadAllText(exe) == "new" && File.ReadAllText(exe + ".old") == "old" && !Leftovers(),
                "update replaces the executable and keeps the previous one aside");
            UpdateInstaller.RemovePrevious(exe);
            Check(!File.Exists(exe + ".old"), "the previous executable is removed on the next start");

            File.WriteAllText(exe, "old");
            bool rejected = false;
            try { await UpdateInstaller.InstallAsync(Release(good, new string('0', 64)), exe, Serve(good), CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(exe) == "old" && !File.Exists(exe + ".old") && !Leftovers(),
                "a package with the wrong SHA-256 is rejected and nothing changes");

            var empty = Package(null, "");
            rejected = false;
            try { await UpdateInstaller.InstallAsync(Release(empty), exe, Serve(empty), CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(exe) == "old" && !File.Exists(exe + ".old") && !Leftovers(),
                "a package without FolderSplitter.exe is rejected and nothing changes");
        }
        finally { Directory.Delete(root, true); }
    }

    // Uses a throwaway registry root so the user's real Win+E setting is never touched.
    private static void RunWinEScenario()
    {
        string rootPath = @"Software\FolderSplitterTests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath);
            var shortcut = new WinEShortcut(root, @"C:\Apps\Folder Splitter\FolderSplitter.exe");
            Check(!shortcut.IsEnabled, "Win+E replacement is off by default");
            shortcut.Enable();
            using (var command = root.OpenSubKey(WinEShortcut.ClassKey + @"\shell\opennewwindow\command"))
                Check(shortcut.IsEnabled && command?.GetValue("") as string == "\"C:\\Apps\\Folder Splitter\\FolderSplitter.exe\" --toggle"
                    && command.GetValue("DelegateExecute") as string == "", "Win+E replacement registers the quoted executable");
            using (var legacy = root.CreateSubKey(WinEShortcut.ClassKey + @"\shell\opennewwindow\command"))
                legacy.SetValue("", "\"C:\\Apps\\Folder Splitter\\FolderSplitter.exe\"");
            Check(shortcut.IsEnabled, "a Win+E entry from an earlier version still counts as on");
            shortcut.UpgradeLegacy();
            using (var command = root.OpenSubKey(WinEShortcut.ClassKey + @"\shell\opennewwindow\command"))
                Check(command?.GetValue("") as string == "\"C:\\Apps\\Folder Splitter\\FolderSplitter.exe\" --toggle",
                    "an earlier Win+E entry is upgraded so Win+E can hide the window");
            shortcut.Disable();
            Check(!shortcut.IsEnabled && root.OpenSubKey(WinEShortcut.ClassKey) is null, "turning Win+E off removes only its own keys");
            using (var icon = root.CreateSubKey(WinEShortcut.ClassKey + @"\DefaultIcon")) icon.SetValue("", "kept");
            shortcut.Enable();
            shortcut.Disable();
            using (var icon = root.OpenSubKey(WinEShortcut.ClassKey + @"\DefaultIcon"))
                Check(icon?.GetValue("") as string == "kept" && !shortcut.IsEnabled, "turning Win+E off keeps other per-user CLSID settings");
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false);
            using var parent = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\FolderSplitterTests");
            if (parent is { SubKeyCount: 0 }) Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\FolderSplitterTests", false);
        }
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
                Check(form.DockTab(source, source.ActiveTab!, 0, SplitEdge.Right)
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
                var version = Descendants(form).OfType<Label>().Single(l => l.AccessibleName == "Application version");
                var versionBounds = form.RectangleToClient(version.RectangleToScreen(version.ClientRectangle));
                Check(version.Visible && version.Text == "v" + typeof(MainForm).Assembly.GetName().Version!.ToString(3)
                    && versionBounds.Bottom == form.ClientRectangle.Bottom && versionBounds.Left == 0 && version.Height >= version.PreferredHeight,
                    "lower-left version matches running assembly and remains visible with sidebar hidden");
                form.SetSidebarVisible(true);
                Check(form.SidebarWidth == 320, "quick access restores its resized width");
                var favoriteList = Descendants(form).OfType<ListBox>().Single(list => list.AccessibleName == "Favorites");
                favoriteList.Update();
                form.SetSidebarWidth(260);
                Check(GetUpdateRect(favoriteList.Handle, 0, false), "narrowing quick access repaints the favorites list so no old name tails remain");
                form.SetSidebarWidth(320);
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
                var arrangements = toolbar.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Three pane arrangements");
                Check(arrangements.Bounds.Width >= TextRenderer.MeasureText(arrangements.Text, arrangements.Font).Width,
                    "the three-pane arrangement arrow is as wide as its text");
                Check(Descendants(form).OfType<TextBox>().All(t => t.Height >= t.PreferredHeight),
                    "address fields fit their preferred font height");
                Check(Descendants(form).OfType<Label>().Where(l => l.AccessibleName?.EndsWith(" status") == true)
                    .All(l => l.Height >= l.PreferredHeight), "status labels fit their preferred font height");
                Check(Descendants(form).OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<Button>())
                    .All(b => b.Width >= b.GetPreferredSize(Size.Empty).Width),
                    "navigation icon columns fit their preferred glyph width");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
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
                    var address = Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName?.EndsWith(" address") == true);
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
                        string expectedStatus = active.DisplayName + "  |  " + text.T("Pane {0}  |  {1} tabs", 1, 2) + CountSuffix(text, active);
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
                        == active.DisplayName + "  |  " + new UiText("zh-CN").T("Folder path copied.") + CountSuffix(new UiText("zh-CN"), active),
                        "existing action status refreshes on language switch");
                    selector.SelectedIndex = 3;
                    await Navigate(active, () => active.Travel(0x8000));
                    Check(pane.CurrentPath == Path.Combine(folders[0], "한글 폴더"),
                        "native forward history survives all language switches");
                    ValidateTranslations();
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
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
                var values = Enumerable.Range(1, System.Text.CompositeFormat.Parse(key).MinimumArgumentCount)
                    .Select(value => (object)value).ToArray();
                string translated = text.T(key, values);
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
        var theme = items.Single(i => i.AccessibleName == "Color theme");
        Check(new[] { theme, label, selector, close, help }.All(i =>
                i.Alignment == ToolStripItemAlignment.Right && i.Placement == ToolStripItemPlacement.Main)
            && theme.Bounds.Right <= label.Bounds.Left && label.Bounds.Right <= selector.Bounds.Left && selector.Bounds.Right <= close.Bounds.Left
            && close.Bounds.Right <= help.Bounds.Left,
            name + " right group orders theme, language, close tab and shortcuts");
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int length);

    [DllImport("user32.dll", EntryPoint = "GetParent")]
    private static extern nint WheelGetParent(nint window);

    private sealed class WheelReceiver : Control
    {
        internal int Count { get; private set; }
        internal Message LastMessage { get; private set; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x20A or 0x20E)
            {
                Count++;
                LastMessage = message;
                return;
            }
            base.WndProc(ref message);
        }
    }

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
        var address = Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName?.EndsWith(" address") == true);
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
                // Switching language with candidates open searches again; wait for that search, not a new edit.
                var relocalized = SearchSignal(pane);
                form.SetLanguage("ja");
                Check(form.Language == "ja", "language switch with candidates open does not throw");
                await relocalized.WaitAsync(TimeSpan.FromSeconds(15));
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
            finally { backend.Release(); form.ExitApplication(); }
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

    private static Task SelectionSignal(ShellViewHost host)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? settled = null;
        settled = (_, _) => { host.SelectionSettled -= settled; source.TrySetResult(); };
        host.SelectionSettled += settled;
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
