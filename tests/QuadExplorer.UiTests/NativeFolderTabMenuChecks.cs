using QuadExplorer;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class Program
{
    private static void RunNativeFolderTabMenuScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "NativeFolderTabMenus")).FullName;
        string[] folders = Enumerable.Range(0, 4).Select(i =>
            Directory.CreateDirectory(Path.Combine(fixture, $"MenuFolder-{i}")).FullName).ToArray();
        string file = Path.Combine(fixture, "menu-file.txt");
        File.WriteAllText(file, "Owned native-menu fixture; never launched.");
        string deleteFolder = Directory.CreateDirectory(Path.Combine(fixture, "OwnedDelete")).FullName;
        string session = Path.Combine(root, "native-folder-tab-menu.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, ExplorerFavoritesImported = true,
            CheckUpdates = false, GlobalHotkey = false, KeepInTray = false, AutoRestartMemory = 0,
            Panes = [new PaneState { Tabs = [fixture, folders[3]] }]
        });
        using var form = new MainForm(session, []);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                await OitReady(pane.ActiveView!);
                var opener = pane.ActiveTab!;
                var host = opener.Host!;
                host.SelectPaths([folders[0]]);
                NftLogHostedMenuBeforeInsertion(host);
                var singleMenu = await NftChooseMenu(host, "opennewtab", folders[0]);
                Check(singleMenu.Count(entry => entry.Verb == "opennewtab") == 1,
                    "actual folder popup exposes exactly one native-or-inserted Open in new tab command");
                Check(pane.TabCount == 3 && pane.Tabs[1].Host?.LocationPath == folders[0]
                    && pane.ActiveTab == opener && host.LocationPath == fixture,
                    $"native pointer-context menu opens one background tab after its opener (tabs={pane.TabCount}, selected={pane.SelectedIndex}, paths={string.Join("|", pane.Tabs.Select(tab => tab.Host?.LocationPath))})");
                Check(SelectedPaths(host).SequenceEqual([folders[0]]),
                    "native right-click selection is retained after the menu opens its background tab");
                Check(new[] { "properties", "rename", "copy", "cut", "delete" }
                    .All(verb => singleMenu.Any(entry => entry.Verb == verb)),
                    "wrapped popup retains the original Shell Properties/Rename/Copy/Cut/Delete command offsets");
                Console.WriteLine($"NATIVE_FOLDER_MENU raw_native_opennewtab={singleMenu.Any(entry => entry.Verb == "opennewtab" && entry.Id != 0x7000)} raw_native_opennewwindow={singleMenu.Any(entry => entry.Verb == "opennewwindow" && entry.Id != 0x7001)}");

                Check(host.SelectPaths([folders[1], folders[2]]) == 2,
                    "native menu multi-folder case uses the real Shell selection");
                await NftChooseMenu(host, "opennewtab"); // Keyboard WM_CONTEXTMENU retains the whole selection.
                Check(pane.TabCount == 5 && pane.Tabs.Skip(1).Take(3).Select(tab => tab.Host?.LocationPath).SequenceEqual(folders.Take(3))
                    && pane.ActiveTab == opener,
                    $"actual multi-folder popup appends every selected folder to the opener's existing background run (tabs={pane.TabCount}, paths={string.Join("|", pane.Tabs.Select(tab => tab.Host?.LocationPath))})");
                Check(SelectedPaths(host).SequenceEqual([folders[1], folders[2]]),
                    "multi-folder menu activation leaves the native selection unchanged");

                foreach (Keys key in new[] { Keys.Apps, Keys.Shift | Keys.F10 })
                {
                    host.SelectPaths([folders[0]]);
                    int count = pane.TabCount;
                    await NftChooseMenu(host, "opennewtab", keyboard: key);
                    Check(pane.TabCount == count + 1 && pane.ActiveTab == opener,
                        $"{key} opens the selected folder's actual anchored menu and creates a background tab");
                }

                host.SelectPaths([folders[1], folders[2]]);
                var existingWindows = form.ApplicationContext.Windows.ToArray();
                await NftChooseMenu(host, "opennewwindow");
                var created = form.ApplicationContext.Windows.Single(window => !existingWindows.Contains(window));
                await OitReady(created.GetPane(0).ActiveView!);
                Check(created.GetPane(0).CaptureState().Tabs.SequenceEqual([folders[1], folders[2]])
                    && pane.ActiveTab == opener && host.LocationPath == fixture,
                    "actual Open in new window menu command creates our browser with every selected folder");
                var peerPane = created.GetPane(0);
                await OitReset(peerPane, fixture, folders[3]);
                var peerOpener = peerPane.ActiveTab!;
                peerPane.SetTabsPinned([peerOpener], true);
                var peerHost = peerOpener.Host!;
                peerHost.SelectPaths([folders[0]]);
                await NftChooseMenu(peerHost, "opennewtab", folders[0]);
                Check(peerPane.PinnedCount == 1 && peerPane.ActiveTab == peerOpener
                    && OitPath(peerPane.Tabs[1]) == folders[0] && !peerPane.Tabs[1].Pinned,
                    "peer window native folder context menu opens background tabs outside its pinned prefix");
                peerHost.SelectPaths([folders[1]]);
                peerHost.FocusView();
                await OitKey(PnGetFocus(), Keys.Control | Keys.Shift | Keys.Enter);
                Check(peerPane.PinnedCount == 1 && peerPane.ActiveTab == peerPane.Tabs[2]
                    && OitPath(peerPane.ActiveTab) == folders[1] && !peerPane.ActiveTab.Pinned,
                    "peer window Ctrl+Shift+Enter appends after the pinned opener's related menu run");
                peerPane.SelectTab(0, focus: false);
                peerHost.SelectPaths([folders[2]]);
                await NftChooseMenu(peerHost, "opennewtab", keyboard: Keys.Apps);
                Check(peerPane.PinnedCount == 1 && peerPane.ActiveTab == peerOpener
                    && peerPane.Tabs.Count(tab => OitPath(tab) == folders[2]) == 1,
                    "peer window Apps-key folder menu retains its hosted open-in-new-tab command");
                created.Close();

                host.SelectPaths([folders[0]]);
                var background = await NftChooseMenu(host, null, blank: true);
                Check(background.All(entry => !entry.Wrapped && entry.Id != 0x7000),
                    "blank-space context menu remains original DefView even while a folder is selected");
                host.SelectPaths([folders[0]]);

                int before = pane.TabCount;
                await NftChooseMenu(host, null);
                Check(pane.TabCount == before && pane.ActiveTab == opener,
                    "dismissing the real wrapped popup invokes no command");
                var submenus = await NftChooseMenu(host, "__submenus");
                Check(submenus.Any(entry => entry.HasSubmenu),
                    "live native submenu initialization is forwarded without replacing Shell extension items");

                host.SelectPaths([folders[0]]);
                await NftChooseMenu(host, "rename");
                nint list = NativeAppearanceList(host);
                nint edit = SendMessage(list, 0x1018, 0, 0); // LVM_GETEDITCONTROL
                Check(edit != 0 && pane.TabCount == before,
                    "a real native Rename menu command preserves DefView's in-place label editor");
                if (edit != 0)
                {
                    SendMessage(edit, 0x0100, 0x1B, 1);
                    SendMessage(edit, 0x0101, 0x1B, unchecked((nint)0xC0000001));
                    await NativeAppearanceDispatch(host);
                }
                Check(Directory.Exists(folders[0]), "cancelling native Rename leaves the owned folder intact");

                host.SelectPaths([folders[0]]);
                await NftProperties(host);
                await NftDeleteOwnedFolder(host, deleteFolder);

                host.SelectPaths([file]);
                var fileMenu = await NftChooseMenu(host, null);
                Check(fileMenu.All(entry => entry.Id != 0x7000) && pane.TabCount == before,
                    "file-only right-click stays native and receives no folder-tab menu command");

                host.SelectPaths([folders[2]]);
                var normalOpen = NavigationSignal(host);
                await NftChooseMenu(host, "open");
                await normalOpen.WaitAsync(TimeSpan.FromSeconds(15));
                Check(host.LocationPath == folders[2] && pane.ActiveTab == opener && pane.TabCount == before,
                    "original native Open menu command still navigates the current pane through its hosted Shell site");
            }
            catch (Exception ex) { failure = ex; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Native folder-tab menu scenario failed.", failure);
    }

    private sealed record NftMenuEntry(int Position, uint Id, string? Verb, bool Selectable, bool Wrapped, bool HasSubmenu);

    private static async Task NftProperties(ShellViewHost host)
    {
        using var dialog = new NftOwnedDialog(confirmDelete: false);
        await NftChooseMenu(host, "properties");
        await dialog.Shown.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await dialog.Closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Check(dialog.Failure is null, "native Properties opens and closes its actual private-desktop property sheet");
    }

    private static async Task NftDeleteOwnedFolder(ShellViewHost host, string folder)
    {
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(folder)!, Path.GetFileName(folder))
        {
            NotifyFilter = NotifyFilters.DirectoryName
        };
        watcher.Deleted += (_, _) => removed.TrySetResult();
        watcher.EnableRaisingEvents = true;
        using var dialog = new NftOwnedDialog(confirmDelete: true);
        Check(host.SelectPaths([folder]) == 1, "native Delete selects only the owned disposable folder");
        await NftChooseMenu(host, "delete", modifiers: Keys.Shift);
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (dialog.Shown.Task.IsCompleted) await dialog.Closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Check(dialog.Failure is null && !Directory.Exists(folder),
            "original native Shift+Delete removes only its owned fixture without touching the shared Recycle Bin");
    }

    private sealed class NftOwnedDialog : IDisposable
    {
        internal readonly TaskCompletionSource Shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? Failure;
        private readonly ShortcutWinEvent callback;
        private readonly nint hook;
        private nint dialog;

        internal NftOwnedDialog(bool confirmDelete)
        {
            callback = (_, eventId, window, objectId, childId, _, _) =>
            {
                if (objectId != 0 || childId != 0) return;
                if (eventId == 0x8001 && window == dialog) { Closed.TrySetResult(); return; }
                if (eventId != 0x8002 || dialog != 0) return;
                var name = new StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "#32770") return;
                dialog = window;
                Shown.TrySetResult();
                Console.WriteLine($"NATIVE_OWNED_DIALOG kind={(confirmDelete ? "delete" : "properties")} hwnd={window:X}");
                if (!confirmDelete) PostMessage(window, 0x0010, 0, 0);
                else
                {
                    nint yes = NftGetDlgItem(window, 6); // IDYES; only the owned delete fixture is selected.
                    if (yes != 0) PostMessage(yes, 0x00F5, 0, 0); // BM_CLICK
                    else
                    {
                        Failure = new InvalidOperationException("Owned delete dialog did not expose a standard Yes button.");
                        PostMessage(window, 0x0010, 0, 0);
                    }
                }
            };
            hook = ShortcutSetWinEventHook(0x8001, 0x8002, 0, callback, (uint)Environment.ProcessId, 0, 0);
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            ShortcutUnhookWinEvent(hook);
            GC.KeepAlive(callback);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint NftGetDlgItem(nint dialog, int id);

    private static void NftLogHostedMenuBeforeInsertion(ShellViewHost host)
    {
        var view = (IShellView)typeof(ShellViewHost).GetField("view",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Guid id = typeof(IContextMenu).GUID;
        Marshal.ThrowExceptionForHR(view.GetItemObject(1, ref id, out nint pointer));
        IContextMenu handler;
        try { handler = (IContextMenu)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
        nint menu = NftCreatePopupMenu();
        try
        {
            if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.ThrowExceptionForHR(handler.QueryContextMenu(menu, 0, 1, 0x6FFF, 0x10));
            for (int i = 0, count = NftGetMenuItemCount(menu); i < count; i++)
            {
                uint command = NftGetMenuItemID(menu, i);
                var caption = new StringBuilder(512);
                NftGetMenuString(menu, (uint)i, caption, caption.Capacity, 0x0400);
                Console.WriteLine($"HOSTED_NATIVE_FOLDER_MENU_BEFORE_INSERTION position={i} id={command} canonical={NftVerb(handler, command)} caption={caption}");
            }
        }
        finally
        {
            if (menu != 0) NftDestroyMenu(menu);
            Marshal.ReleaseComObject(handler);
        }
    }

    private static async Task<IReadOnlyList<NftMenuEntry>> NftChooseMenu(ShellViewHost host, string? command,
        string? mouseFolder = null, Keys? keyboard = null, bool blank = false, Keys modifiers = Keys.None)
    {
        nint list = NativeAppearanceList(host), menuWindow = 0;
        Point point = mouseFolder is null ? Point.Empty : OitRowPoint(host, mouseFolder);
        var opened = new TaskCompletionSource<IReadOnlyList<NftMenuEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? driveMenu = null;
        Exception? driveFailure = null;
        byte[]? savedKeys = null;
        var tabInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveTabs(IReadOnlyList<string> paths, bool foreground) => tabInvoked.TrySetResult();
        host.OpenFoldersInTabsRequested += ObserveTabs;
        ShortcutWinEvent callback = (_, eventId, window, _, _, _, _) =>
        {
            if (eventId == 7)
            {
                if (window == menuWindow) closed.TrySetResult();
                return;
            }
            if (eventId != 6 || menuWindow != 0) return;
            menuWindow = window;
            try
            {
                nint menu = SendMessage(window, 0x01E1, 0, 0); // MN_GETHMENU: actual tracking popup, never a mock.
                if (menu == 0) throw new InvalidOperationException("The native popup has no HMENU.");
                var handler = (IContextMenu?)typeof(ShellViewHost).GetField("activeFolderContextMenu",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host);
                var entries = new List<NftMenuEntry>();
                for (int i = 0, count = NftGetMenuItemCount(menu); i < count; i++)
                {
                    uint id = NftGetMenuItemID(menu, i);
                    uint state = NftGetMenuState(menu, (uint)i, 0x0400); // MF_BYPOSITION
                    string? verb = id == 0x7000 ? "opennewtab" : id == 0x7001 ? "opennewwindow"
                        : handler is null ? null : NftVerb(handler, id);
                    entries.Add(new NftMenuEntry(i, id, verb, (state & 0x0803) == 0, handler is not null,
                        NftGetSubMenu(menu, i) != 0)); // not separator/disabled/grayed
                    var caption = new StringBuilder(512);
                    NftGetMenuString(menu, (uint)i, caption, caption.Capacity, 0x0400);
                    Console.WriteLine($"NATIVE_FOLDER_POPUP position={i} id={id} canonical={verb} caption={caption}");
                }
                if (command is null)
                {
                    opened.TrySetResult(entries);
                    NftPostMenuKey(window, 0x1B); // Native DefView owns file/background menus, not our list idle observer.
                    return;
                }
                if (command == "__submenus")
                {
                    driveMenu = () =>
                    {
                        bool populated = false;
                        foreach (var entry in entries.Where(entry => entry.HasSubmenu && entry.Selectable))
                        {
                            nint submenu = NftGetSubMenu(menu, entry.Position);
                            SendMessage(list, 0x0117, submenu, entry.Position); // WM_INITMENUPOPUP, actual native child HMENU
                            int children = NftGetMenuItemCount(submenu);
                            if (children < 0) throw new InvalidOperationException("A live native submenu lost its HMENU.");
                            populated |= children > 0; // Some optional extensions legitimately have no applicable commands.
                            Console.WriteLine($"NATIVE_FOLDER_SUBMENU position={entry.Position} children={children}");
                        }
                        if (!populated) throw new InvalidOperationException("No applicable native submenu populated.");
                        NftPostMenuKey(window, 0x1B);
                    };
                    opened.TrySetResult(entries);
                    return;
                }
                var target = entries.SingleOrDefault(entry => entry.Verb == command && entry.Selectable)
                    ?? throw new InvalidOperationException("Actual native popup lacks command: " + command);
                // MENUPOPUPSTART precedes tracking readiness. Drive the real Win11 popup's
                // selection/activation messages only after its modal loop reaches WM_ENTERIDLE.
                // This never fabricates WM_COMMAND or calls an application callback directly.
                driveMenu = () =>
                {
                    SendMessage(window, 0x01E5, target.Position, 0); // MN_SELECTITEM, actual menu position
                    if (!PostMessage(window, 0x01F1, target.Position, 0)) // MN_DBLCLK, native item activation
                        throw new InvalidOperationException("Could not activate the actual private native menu item.");
                };
                opened.TrySetResult(entries);
            }
            catch (Exception ex)
            {
                opened.TrySetException(ex);
                NftPostMenuKey(window, 0x1B);
            }
        };
        nint hook = ShortcutSetWinEventHook(6, 7, 0, callback, (uint)Environment.ProcessId, 0, 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var observer = new NftMenuObserver(() =>
        {
            var drive = driveMenu;
            driveMenu = null;
            try { drive?.Invoke(); }
            catch (Exception ex)
            {
                driveFailure = ex;
                NftPostMenuKey(menuWindow, 0x1B);
            }
        });
        observer.AssignHandle(list);
        try
        {
            if (keyboard is { } key)
            {
                host.FocusView();
                savedKeys = OitSetModifiers(key);
                nint code = (nint)(key & Keys.KeyCode);
                bool systemKey = (key & Keys.KeyCode) == Keys.F10;
                Check(PostMessage(list, systemKey ? 0x0104u : 0x0100u, code, 1)
                    && PostMessage(list, systemKey ? 0x0105u : 0x0101u, code, unchecked((nint)0xC0000001)),
                    $"{key} is posted through the actual application/native keyboard route");
            }
            else if (blank)
            {
                NativeAppearanceGetClientRect(list, out var client);
                var empty = new ShortcutPoint { X = client.Right - 12, Y = client.Bottom - 12 };
                ShortcutToScreen(list, ref empty);
                SendMessage(list, 0x007B, list, (empty.Y << 16) | (empty.X & 0xFFFF));
            }
            else if (mouseFolder is null)
            {
                if (modifiers != Keys.None) savedKeys = OitSetModifiers(modifiers);
                SendMessage(list, 0x007B, list, -1); // Return only after the real modal context-menu call unwinds.
            }
            else
            {
                var screen = new ShortcutPoint { X = point.X, Y = point.Y };
                ShortcutToScreen(list, ref screen);
                // Address the real pointer-context message explicitly. Synthetic button-up uses
                // GetMessagePos (the user's unrelated cursor), not its supplied client coordinates.
                SendMessage(list, 0x007B, list, (screen.Y << 16) | (screen.X & 0xFFFF));
            }
            var entries = await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (driveFailure is not null) throw new InvalidOperationException("Native popup activation failed.", driveFailure);
            if (command == "opennewtab") await tabInvoked.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await NativeAppearanceDispatch(host);
            await NativeAppearanceDispatch(host);
            return entries;
        }
        finally
        {
            if (menuWindow != 0 && !closed.Task.IsCompleted) NftPostMenuKey(menuWindow, 0x1B);
            observer.ReleaseHandle();
            ShortcutUnhookWinEvent(hook);
            host.OpenFoldersInTabsRequested -= ObserveTabs;
            if (savedKeys is not null && !PnSetKeyboardState(savedKeys))
                throw new InvalidOperationException("Could not restore private thread menu key state.");
            GC.KeepAlive(callback);
        }
    }

    private sealed class NftMenuObserver(Action idle) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0121 && message.WParam == 2) idle(); // WM_ENTERIDLE, MSGF_MENU
            base.WndProc(ref message);
        }
    }

    private static void NftPostMenuKey(nint window, int key)
    {
        if (!PostMessage(window, 0x0100, key, 1)
            || !PostMessage(window, 0x0101, key, unchecked((nint)0xC0000001)))
            throw new InvalidOperationException("Could not address a key to the private native popup.");
    }

    private static string? NftVerb(IContextMenu menu, uint id)
    {
        if (id == 0 || id > 0x6FFF) return null;
        nint text = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.WriteInt16(text, 0);
            return menu.GetCommandString(id - 1, 4, 0, text, 512) >= 0 ? Marshal.PtrToStringUni(text) : null;
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")] private static extern nint NftCreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "DestroyMenu")] private static extern bool NftDestroyMenu(nint menu);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemCount")] private static extern int NftGetMenuItemCount(nint menu);
    [DllImport("user32.dll", EntryPoint = "GetSubMenu")] private static extern nint NftGetSubMenu(nint menu, int position);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemID")] private static extern uint NftGetMenuItemID(nint menu, int position);
    [DllImport("user32.dll", EntryPoint = "GetMenuState")] private static extern uint NftGetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMenuStringW", CharSet = CharSet.Unicode)]
    private static extern int NftGetMenuString(nint menu, uint item, StringBuilder text, int capacity, uint flags);
}
