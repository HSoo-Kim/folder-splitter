using QuadExplorer;
using System.Runtime.InteropServices;

internal static partial class Program
{
    // Recent items list the targets of the shortcuts Windows keeps in the Recent folder, so a drag or copy
    // hands over the file itself. A scratch folder stands in for the user's Recent folder.
    private static void RunRecentItemsScenario(string root)
    {
        string targets = Directory.CreateDirectory(Path.Combine(root, "RecentTargets")).FullName;
        string recent = Directory.CreateDirectory(Path.Combine(root, "Recent")).FullName;
        string folder = Directory.CreateDirectory(Path.Combine(targets, "Folder C")).FullName;
        string report = Path.Combine(targets, "report.pdf");
        string notes = Path.Combine(targets, "notes.txt");
        string gone = Path.Combine(targets, "gone.txt");
        string later = Path.Combine(targets, "later.txt");
        string latest = Path.Combine(targets, "latest.txt");
        foreach (string file in new[] { report, notes, gone, later, latest }) File.WriteAllText(file, Path.GetFileName(file));
        var stamp = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Local);
        Shortcut(recent, "notes.txt.lnk", notes, stamp.AddMinutes(1));
        Shortcut(recent, "report (older).lnk", report, stamp);
        Shortcut(recent, "report.pdf.lnk", report, stamp.AddMinutes(2));
        Shortcut(recent, "Folder C.lnk", folder, stamp.AddMinutes(3));
        Shortcut(recent, "gone.txt.lnk", gone, stamp.AddMinutes(4));
        File.Delete(gone);
        // Damaged shortcuts, newer than the rest, are skipped without disturbing the list.
        byte[] sound = File.ReadAllBytes(Path.Combine(recent, "notes.txt.lnk"));
        int linkInfo = 0x4E + BitConverter.ToUInt16(sound, 0x4C);
        byte[] oversized = (byte[])sound.Clone();
        BitConverter.GetBytes(0x7FFFFFF0).CopyTo(oversized, linkInfo); // LinkInfoSize
        BitConverter.GetBytes(0x24).CopyTo(oversized, linkInfo + 4); // a header with Unicode offsets
        BitConverter.GetBytes(0x7FFFFF00).CopyTo(oversized, linkInfo + 0x1C);
        BitConverter.GetBytes(0x7FFFFF00).CopyTo(oversized, linkInfo + 0x20);
        File.WriteAllBytes(Path.Combine(recent, "oversized.lnk"), oversized);
        File.WriteAllBytes(Path.Combine(recent, "truncated.lnk"), sound[..(linkInfo + 0x1E)]);
        foreach (string damaged in new[] { "oversized.lnk", "truncated.lnk" }) File.SetLastWriteTime(Path.Combine(recent, damaged), stamp.AddMinutes(5));
        string[] expected = [folder, report, notes];

        string previousRecent = ShellNative.Recent;
        ShellNative.Recent = recent;
        string session = Path.Combine(root, "recent.json");
        try
        {
            SessionStore.Save(session, new SessionState
            {
                Layout = 1, Language = "en", ExplorerFavoritesImported = true, ShowPreview = false,
                Panes = [new PaneState { Tabs = [targets] }]
            });
            RunRecentForm(session, async (pane, host) =>
            {
                await Navigate(host, () => pane.Navigate(recent));
                await ItemCount(host, expected.Length);
                Check(ShellViewHost.IsRecent(pane.CurrentPath) && host.DisplayName == "Recent items",
                    "recent items keep the Recent folder as their location and title");
                Check(ListedPaths(host).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase),
                    "recent items hand over each target once, most recently opened first, without deleted files");
                Check(!HasNativeDropTarget(host), "nothing can be dropped onto the recent list");

                host.SetFilter("report");
                await ItemCount(host, 1);
                Check(ListedPaths(host).SequenceEqual([report], StringComparer.OrdinalIgnoreCase), "the quick filter narrows the recent list");
                host.SetFilter("");
                await ItemCount(host, expected.Length);

                Shortcut(recent, "later.txt.lnk", later, stamp.AddMinutes(10));
                await ItemCount(host, expected.Length + 1);
                Check(ListedPaths(host).First().Equals(later, StringComparison.OrdinalIgnoreCase),
                    "a newly opened file appears at the top of an open recent list");

                var view = FolderView(host);
                Marshal.ThrowExceptionForHR(view.SelectItem(1, 0x5)); // SVSI_SELECT | SVSI_DESELECTOTHERS
                Shortcut(recent, "latest.txt.lnk", latest, stamp.AddMinutes(11));
                await Task.Delay(1500);
                Check(host.ItemCount == expected.Length + 1, "the recent list does not reorder while something is selected");
                view.SelectItem(1, 0); // SVSI_DESELECT
                await ItemCount(host, expected.Length + 2);
                Check(ListedPaths(host).First().Equals(latest, StringComparison.OrdinalIgnoreCase),
                    "the recent list catches up once nothing is selected");

                int folderRow = Array.FindIndex(ListedPaths(host), path => path.Equals(folder, StringComparison.OrdinalIgnoreCase));
                Marshal.ThrowExceptionForHR(view.SelectItem(folderRow, 0x5));
                await Navigate(host, () => host.InvokeSelectionVerb("open"));
                Check(pane.CurrentPath.Equals(folder, StringComparison.OrdinalIgnoreCase), "opening a recent folder shows it in the same pane");
                await Navigate(host, () => host.Travel(0x4000));
                await ItemCount(host, expected.Length + 2);
                Check(ShellViewHost.IsRecent(pane.CurrentPath), "back returns to the recent list");

                await Navigate(host, () => host.Travel(0x2000));
                Check(pane.CurrentPath.Equals(root, StringComparison.OrdinalIgnoreCase), "up from the recent list opens the folder holding the Recent folder");
                host.SelectPaths([recent]);
                await Navigate(host, () => host.InvokeSelectionVerb("open"));
                await ItemCount(host, expected.Length + 2);
                Check(ShellViewHost.IsRecent(pane.CurrentPath) && ListedPaths(host).First().Equals(latest, StringComparison.OrdinalIgnoreCase),
                    "opening the Recent folder inside a pane shows the recent list");
                await Navigate(host, () => host.Travel(0x4000));
                Check(pane.CurrentPath.Equals(root, StringComparison.OrdinalIgnoreCase), "back from there returns to the folder before it");

                // Only reads the user's Recent folder: shell:recent always names the real one.
                ShellNative.Recent = previousRecent;
                await Navigate(host, () => pane.Navigate("shell:recent"));
                for (int i = 0; i < 400 && host.ItemCount is null; i++) await Task.Delay(25);
                Check(ShellViewHost.IsRecent(pane.CurrentPath) && host.DisplayName == "Recent items" && host.ItemCount is not null,
                    "shell:recent opens the recent list");
                ShellNative.Recent = recent;
                await Navigate(host, () => pane.Navigate(recent));
                await ItemCount(host, expected.Length + 2);
            });
            Check(ShellViewHost.IsRecent(SessionStore.Load(session).Panes[0].Tabs[0]), "a recent items tab is saved as recent items");
            RunRecentForm(session, async (pane, host) =>
            {
                await ItemCount(host, expected.Length + 2);
                Check(ListedPaths(host).First().Equals(latest, StringComparison.OrdinalIgnoreCase), "a restored recent items tab lists the targets");
            });
        }
        finally { ShellNative.Recent = previousRecent; }
    }

    private static void RunRecentForm(string session, Func<ExplorerPane, ShellViewHost, Task> steps)
    {
        using var form = new MainForm(session, []);
        var pane = Descendants(form).OfType<ExplorerPane>().OrderBy(p => p.PaneNumber).First();
        var initial = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView!);
        form.Shown += async (_, _) =>
        {
            try
            {
                await initial.WaitAsync(TimeSpan.FromSeconds(20));
                await steps(pane, pane.ActiveView!);
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
    }

    private static void Shortcut(string folder, string name, string target, DateTime opened)
    {
        string path = Path.Combine(folder, name);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.Save();
            Marshal.ReleaseComObject(link);
        }
        finally { Marshal.ReleaseComObject(shell); }
        File.SetLastWriteTime(path, opened);
    }

    private static async Task ItemCount(ShellViewHost host, int count)
    {
        for (int i = 0; i < 200; i++)
        {
            if (host.ItemCount == count) return;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Expected {count} items, the view shows {host.ItemCount?.ToString() ?? "none"}");
    }

    private static IFolderView FolderView(ShellViewHost host) => (IFolderView)typeof(ShellViewHost)
        .GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(host)!;

    // The file-system path each row hands to a drag or copy, in display order. Leaves nothing selected.
    private static string[] ListedPaths(ShellViewHost host)
    {
        var view = FolderView(host);
        Marshal.ThrowExceptionForHR(view.ItemCount(ShellNative.SvgioAllView, out int count));
        var paths = new string[count];
        for (int i = 0; i < count; i++)
        {
            Marshal.ThrowExceptionForHR(view.SelectItem(i, 0x5)); // SVSI_SELECT | SVSI_DESELECTOTHERS
            paths[i] = SelectedPaths(host).Single();
            view.SelectItem(i, 0); // SVSI_DESELECT
        }
        return paths;
    }
}
