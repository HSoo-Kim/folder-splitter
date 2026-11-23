using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using QuadExplorer;

internal static partial class Program
{
    private const string FoExecutable = @"C:\Apps\Folder Splitter\FolderSplitter.exe";

    private static void RunFolderOpenScenario(string root)
    {
        RunFolderOpenRegistrationScenario();
        RunFolderMenuVerbChecks();
        RunFolderHandoffScenario(root);
        RunFolderActivationScenario(root);
    }

    // Uses a throwaway HKCU root, so the user's real folder associations are never read or changed.
    private static void RunFolderOpenRegistrationScenario()
    {
        string rootPath = @"Software\FolderSplitterTests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(rootPath);
            var registration = new FolderOpenRegistration(root, FoExecutable);
            string? Value(string path, string name = "") { using var key = root.OpenSubKey(path); return key?.GetValue(name) as string; }
            Check(!registration.MenuEnabled && !registration.DefaultEnabled, "folder opening registration is off by default");
            Check(registration.Command("Directory") == $"\"{FoExecutable}\" \"%1\"" && registration.Command("Drive") == $"\"{FoExecutable}\" %1",
                "folder paths are quoted and drive roots stay unquoted so C:\\ is not read as an escaped quote");

            registration.Apply(menu: true, openByDefault: false, "Open in Folder Splitter");
            foreach (string kind in new[] { "Directory", "Drive" })
                Check(Value(FolderOpenRegistration.VerbKey(kind), "MUIVerb") == "Open in Folder Splitter"
                    && Value(FolderOpenRegistration.VerbKey(kind), "Icon") == $"\"{FoExecutable}\",0"
                    && Value(FolderOpenRegistration.VerbKey(kind) + @"\command") == registration.Command(kind)
                    && Value(FolderOpenRegistration.ShellKey(kind)) is null,
                    $"the {kind} right-click verb is registered without changing the default");
            Check(registration.MenuEnabled && !registration.DefaultEnabled, "the right-click setting reads as on and the default as off");

            using (var other = root.CreateSubKey(FolderOpenRegistration.ShellKey("Directory") + @"\OtherTool\command"))
                other.SetValue("", "other.exe \"%1\"");
            using (var shell = root.CreateSubKey(FolderOpenRegistration.ShellKey("Directory"))) shell.SetValue("", "OtherTool");
            registration.Apply(menu: true, openByDefault: true, "Open in Folder Splitter");
            Check(registration.DefaultEnabled && Value(FolderOpenRegistration.ShellKey("Directory")) == FolderOpenRegistration.Verb
                && Value(FolderOpenRegistration.ShellKey("Drive")) == FolderOpenRegistration.Verb,
                "opening by default makes the verb the folder and drive default");
            using (var classes = root.OpenSubKey(@"Software\Classes")!)
                Check(ShellViewHost.FolderVerbOpensElsewhere(classes, Path.GetTempPath())
                    && ShellViewHost.FolderVerbOpensElsewhere(classes, @"C:\"),
                    "the file list detects that folders and drives no longer open in place");
            registration.UpdateLabel("Folder Splitter로 열기");
            Check(Value(FolderOpenRegistration.VerbKey("Directory"), "MUIVerb") == "Folder Splitter로 열기"
                && Value(FolderOpenRegistration.VerbKey("Drive"), "MUIVerb") == "Folder Splitter로 열기",
                "a language change relabels both registered verbs");

            registration.Apply(menu: true, openByDefault: false, "Open in Folder Splitter");
            Check(registration.MenuEnabled && !registration.DefaultEnabled
                && Value(FolderOpenRegistration.ShellKey("Directory")) == "OtherTool" && Value(FolderOpenRegistration.ShellKey("Drive")) is null,
                "turning the default off restores another program's default and keeps the right-click verb");

            registration.Apply(menu: true, openByDefault: true, "Open in Folder Splitter");
            registration.Apply(menu: false, openByDefault: false, "Open in Folder Splitter");
            Check(!registration.MenuEnabled && !registration.DefaultEnabled
                && root.OpenSubKey(FolderOpenRegistration.VerbKey("Directory")) is null
                && Value(FolderOpenRegistration.ShellKey("Directory")) == "OtherTool"
                && Value(FolderOpenRegistration.ShellKey("Directory") + @"\OtherTool\command") == "other.exe \"%1\""
                && root.OpenSubKey(@"Software\Classes\Drive") is null,
                "turning both off removes only Folder Splitter's keys and restores the previous default");
            registration.UpdateLabel("Folder Splitter로 열기");
            Check(root.OpenSubKey(FolderOpenRegistration.VerbKey("Directory")) is null,
                "relabeling never recreates a verb that is turned off");

            using (var shell = root.OpenSubKey(FolderOpenRegistration.ShellKey("Directory"), writable: true)!)
            using (var classes = root.OpenSubKey(@"Software\Classes")!)
            {
                bool elsewhere = false;
                foreach (string verb in new[] { "none", "open", "explore" })
                {
                    shell.SetValue("", verb);
                    elsewhere |= ShellViewHost.FolderVerbOpensElsewhere(classes, Path.GetTempPath());
                }
                Check(!elsewhere, "Windows' own folder defaults keep native in-place opening");
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false);
            using var parent = Registry.CurrentUser.OpenSubKey(@"Software\FolderSplitterTests");
            if (parent is { SubKeyCount: 0 }) Registry.CurrentUser.DeleteSubKey(@"Software\FolderSplitterTests", false);
        }
    }

    // The app's own folder menu must not offer the registered verb, which would start a second instance.
    private static void RunFolderMenuVerbChecks()
    {
        var remove = typeof(ShellViewHost).GetMethod("RemoveOwnFolderVerb", BindingFlags.Static | BindingFlags.NonPublic)!;
        nint buffer = Marshal.AllocHGlobal(1024);
        try
        {
            foreach (bool ownDefault in new[] { true, false })
            {
                string[] verbs = ownDefault ? ["open", FolderOpenRegistration.Verb, "properties"] : ["open", "properties", FolderOpenRegistration.Verb];
                nint menu = NftCreatePopupMenu();
                try
                {
                    for (int i = 0; i < verbs.Length; i++) FoInsertMenu(menu, (uint)i, 0x0400, (nuint)(i + 1), verbs[i]);
                    FoSetMenuDefaultItem(menu, ownDefault ? 1u : 0u, 1);
                    remove.Invoke(null, [new FoMenuHandler(verbs), menu, buffer]);
                    var ids = Enumerable.Range(0, NftGetMenuItemCount(menu)).Select(i => NftGetMenuItemID(menu, i)).ToArray();
                    int ownId = Array.IndexOf(verbs, FolderOpenRegistration.Verb) + 1;
                    Check(ids.Length == 2 && !ids.Contains((uint)ownId) && FoGetMenuDefaultItem(menu, 1, 0) == 0,
                        ownDefault ? "the app's folder menu drops its own default verb and makes Open the default again"
                            : "the app's folder menu drops its own verb and keeps Open as the default");
                }
                finally { NftDestroyMenu(menu); }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed class FoMenuHandler(string[] verbs) : IContextMenu
    {
        public int QueryContextMenu(nint menu, uint index, uint firstCommand, uint lastCommand, uint flags) => 0;
        public int InvokeCommand(nint info) => 0;
        public int GetCommandString(nuint command, uint type, nint reserved, nint name, uint length)
        {
            if (command >= (nuint)verbs.Length || type != 4) return unchecked((int)0x80004005);
            string verb = verbs[(int)command];
            for (int i = 0; i < verb.Length; i++) Marshal.WriteInt16(name, i * 2, verb[i]);
            Marshal.WriteInt16(name, verb.Length * 2, 0);
            return 0;
        }
    }

    // A second launch sends its folders to the running instance's activation window.
    private static void RunFolderHandoffScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "FolderHandoff")).FullName;
        string start = Directory.CreateDirectory(Path.Combine(fixture, "Start")).FullName;
        string first = Directory.CreateDirectory(Path.Combine(fixture, "Handed folder")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(fixture, "Second")).FullName;
        string session = Path.Combine(fixture, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, ExplorerFavoritesImported = true, CheckUpdates = false, GlobalHotkey = false,
            KeepInTray = true, TrayNoticeShown = true, AutoRestartMemory = 0, StartWithWindows = false,
            Panes = [new PaneState { Tabs = [start] }]
        });
        FoCheckCopyData(fixture);
        var clock = Stopwatch.StartNew();
        Check(!FolderHandoff.TrySend(Path.Combine(fixture, "not-running.json"), [first], TimeSpan.FromMilliseconds(300))
            && clock.Elapsed < TimeSpan.FromSeconds(5),
            "a handoff with no running instance gives up so the launch can fall back to activation");
        using var context = MultiWindowContext(session);
        RunMultiWindowContext(context, async () =>
        {
            var window = context.Windows.Single();
            await MultiWindowReady(window);
            var pane = window.GetPane(0);
            window.Hide();
            Check(FolderHandoff.TrySend(session, [first, second, Path.Combine(fixture, "missing")], TimeSpan.FromSeconds(2)),
                "the running instance accepts folders from a second launch");
            await FoUntil(() => pane.TabCount == 3);
            Check(window.Visible && pane.CaptureState().Tabs.SequenceEqual([start, first, second])
                && pane.ActiveTab?.Host?.LocationPath == first,
                "handed-over folders open as new tabs, the first selected, in the shown window; missing folders are skipped");
        });
    }

    private static void FoCheckCopyData(string fixture)
    {
        string payload = string.Join('\n', fixture, "relative", Path.Combine(fixture, "missing"));
        nint text = Marshal.StringToHGlobalUni(payload), data = Marshal.AllocHGlobal(3 * IntPtr.Size);
        try
        {
            IReadOnlyList<string>? Read(nint signature)
            {
                Marshal.WriteIntPtr(data, 0, signature);
                Marshal.WriteInt32(data, IntPtr.Size, payload.Length * 2);
                Marshal.WriteIntPtr(data, 2 * IntPtr.Size, text);
                return FolderHandoff.Read(data);
            }
            Check(Read(0x46534F46)?.SequenceEqual([fixture]) == true && Read(0x12345678) is null,
                "only Folder Splitter's folder message is read, and only absolute folders that exist");
        }
        finally { Marshal.FreeHGlobal(text); Marshal.FreeHGlobal(data); }
    }

    private static async Task FoUntil(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Folder handoff did not finish.");
            await Task.Delay(50);
        }
    }

    // When folders open in Folder Splitter by default, the hosted list browses in place instead of running that verb.
    private static void RunFolderActivationScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "FolderActivation")).FullName;
        string inside = Directory.CreateDirectory(Path.Combine(fixture, "Inside")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(fixture, "Other")).FullName;
        string session = Path.Combine(root, "folder-activation.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, ExplorerFavoritesImported = true, CheckUpdates = false, GlobalHotkey = false,
            KeepInTray = false, AutoRestartMemory = 0, StartWithWindows = false, Panes = [new PaneState { Tabs = [fixture] }]
        });
        string rootPath = @"Software\FolderSplitterTests\" + Guid.NewGuid().ToString("N");
        var original = ShellViewHost.FolderOpensElsewhere;
        using var form = new MainForm(session, []);
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                var host = pane.ActiveView!;
                await OitReady(host);
                int navigations = 0;
                host.Navigated += (_, _) => navigations++;
                ShellViewHost.FolderOpensElsewhere = _ => true;
                await FoDoubleClick(host, inside);
                await Task.Delay(500);
                await NativeAppearanceDispatch(host);
                Check(host.LocationPath == inside && host.InPlaceFolderActivations == 1 && navigations == 1,
                    $"double-clicking a folder opens it in place exactly once (navigations={navigations})");
                var back = NavigationSignal(host);
                host.Travel(0x4000);
                await back.WaitAsync(TimeSpan.FromSeconds(20));
                Check(host.LocationPath == fixture, "Back returns to the parent, so the folder was entered once");

                await OitReady(host);
                host.SelectPaths([inside]);
                host.FocusView();
                var entered = NavigationSignal(host);
                TgKey(form, Keys.Enter); // through the app's key filter and the view's accelerators, as typed
                await entered.WaitAsync(TimeSpan.FromSeconds(20));
                Check(host.LocationPath == inside && host.InPlaceFolderActivations == 2, "Enter on a folder opens it in place");
                back = NavigationSignal(host);
                host.Travel(0x4000);
                await back.WaitAsync(TimeSpan.FromSeconds(20));

                // With Windows' own default the Shell keeps handling activation. Never run this leg while the real
                // default would start Folder Splitter, which would hand the folder to the user's running app.
                if (ShellViewHost.FolderVerbOpensElsewhere(Registry.ClassesRoot, other))
                    throw new InvalidOperationException("Turn off 'Open folders in Folder Splitter by default' before this check.");
                ShellViewHost.FolderOpensElsewhere = _ => false;
                await OitReady(host);
                await FoDoubleClick(host, other);
                Check(host.LocationPath == other && host.InPlaceFolderActivations == 2,
                    "with Windows' own folder default the Shell still opens the folder itself");

                using var scratch = Registry.CurrentUser.CreateSubKey(rootPath);
                form.ApplicationContext.FolderOpening = new FolderOpenRegistration(scratch, FoExecutable);
                var registration = form.ApplicationContext.FolderOpening;
                var settings = form.Controls.OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>())
                    .Single(item => item.AccessibleName == "Settings");
                var menu = settings.DropDownItems.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == "Show \"Open in Folder Splitter\" when right-clicking a folder");
                var byDefault = settings.DropDownItems.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == "Open folders in Folder Splitter by default");
                bool State(bool menuOn, bool defaultOn) => registration.MenuEnabled == menuOn && registration.DefaultEnabled == defaultOn
                    && menu.Checked == menuOn && byDefault.Checked == defaultOn;
                byDefault.PerformClick();
                Check(State(true, true), "turning on opening by default also adds the right-click verb");
                byDefault.PerformClick();
                Check(State(true, false), "turning off opening by default keeps the right-click verb");
                byDefault.PerformClick();
                menu.PerformClick();
                Check(State(false, false), "removing the right-click verb also ends opening by default");
                menu.PerformClick();
                Check(State(true, false), "the right-click verb can be added alone");
                menu.PerformClick();
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL folder activation " + ex); }
            finally
            {
                ShellViewHost.FolderOpensElsewhere = original;
                form.ExitApplication();
            }
        };
        try { Application.Run(form); }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false);
            using var parent = Registry.CurrentUser.OpenSubKey(@"Software\FolderSplitterTests");
            if (parent is { SubKeyCount: 0 }) Registry.CurrentUser.DeleteSubKey(@"Software\FolderSplitterTests", false);
        }
    }

    private static async Task FoDoubleClick(ShellViewHost host, string folder)
    {
        nint list = NativeAppearanceList(host);
        var point = OitRowPoint(host, folder);
        var navigated = NavigationSignal(host);
        // Posted mouse messages do not synthesize the parent notification of the Windows input route.
        SendMessage(WheelGetParent(list), 0x0210, 0x0201, (point.Y << 16) | (point.X & 0xffff));
        await OitMouse(list, point, 0x201, 0x202, Keys.None, doubleClick: true);
        await navigated.WaitAsync(TimeSpan.FromSeconds(20));
    }

    // The published executable: a second process with a folder argument, written exactly as the registered
    // command, hands it to the first instead of opening another window.
    private static int RunPublishedFolderHandoffSmoke(string executable, string output)
    {
        Directory.CreateDirectory(output);
        executable = Path.GetFullPath(executable);
        string session = Path.Combine(output, "published-handoff-session.json");
        string start = Directory.CreateDirectory(Path.Combine(output, "Handoff-start")).FullName;
        string folder = Directory.CreateDirectory(Path.Combine(output, "Handoff folder")).FullName;
        string drive = Path.GetPathRoot(folder)!;
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, StartWithWindows = false, GlobalHotkey = false, CheckUpdates = false,
            AutoRestartMemory = 0, KeepInTray = false, ExplorerFavoritesImported = true, Panes = [new() { Tabs = [start] }]
        });
        var registration = new FolderOpenRegistration(Registry.CurrentUser, executable); // formats commands only
        Process Launch(string kind, string target)
        {
            string arguments = registration.Command(kind)[($"\"{executable}\" ").Length..].Replace("%1", target);
            return Process.Start(new ProcessStartInfo(executable, $"--session \"{session}\" {arguments}") { UseShellExecute = false })
                ?? throw new InvalidOperationException("Published process did not start.");
        }
        using var app = Process.Start(new ProcessStartInfo(executable, $"--session \"{session}\"") { UseShellExecute = false })
            ?? throw new InvalidOperationException("Published process did not start.");
        try
        {
            List<nint> windows = [];
            // The accessibility walk reaches some subtrees from several HWNDs; count each tab once.
            int Tabs() => PublishedNames(windows[0]).Select(item => item.Name)
                .Where(name => name.StartsWith("Folder tab:", StringComparison.Ordinal)).Distinct().Count();
            PublishedWait(() =>
            {
                windows = PublishedWindows(app.Id, browsersOnly: true);
                return windows.Count == 1 && PublishedNames(windows[0]).Any(item => item.Name.Contains("Handoff-start", StringComparison.Ordinal));
            }, "published browser with its start folder");
            int before = Tabs();
            foreach (var (kind, target) in new[] { ("Directory", folder), ("Drive", drive) })
            {
                var clock = Stopwatch.StartNew();
                using (var second = Launch(kind, target))
                    Check(second.WaitForExit(30_000) && second.ExitCode == 0 && clock.Elapsed < TimeSpan.FromSeconds(15),
                        $"a second launch with the registered {kind} command exits after handing the folder over");
                PublishedWait(() => PublishedWindows(app.Id, browsersOnly: true).Count == 1 && Tabs() == before + 1,
                    $"handed-over {kind} tab");
                before++;
                Check(PublishedWindows(app.Id, browsersOnly: true).Count == 1 && !app.HasExited,
                    $"the {kind} folder opens as a tab in the running window, not a new window");
            }
            var settings = PublishedNames(windows[0]).First(item => item.Name == "Settings");
            settings.Object.accDoDefaultAction(settings.Child);
            PublishedAccessible? exit = null;
            PublishedWait(() =>
            {
                exit = PublishedWindows(app.Id, browsersOnly: false).SelectMany(PublishedNames).FirstOrDefault(item => item.Name == "Exit");
                return exit is not null;
            }, "published Settings Exit");
            exit!.Object.accDoDefaultAction(exit.Child);
            Check(app.WaitForExit(30_000) && app.ExitCode == 0, "the published app exits cleanly after the handoffs");
            var tabs = SessionStore.Load(session).Windows[0].Panes[0].Tabs;
            Check(tabs.Contains(folder, StringComparer.OrdinalIgnoreCase) && tabs.Contains(drive, StringComparer.OrdinalIgnoreCase),
                "the handed-over folder and drive tabs are saved with the session");
        }
        catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL published folder handoff " + exception); }
        finally { if (!app.HasExited) { app.Kill(entireProcessTree: true); app.WaitForExit(); } }
        Console.WriteLine($"PUBLISHED_FOLDER_HANDOFF_RESULT checks={checks} failures={failures}");
        return failures == 0 ? 0 : 1;
    }

    [DllImport("user32.dll", EntryPoint = "InsertMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool FoInsertMenu(nint menu, uint position, uint flags, nuint command, string text);
    [DllImport("user32.dll", EntryPoint = "SetMenuDefaultItem")]
    private static extern bool FoSetMenuDefaultItem(nint menu, uint item, uint byPosition);
    [DllImport("user32.dll", EntryPoint = "GetMenuDefaultItem")]
    private static extern uint FoGetMenuDefaultItem(nint menu, uint byPosition, uint flags);
}
