using System.Diagnostics;
using System.Runtime.InteropServices;
using Accessibility;
using Microsoft.Win32;
using QuadExplorer;

internal static partial class Program
{
    private sealed record PublishedAccessible(IAccessible Object, int Child, string Name);

    private static int RunPublishedGroupSmoke(string executable, string output)
    {
        Directory.CreateDirectory(output);
        string session = Path.Combine(output, "published-groups-session.json");
        string first = Directory.CreateDirectory(Path.Combine(output, "Published-first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(output, "Published-second")).FullName;
        string savedPath = Directory.CreateDirectory(Path.Combine(output, "Published-saved")).FullName;
        const string savedId = "published-saved-group";
        SessionStore.Save(session, new SessionState
        {
            Language = "en", StartWithWindows = true, GlobalHotkey = false, CheckUpdates = false,
            AutoRestartMemory = 0, KeepInTray = false, ExplorerFavoritesImported = true,
            SavedGroups = [new() { Id = savedId, Name = "Published Saved", Color = "Green", Paths = [savedPath] }],
            Windows = [new() { Panes = [new() { Tabs = [first], Groups = [new() { Name = "Smoke first", Color = "Blue", Count = 1 }] }],
                    WindowX = 20, WindowY = 20, ShowFolderTree = false },
                new() { Panes = [new() { Tabs = [second], Groups = [new() { Name = "Smoke second", Color = "Purple", Count = 1 }] }],
                    WindowX = 60, WindowY = 60, ShowFolderTree = false }]
        });
        (RegistryValueKind? Kind, string? Value) RunValue()
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKey, writable: false);
            if (key is null || !key.GetValueNames().Contains(StartupRegistration.ValueName, StringComparer.OrdinalIgnoreCase))
                return (null, null);
            return (key.GetValueKind(StartupRegistration.ValueName), System.Text.Json.JsonSerializer.Serialize(
                key.GetValue(StartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
        }
        var originalRun = RunValue();
        try
        {
            for (int launch = 1; launch <= 2; launch++)
            {
                var info = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
                info.ArgumentList.Add("--session");
                info.ArgumentList.Add(session);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Published process did not start.");
                try
                {
                    List<nint> windows = [];
                    PublishedWait(() => { windows = PublishedWindows(process.Id, browsersOnly: true); return windows.Count == 2; },
                        "two published browser windows");
                    Check(windows.Count == 2, $"published group smoke launch {launch} shows both independent browser HWNDs");
                    PublishedWait(() => windows.SelectMany(PublishedNames).Any(item => item.Name.Contains("Smoke first", StringComparison.Ordinal))
                        && windows.SelectMany(PublishedNames).Any(item => item.Name.Contains("Smoke second", StringComparison.Ordinal)),
                        "both published group chips");
                    Check(windows.Count(window => PublishedNames(window).Any(item => item.Name.Contains("Smoke first", StringComparison.Ordinal))) == 1
                        && windows.Count(window => PublishedNames(window).Any(item => item.Name.Contains("Smoke second", StringComparison.Ordinal))) == 1,
                        $"published group smoke launch {launch} exposes each restored named chip in exactly its own window");
                    PublishedAccessible? savedItem = null;
                    PublishedWait(() =>
                    {
                        savedItem = windows.SelectMany(PublishedNames).FirstOrDefault(item => item.Name == "Published Saved (1 tabs)");
                        return savedItem is not null && windows.SelectMany(PublishedNames).Any(item => item.Name == "Saved groups");
                    }, "published Saved groups sidebar");
                    Check(savedItem is not null, $"published saved group smoke launch {launch} shows the saved entry in the sidebar");
                    nint savedList = PublishedSavedGroupList(windows);
                    var row = new NativeRect();
                    NativeAppearanceRectMessage(savedList, 0x0198, 0, ref row); // LB_GETITEMRECT
                    nint click = (nint)(((row.Top + (row.Bottom - row.Top) / 2) << 16) | ((row.Left + 10) & 0xffff));
                    // Native clicks update ListBox's managed selection before its consumer MouseClick command.
                    PostMessage(savedList, 0x0201, 1, click);
                    PostMessage(savedList, 0x0202, 0, click);
                    PublishedWait(() => windows.SelectMany(PublishedNames)
                        .Any(item => item.Name.Contains("Tab group: Published Saved,", StringComparison.Ordinal)),
                        "saved sidebar entry opening its group");
                    Check(windows.Count(window => PublishedNames(window).Any(item =>
                        item.Name.Contains("Tab group: Published Saved,", StringComparison.Ordinal))) == 1,
                        $"published saved group smoke launch {launch} opens or focuses exactly one linked group");
                    var settings = windows.SelectMany(PublishedNames).First(item => item.Name == "Settings");
                    settings.Object.accDoDefaultAction(settings.Child);
                    PublishedAccessible? exit = null;
                    PublishedWait(() =>
                    {
                        exit = PublishedWindows(process.Id, browsersOnly: false).SelectMany(PublishedNames).FirstOrDefault(item => item.Name == "Exit");
                        return exit is not null;
                    }, "published Settings Exit menu");
                    exit!.Object.accDoDefaultAction(exit.Child);
                    Check(process.WaitForExit(30_000) && process.ExitCode == 0,
                        $"published group smoke launch {launch} exits through the real Settings menu without killing a browser");
                    var saved = SessionStore.Load(session);
                    Check(saved.Windows.Count == 2 && saved.Windows[0].Panes[0].Tabs.Contains(first)
                        && saved.Windows[1].Panes[0].Tabs.Contains(second)
                        && saved.Windows[0].Panes[0].Groups.Any(group => group is { Name: "Smoke first", Color: "Blue", Count: 1 })
                        && saved.Windows[1].Panes[0].Groups.Any(group => group is { Name: "Smoke second", Color: "Purple", Count: 1 }),
                        $"published group smoke launch {launch} saves both windows and their group metadata for relaunch");
                    Check(saved.SavedGroups.Single() is { Id: savedId, Name: "Published Saved", Color: "Green" } entry
                        && entry.Paths.SequenceEqual([savedPath])
                        && saved.Windows.SelectMany(window => window.Panes).SelectMany(pane => pane.Groups)
                            .Single(group => group.SavedId == savedId).Name == "Published Saved",
                        $"published saved group smoke launch {launch} retains the saved catalog and stable open link for relaunch");
                }
                finally
                {
                    // Only the process created above belongs to this smoke; never discover or stop a user's instance.
                    if (!process.HasExited)
                    {
                        foreach (var window in PublishedWindows(process.Id, browsersOnly: false))
                            Console.WriteLine($"PUBLISHED_ACCESS window=0x{window:X} names=" +
                                string.Join(" | ", PublishedNames(window).Select(item => item.Name)));
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
            }
            Check(originalRun == RunValue(), "published group smoke leaves the real Run registration byte-for-byte unchanged");
        }
        catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL published group smoke " + exception); }
        finally
        {
            if (originalRun != RunValue()) { failures++; Console.Error.WriteLine("FAIL published smoke changed the real Run value"); }
        }
        Console.WriteLine($"PUBLISHED_GROUP_SMOKE_RESULT checks={checks} failures={failures}");
        return failures == 0 ? 0 : 1;
    }

    private static void PublishedWait(Func<bool> ready, string description)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(40))
        {
            if (ready()) return;
            Application.DoEvents();
            Thread.Sleep(100);
        }
        throw new TimeoutException("Timed out waiting for " + description);
    }

    private static List<nint> PublishedWindows(int process, bool browsersOnly)
    {
        List<nint> windows = [];
        PublishedEnumWindows((window, _) =>
        {
            ScrollWindowThread(window, out uint owner);
            if (owner != process || !DemoIsVisible(window)) return true;
            var title = new System.Text.StringBuilder(512);
            GetCloseWarningText(window, title, title.Capacity);
            if (!browsersOnly || title.ToString() == "Folder Splitter") windows.Add(window);
            return true;
        }, 0);
        return windows;
    }

    private static IEnumerable<PublishedAccessible> PublishedNames(nint window)
    {
        var items = new List<PublishedAccessible>();
        void Visit(IAccessible accessible, int depth)
        {
            if (depth > 24 || items.Count > 4000) return;
            try
            {
                string name = accessible.get_accName(0) ?? "";
                items.Add(new(accessible, 0, name));
                int count = Math.Clamp(accessible.accChildCount, 0, 4000 - items.Count);
                if (count == 0) return;
                var children = new object[count];
                Marshal.ThrowExceptionForHR(PublishedAccessibleChildren(accessible, 0, count, children, out int obtained));
                for (int index = 0; index < obtained; index++)
                {
                    if (children[index] is IAccessible nested) Visit(nested, depth + 1);
                    else if (children[index] is int child)
                        items.Add(new(accessible, child, accessible.get_accName(child) ?? ""));
                }
            }
            catch (COMException) { }
            catch (ArgumentException) { }
        }
        void Collect(nint handle)
        {
            Guid id = typeof(IAccessible).GUID;
            if (PublishedAccessibleFromWindow(handle, 0xFFFFFFFC, ref id, out var accessible) >= 0 && accessible is not null)
                Visit(accessible, 0);
        }
        Collect(window);
        EnumChildWindows(window, (child, _) => { Collect(child); return true; }, 0);
        return items;
    }

    private static nint PublishedSavedGroupList(IEnumerable<nint> windows)
    {
        foreach (nint window in windows)
        {
            nint result = 0;
            EnumChildWindows(window, (child, _) =>
            {
                var name = new System.Text.StringBuilder(256);
                GetClassName(child, name, name.Capacity);
                if (name.ToString().Contains("LISTBOX", StringComparison.OrdinalIgnoreCase)
                    && PublishedNames(child).Any(item => item.Name == "Saved groups")) result = child;
                return result == 0;
            }, 0);
            if (result != 0) return result;
        }
        throw new InvalidOperationException("The published Saved groups list HWND was not found.");
    }

    private delegate bool PublishedWindowCallback(nint window, nint data);
    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    private static extern bool PublishedEnumWindows(PublishedWindowCallback callback, nint data);
    [DllImport("oleacc.dll", EntryPoint = "AccessibleObjectFromWindow")]
    private static extern int PublishedAccessibleFromWindow(nint window, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("oleacc.dll", EntryPoint = "AccessibleChildren")]
    private static extern int PublishedAccessibleChildren([MarshalAs(UnmanagedType.Interface)] IAccessible accessible,
        int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);
}
