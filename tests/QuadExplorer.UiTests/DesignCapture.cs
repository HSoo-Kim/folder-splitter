using QuadExplorer;

internal static partial class Program
{
    // Renders the window chrome (toolbar, sidebar, tabs, command bar, status) in a few themes for design review.
    // Shell file lists are hosted native views and stay blank in these captures.
    private static int RunDesignCapture(string output)
    {
        Directory.CreateDirectory(output);
        string root = Path.Combine(Path.GetTempPath(), "FolderSplitter-Design-" + Guid.NewGuid().ToString("N"));
        try
        {
            string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([root, .. parts])).FullName;
            string research = Folder("Research"), proposal = Folder("2026_첨단모빌리티 연구과제 제안서 최종본"),
                report = Folder("2026-2029_국가연구개발사업 연차보고서"), documents = Folder("문서"), photos = Folder("사진");
            File.WriteAllText(Path.Combine(research, "notes.txt"), "design capture");
            string session = Path.Combine(root, "design.json");
            SessionStore.Save(session, new SessionState
            {
                Layout = 2, Language = "ko", ExplorerFavoritesImported = true, WindowWidth = 1500, WindowHeight = 900, ShowPreview = false,
                Favorites = [proposal, report, research, documents, photos],
                Panes = [new PaneState { Tabs = [research, proposal, documents], SelectedTab = 0 }, new PaneState { Tabs = [photos] },
                    new PaneState(), new PaneState()]
            });
            using var form = new MainForm(session, []);
            form.Shown += async (_, _) =>
            {
                try
                {
                    foreach (var pane in new[] { form.GetPane(0), form.GetPane(1) })
                        if (pane.ActiveView is { HasNavigated: false } view) await NavigationSignal(view).WaitAsync(TimeSpan.FromSeconds(20));
                    foreach (string mode in (Environment.GetEnvironmentVariable("FOLDERSPLITTER_DESIGN_THEMES") ?? "light,dark,sky,beige").Split(','))
                    {
                        form.SetColorTheme(mode);
                        form.Refresh();
                        using var shot = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(shot, new Rectangle(Point.Empty, form.Size));
                        shot.Save(Path.Combine(output, $"design-{mode}.png"));
                        Console.WriteLine("SCREENSHOT design-" + mode);
                    }
                    // Hover states: an idle tab, a command and a top toolbar button under the pointer.
                    form.SetColorTheme("light");
                    var first = form.GetPane(0);
                    first.TabButtons[1].Select();
                    Descendants(first).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                        .Single(item => item.AccessibleName == "Pane 1 New folder").Select();
                    form.Controls.OfType<ToolStrip>().Single().Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Equal size").Select();
                    form.Refresh();
                    using (var hover = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(hover, new Rectangle(Point.Empty, form.Size));
                        hover.Save(Path.Combine(output, "design-hover.png"));
                    }
                }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        Console.WriteLine(failures == 0 ? "DESIGN_CAPTURE_DONE" : "DESIGN_CAPTURE_FAILED");
        return failures == 0 ? 0 : 1;
    }
}
