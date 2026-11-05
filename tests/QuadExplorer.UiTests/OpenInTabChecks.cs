using QuadExplorer;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FolderTab = QuadExplorer.ExplorerPane.FolderTab;

internal static partial class Program
{
    private static void RunOpenInTabScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "OpenInTab")).FullName;
        string[] folders = Enumerable.Range(0, 16).Select(i =>
            Directory.CreateDirectory(Path.Combine(fixture, $"Folder-{i:D2}")).FullName).ToArray();
        string file = Path.Combine(fixture, "ordinary-file.txt");
        File.WriteAllText(file, "Owned open-in-tab fixture; never launched by this scenario.");
        string link = Path.Combine(fixture, "folder-shortcut.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object shortcut = ((dynamic)shell).CreateShortcut(link);
        try { ((dynamic)shortcut).TargetPath = folders[5]; ((dynamic)shortcut).Save(); }
        finally { Marshal.ReleaseComObject(shortcut); Marshal.ReleaseComObject(shell); }
        string session = Path.Combine(root, "open-in-tab.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, WindowWidth = 1200, WindowHeight = 800,
            ExplorerFavoritesImported = true, CheckUpdates = false, GlobalHotkey = false,
            KeepInTray = false, AutoRestartMemory = 0, ShowFolderTree = true,
            Favorites = [folders[0]], Panes = [new PaneState { Tabs = [fixture] }],
            SavedGroups = [new SavedTabGroup { Id = "open-in-tab-fixture", Name = "Owned background group",
                Paths = [folders[4], folders[5]] }]
        });
        using var form = new MainForm(session, []);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                await OitReady(pane.ActiveView!);
                await OitNativeGestures(pane, fixture, folders, file);
                OitPlacement(pane, folders, file);
                await OitChromeGestures(form, pane, fixture, folders);
                await OitExternalDrops(form, pane, fixture, folders, file, link);
            }
            catch (Exception ex) { failure = ex; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Open-in-tab scenario failed.", failure);
    }

    private static void RunNativeDropTargetScenario(string root, string[] folders)
    {
        string session = Path.Combine(root, "native-drop-targets.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 4, ExplorerFavoritesImported = true, CheckUpdates = false, GlobalHotkey = false,
            KeepInTray = false, AutoRestartMemory = 0,
            Panes = folders.Select(path => new PaneState { Tabs = [path] }).ToList()
        });
        using var form = new MainForm(session, []);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var panes = Enumerable.Range(0, 4).Select(form.GetPane).ToArray();
                foreach (var pane in panes) await OitReady(pane.ActiveView!);
                Check(panes.All(pane => HasNativeDropTarget(pane.ActiveView!)),
                    "all four initial native file lists retain registered Shell OLE drop targets");
                var first = panes[0];
                var original = first.ActiveView!;
                await Navigate(original, () => first.Navigate(folders[1]));
                Check(HasNativeDropTarget(original), "navigation preserves the native Shell file-list drop target");
                first.AddTab(folders[2]);
                await OitReady(first.ActiveView!);
                var added = first.ActiveTab!;
                Check(HasNativeDropTarget(added.Host!), "a new Shell tab registers its native file-list target independently of its strip");
                form.SetColorTheme("dark");
                Check(HasNativeDropTarget(added.Host!), "theme changes preserve the native Shell drop target");
                Check(form.DockTab(first, added, 1, null), "native target lifecycle fixture docks a real tab");
                await OitReady(panes[1].ActiveView!);
                Check(HasNativeDropTarget(panes[1].ActiveView!) && HasNativeDropTarget(original),
                    "tab docking leaves both receiving and original native file-list OLE targets intact");
                panes[1].CloseTab();
                Check(HasNativeDropTarget(panes[1].ActiveView!), "closing a docked tab restores the destination's native target");
            }
            catch (Exception ex) { failure = ex; }
            finally { form.SetColorTheme("light"); form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Native drop target lifecycle failed.", failure);
    }

    private static async Task OitReady(ShellViewHost host)
    {
        if (!host.HasNavigated) await NavigationSignal(host).WaitAsync(TimeSpan.FromSeconds(20));
        await ItemCountKnown(host);
        host.SetViewModeAndSize(4, 16);
        await NativeAppearanceDispatch(host);
    }

    private static async Task OitReset(ExplorerPane pane, params string[] paths)
    {
        pane.ReplaceTabs(new PaneState { Tabs = paths.ToList() });
        await OitReady(pane.ActiveView!);
    }

    private static string? OitPath(FolderTab tab) => tab.Host?.LocationPath;

    private static async Task OitNativeGestures(ExplorerPane pane, string fixture,
        string[] folders, string file)
    {
        foreach (var gesture in new[]
        {
            (Middle: true, Modifiers: Keys.None, Foreground: false),
            (Middle: true, Modifiers: Keys.Control | Keys.Shift, Foreground: true),
            (Middle: false, Modifiers: Keys.Control, Foreground: false),
            (Middle: false, Modifiers: Keys.Control | Keys.Shift, Foreground: true)
        })
        {
            await OitReset(pane, fixture, folders[15]);
            var opener = pane.ActiveTab!;
            var host = opener.Host!;
            nint list = NativeAppearanceList(host);
            Check(host.SelectPaths([folders[1], file]) == 2, "native gesture fixture has a mixed selection distinct from the clicked folder");
            Point point = OitRowPoint(host, folders[0]);
            string[] selected = SelectedPaths(host);
            await OitMouse(list, point, gesture.Middle ? 0x207u : 0x201u,
                gesture.Middle ? 0x208u : 0x202u, gesture.Modifiers,
                doubleClick: !gesture.Middle);
            var opened = pane.Tabs.Where(tab => tab != opener && OitPath(tab) == folders[0]).ToArray();
            string name = $"native {(gesture.Middle ? "middle down/up" : "Ctrl double-click")} {gesture.Modifiers}";
            Check(pane.TabCount == 3 && opened.Length == 1 && pane.Tabs[1] == opened[0],
                name + " opens exactly the hit folder directly after its opener");
            Check(host.LocationPath == fixture && (gesture.Foreground ? pane.ActiveTab == opened.SingleOrDefault() : pane.ActiveTab == opener),
                name + " preserves the opener and applies background/foreground selection");
            if (gesture.Middle)
                Check(SelectedPaths(host).SequenceEqual(selected), name + " leaves native mixed selection unchanged");
            if (gesture.Foreground && opened.Length == 1) await OitReady(opened[0].Host!);
        }

        await OitReset(pane, fixture);
        var original = pane.ActiveTab!;
        var originalHost = original.Host!;
        nint originalList = NativeAppearanceList(originalHost);
        await OitMouse(originalList, OitRowPoint(originalHost, file), 0x207, 0x208, Keys.None);
        Check(pane.TabCount == 1 && pane.ActiveTab == original && pane.CurrentPath == fixture,
            "middle-clicking a native file neither opens a folder tab nor navigates");
        Check(originalHost.SelectPaths([folders[0]]) == 1, "Ctrl single-click fixture selects one real folder");
        await OitMouse(originalList, OitRowPoint(originalHost, folders[1]), 0x201, 0x202, Keys.Control);
        Check(pane.TabCount == 1 && SelectedPaths(originalHost).Order().SequenceEqual(new[] { folders[0], folders[1] }.Order()),
            "Ctrl single-click retains native folder multiselection without opening a tab");
        await OitMouse(originalList, OitRowPoint(originalHost, file), 0x201, 0x202, Keys.Control);
        Check(pane.TabCount == 1 && SelectedPaths(originalHost).Contains(file),
            "Ctrl single-click retains native file selection without opening a tab");
        await OitMouse(originalList, OitRowPoint(originalHost, Path.Combine(fixture, "folder-shortcut.lnk")), 0x207, 0x208, Keys.None);
        Check(pane.TabCount == 2 && OitPath(pane.Tabs[1]) == folders[5] && pane.ActiveTab == original,
            "native middle-click resolves a folder shortcut to its target instead of opening the shortcut's parent");
        foreach (bool foreground in new[] { false, true })
        {
            await OitReset(pane, fixture, folders[15]);
            var opener = pane.ActiveTab!;
            var host = opener.Host!;
            Check(host.SelectPaths([folders[0], folders[1]]) == 2, "Ctrl+Enter fixture selects two folders through the real Shell view");
            host.FocusView();
            await OitKey(PnGetFocus(), Keys.Enter | Keys.Control | (foreground ? Keys.Shift : Keys.None));
            Check(pane.TabCount == 4 && pane.Tabs.Skip(1).Take(2).Select(OitPath).SequenceEqual(folders.Take(2)),
                $"Ctrl{(foreground ? "+Shift" : "")}+Enter opens selected folders in native order");
            Check(foreground ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener,
                "Ctrl+Enter applies background/foreground selection to the opened batch");
            Check(host.LocationPath == fixture, "Ctrl+Enter does not navigate the opener");
        }

        await OitReset(pane, fixture, folders[15]);
        original = pane.ActiveTab!;
        originalHost = original.Host!;
        originalList = NativeAppearanceList(originalHost);
        foreach (string path in folders.Take(2))
            await OitMouse(originalList, OitRowPoint(originalHost, path), 0x207, 0x208, Keys.None);
        Check(pane.Tabs.Skip(1).Take(2).Select(OitPath).SequenceEqual(folders.Take(2)),
            "real native middle-clicks preserve the ordered opener run ahead of unrelated tabs");
        pane.SelectTab(pane.TabCount - 1, focus: false);
        pane.SelectTab(pane.IndexOfTab(original), focus: false);
        await OitMouse(originalList, OitRowPoint(originalHost, folders[2]), 0x207, 0x208, Keys.None);
        Check(OitPath(pane.Tabs[1]) == folders[2],
            "switching away and back resets the native gesture insertion run");

        await OitReset(pane, fixture, folders[15]);
        original = pane.ActiveTab!;
        originalHost = original.Host!;
        originalList = NativeAppearanceList(originalHost);
        var nativeGroup = pane.CreateGroup(original)!;
        pane.AddToGroup(pane.Tabs[1], nativeGroup);
        await OitMouse(originalList, OitRowPoint(originalHost, folders[0]), 0x207, 0x208, Keys.None);
        Check(pane.TabCount == 3 && pane.Tabs[1].Group == nativeGroup && pane.GroupMembers(nativeGroup).Count == 3,
            "a real native folder gesture joins its opener's group at the adjacent slot");
        while (pane.GroupMembers(nativeGroup).Count < TabGroup.MaxTabs) pane.AddTabInGroup(nativeGroup);
        pane.SelectTab(pane.IndexOfTab(original), focus: false);
        await OitMouse(originalList, OitRowPoint(originalHost, folders[1]), 0x207, 0x208, Keys.None);
        Check(pane.TabCount == 13 && pane.Tabs[^1].Group is null && OitPath(pane.Tabs[^1]) == folders[1]
            && pane.ActiveTab == original,
            "a real native folder gesture spills ungrouped after a full twelve-tab opener group");

        foreach (bool middle in new[] { true, false })
        {
            await OitReset(pane, fixture, folders[15], folders[14]);
            var prefix = pane.Tabs.Take(2).ToArray();
            pane.SetTabsPinned(prefix, true);
            pane.SelectTab(0, focus: false);
            var host = pane.ActiveView!;
            if (middle)
                await OitMouse(NativeAppearanceList(host), OitRowPoint(host, folders[0]), 0x207, 0x208, Keys.Control | Keys.Shift);
            else
            {
                host.SelectPaths([folders[0]]);
                host.FocusView();
                await OitKey(PnGetFocus(), Keys.Control | Keys.Shift | Keys.Enter);
            }
            Check(pane.PinnedCount == 2 && pane.Tabs.Take(2).SequenceEqual(prefix)
                && pane.ActiveTab == pane.Tabs[2] && !pane.ActiveTab.Pinned && OitPath(pane.ActiveTab) == folders[0],
                $"pinned opener native {(middle ? "Ctrl+Shift middle-click" : "Ctrl+Shift+Enter")} opens foreground after the prefix");
        }
    }

    private static Point OitRowPoint(ShellViewHost host, string path)
    {
        string[] selected = host.SelectedPaths().Paths.ToArray();
        string[] listed = ListedPaths(host);
        if (selected.Length != 0) host.SelectPaths(selected);
        int index = Array.FindIndex(listed, candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException("Native row not found: " + path);
        nint list = NativeAppearanceList(host);
        SendMessage(list, 0x1013, index, 0); // LVM_ENSUREVISIBLE
        var bounds = new NativeRect { Left = 2 }; // LVIR_LABEL, not the checkbox/icon area
        if (NativeAppearanceRectMessage(list, 0x100E, index, ref bounds) == 0)
            throw new InvalidOperationException("LVM_GETITEMRECT failed.");
        return new Point(bounds.Left + Math.Min(20, Math.Max(1, (bounds.Right - bounds.Left) / 2)),
            (bounds.Top + bounds.Bottom) / 2);
    }

    // Native HWND messages plus a thread-local key table: never global mouse/keyboard input.
    private static async Task OitMouse(nint window, Point point, uint down, uint up, Keys modifiers,
        bool doubleClick = false)
    {
        byte[] saved = OitSetModifiers(modifiers);
        try
        {
            nint flags = ((modifiers & Keys.Control) != 0 ? 8 : 0) | ((modifiers & Keys.Shift) != 0 ? 4 : 0);
            nint packed = (point.Y << 16) | (point.X & 0xffff);
            var owner = Control.FromHandle(window) ?? Control.FromChildHandle(window)
                ?? throw new InvalidOperationException("No owner for native mouse target.");
            SendMessage(window, 0x200, flags, packed);
            OitMousePair(window, down, up, flags, packed);
            if (doubleClick) OitMousePair(window, 0x203, up, flags, packed);
            await NativeAppearanceDispatch(owner);
        }
        finally { if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore mouse modifier state."); }
    }

    private static void OitMousePair(nint window, uint down, uint up, nint flags, nint packed)
    {
        // Tree/list selection can wait inside button-down for button-up. Queue that release first,
        // then finish any unconsumed release synchronously so Shell creation cannot overtake the test.
        if (!PostMessage(window, up, flags, packed)) throw new InvalidOperationException("Could not queue native button release.");
        SendMessage(window, down, flags | (down == 0x207 ? 16 : 1), packed);
        if (OitPeekMessage(out _, window, up, up, 1)) SendMessage(window, up, flags, packed);
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OitPeekMessage(out NativeMessage message, nint window, uint minimum, uint maximum, uint flags);

    private static async Task OitKey(nint window, Keys key)
    {
        byte[] saved = OitSetModifiers(key);
        try
        {
            // Synchronous native dispatch finishes the list subclass even when creating a Shell view
            // pumps nested messages; a posted continuation alone can otherwise overtake that work.
            SendMessage(window, 0x100, (nint)(key & Keys.KeyCode), 1);
            SendMessage(window, 0x101, (nint)(key & Keys.KeyCode), unchecked((nint)0xC0000001));
            var owner = Control.FromHandle(window) ?? Control.FromChildHandle(window)
                ?? throw new InvalidOperationException("No owner for native key target.");
            await NativeAppearanceDispatch(owner);
            await NativeAppearanceDispatch(owner);
        }
        finally { if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore key modifier state."); }
    }

    private static byte[] OitSetModifiers(Keys keys)
    {
        byte[] saved = new byte[256], state = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("GetKeyboardState failed.");
        if ((keys & Keys.Control) != 0) state[0x11] = state[0xA2] = 0x80;
        if ((keys & Keys.Shift) != 0) state[0x10] = state[0xA0] = 0x80;
        if (!PnSetKeyboardState(state)) throw new InvalidOperationException("SetKeyboardState failed.");
        return saved;
    }


    private static void OitPlacement(ExplorerPane pane, string[] folders, string file)
    {
        TgReset(pane, 3);
        var opener = pane.ActiveTab!;
        var trailing = pane.Tabs.Skip(1).ToArray();
        pane.OpenPathsInNewTabs([folders[0], folders[1]], false);
        pane.OpenPathsInNewTabs([folders[2]], false);
        Check(pane.Tabs.Take(4).Select(OitPath).SequenceEqual(new string?[] { null, folders[0], folders[1], folders[2] })
            && pane.Tabs.Skip(4).SequenceEqual(trailing) && pane.ActiveTab == opener,
            "consecutive background batches preserve the opener's insertion run and unrelated trailing tabs");
        pane.CloseTab(3, remember: false);
        pane.OpenPathsInNewTabs([folders[6]], false);
        Check(pane.Tabs.Skip(1).Take(3).Select(OitPath).SequenceEqual(new[] { folders[0], folders[1], folders[6] })
            && pane.ActiveTab == opener,
            "closing the newest background child retains the remaining opener run for the next folder");
        var movedChild = pane.Tabs[3];
        Check(pane.ReorderTab(movedChild, 0), "a background child can be reordered before its opener");
        pane.OpenPathsInNewTabs([folders[7]], false);
        Check(OitPath(pane.Tabs[4]) == folders[7] && pane.ActiveTab == opener,
            "a reordered child does not pull later related tabs before their opener or surviving siblings");
        pane.SelectTab(pane.IndexOfTab(trailing[0]), focus: false);
        pane.OpenPathsInNewTabs([folders[3]], false);
        Check(pane.Tabs[pane.IndexOfTab(trailing[0]) + 1].Host?.LocationPath == folders[3],
            "changing the opener starts a fresh adjacent insertion run");
        int count = pane.TabCount;
        pane.OpenPathsInNewTabs([file], false);
        Check(pane.TabCount == count + 1 && pane.Tabs.Any(tab => OitPath(tab) == Path.GetDirectoryName(file)),
            "the shared path opener opens a file's containing folder rather than launching its association");

        TgReset(pane, 4);
        var prefix = pane.Tabs.Take(3).ToArray();
        pane.OpenPathsInNewTabs([folders[4]], false, minimumIndex: 3);
        Check(pane.Tabs.Take(3).SequenceEqual(prefix) && OitPath(pane.Tabs[3]) == folders[4],
            "minimumIndex preserves an existing protected tab prefix while opening next to its boundary");

        TgReset(pane, 4);
        prefix = pane.Tabs.Take(3).ToArray();
        pane.SetTabsPinned(prefix, true);
        pane.SelectTab(0, focus: false);
        opener = pane.ActiveTab!;
        pane.OpenPathsInNewTabs([folders[0], folders[1]], false);
        pane.OpenPathsInNewTabs([folders[2]], false);
        Check(pane.PinnedCount == 3 && pane.Tabs.Take(3).SequenceEqual(prefix)
            && pane.Tabs.Skip(3).Take(3).Select(OitPath).SequenceEqual(folders.Take(3))
            && pane.Tabs.Skip(3).Take(3).All(tab => !tab.Pinned) && pane.ActiveTab == opener,
            "pinned active opener places consecutive related batches after the pinned prefix and existing related run");
        pane.OpenPathsInNewTabs([folders[3]], true);
        Check(pane.PinnedCount == 3 && pane.Tabs.Take(3).SequenceEqual(prefix)
            && pane.ActiveTab == pane.Tabs[6] && !pane.ActiveTab.Pinned,
            "foreground related opening from a pinned opener stays outside the pinned prefix");

        TgReset(pane, 3);
        opener = pane.ActiveTab!;
        var group = pane.CreateGroup(opener)!;
        pane.AddToGroup(pane.Tabs[1], group);
        var outside = pane.Tabs[^1];
        pane.OpenPathsInNewTabs([folders[0], folders[1]], false);
        Check(pane.GroupMembers(group).Count == 4 && pane.Tabs[1].Group == group && pane.Tabs[2].Group == group
            && pane.Tabs[^1] == outside && pane.ActiveTab == opener,
            "opened folders inherit an opener's group without splitting its contiguous members");
        TgContiguous(pane, "open-in-tab grouped opener");

        TgReset(pane, 2);
        opener = pane.ActiveTab!;
        group = pane.CreateGroup(opener)!;
        while (pane.GroupMembers(group).Count < TabGroup.MaxTabs) pane.AddTabInGroup(group);
        pane.SelectTab(pane.IndexOfTab(opener), focus: false);
        pane.OpenPathsInNewTabs([folders[0], folders[1]], false);
        int groupEnd = pane.Tabs.ToList().FindLastIndex(tab => tab.Group == group) + 1;
        Check(pane.GroupMembers(group).Count == TabGroup.MaxTabs && pane.Tabs[groupEnd].Group is null
            && pane.Tabs.Skip(groupEnd).Take(2).Select(OitPath).SequenceEqual(folders.Take(2)),
            "a full opener group spills the new folder batch ungrouped after the whole group");
        TgContiguous(pane, "open-in-tab full group spill");

        TgReset(pane, 31);
        opener = pane.ActiveTab!;
        pane.OpenPathsInNewTabs(folders.Take(3).ToArray(), false);
        Check(pane.TabCount == 32 && OitPath(pane.Tabs[1]) == folders[0] && pane.ActiveTab == opener,
            "pane capacity opens only the ordered prefix fitting the 32-tab limit without switching background selection");
        var full = pane.Tabs.ToArray();
        pane.OpenPathsInNewTabs([folders[4]], true);
        Check(pane.Tabs.SequenceEqual(full) && pane.ActiveTab == opener,
            "a full pane rejects foreground opening without disturbing tabs or selection");
    }

    private static async Task OitChromeGestures(MainForm form, ExplorerPane pane, string fixture, string[] folders)
    {
        form.SetSidebarVisible(true);
        form.SetFolderTreeVisible(false);
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Control | Keys.Shift })
        {
            await OitReset(pane, fixture);
            var opener = pane.ActiveTab!;
            var favorites = Descendants(form).OfType<ListBox>().Single(list => list.AccessibleName == "Favorites");
            Point point = new(20, favorites.ItemHeight / 2);
            bool middle = modifiers == Keys.None;
            await OitMouse(favorites.Handle, point, middle ? 0x207u : 0x201u, middle ? 0x208u : 0x202u, modifiers);
            Check(pane.TabCount == 2 && OitPath(pane.Tabs[1]) == folders[0]
                && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener),
                $"favorite sidebar {(middle ? "middle" : modifiers.ToString())} click opens a folder tab with the requested activation (tabs={pane.TabCount}, path={OitPath(pane.Tabs[^1])}, selected={pane.SelectedIndex}, visible={favorites.Visible}, size={favorites.Size})");
        }
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Control | Keys.Shift })
        {
            await OitReset(pane, fixture);
            var opener = pane.ActiveTab!;
            var places = Descendants(form).OfType<ListBox>().Single(list => list.AccessibleName == "Quick access");
            bool middle = modifiers == Keys.None;
            await OitMouse(places.Handle, new Point(20, places.ItemHeight / 2),
                middle ? 0x207u : 0x201u, middle ? 0x208u : 0x202u, modifiers);
            Check(pane.TabCount == 2 && OitPath(pane.Tabs[1]) == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener),
                $"quick-access {(middle ? "middle" : modifiers.ToString())} click opens a tab without replacing its opener");
        }
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Control | Keys.Shift })
        {
            await OitReset(pane, fixture);
            var opener = pane.ActiveTab!;
            var saved = SavedGroupList(form);
            bool middle = modifiers == Keys.None;
            await OitMouse(saved.Handle, new Point(20, saved.ItemHeight / 2),
                middle ? 0x207u : 0x201u, middle ? 0x208u : 0x202u, modifiers);
            Check(pane.TabCount == 3 && pane.Tabs.Skip(1).Select(OitPath).SequenceEqual(folders.Skip(4).Take(2))
                && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener),
                $"saved-group {(middle ? "middle" : modifiers.ToString())} click opens its ordered paths with requested activation");
        }
        form.SetFolderTreeVisible(true);
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Control | Keys.Shift })
        {
            await OitReset(pane, fixture);
            await form.FolderTree.FollowPathAsync(folders[0]);
            TreeView tree = form.FolderTree.Tree;
            TreeNode node = tree.SelectedNode ?? throw new InvalidOperationException("Folder tree failed to follow owned fixture.");
            node.EnsureVisible();
            var opener = pane.ActiveTab!;
            bool middle = modifiers == Keys.None;
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void TreeOpened(object? sender, EventArgs args)
            {
                if (pane.TabCount == 2 && OitPath(pane.Tabs[1]) == folders[0]
                    && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener))
                    opened.TrySetResult();
            }
            pane.StateChanged += TreeOpened;
            try
            {
                await OitMouse(tree.Handle, new Point(node.Bounds.Left + 12, node.Bounds.Top + node.Bounds.Height / 2),
                    middle ? 0x207u : 0x201u, middle ? 0x208u : 0x202u, modifiers);
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { pane.StateChanged -= TreeOpened; }
            Check(pane.TabCount == 2 && OitPath(pane.Tabs[1]) == folders[0]
                && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener),
                $"folder tree {(middle ? "middle" : modifiers.ToString())} click opens rather than replacing the current folder");
        }
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Control | Keys.Shift })
        {
            await OitReset(pane, folders[0]);
            pane.FocusFiles();
            await NativeAppearanceDispatch(pane);
            var opener = pane.ActiveTab!;
            var breadcrumb = Descendants(pane).OfType<ToolStrip>().Single(strip => strip.AccessibleName == "Pane 1 breadcrumb");
            var segment = breadcrumb.Items.OfType<ToolStripButton>().Last(item => item.Available);
            bool middle = modifiers == Keys.None;
            await OitMouse(breadcrumb.Handle, new Point(segment.Bounds.Left + segment.Bounds.Width / 2,
                segment.Bounds.Top + segment.Bounds.Height / 2), middle ? 0x207u : 0x201u, middle ? 0x208u : 0x202u, modifiers);
            Check(pane.TabCount == 2 && OitPath(pane.Tabs[1]) == folders[0]
                && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener),
                $"breadcrumb {(middle ? "middle" : modifiers.ToString())} click uses the shared tab opener");
        }
        foreach (var hit in new[] { new QuadExplorer.Search.SearchHit(folders[0], true),
            new QuadExplorer.Search.SearchHit(Path.Combine(fixture, "ordinary-file.txt"), false) })
        foreach (Keys modifiers in new[] { Keys.None, Keys.Control, Keys.Control | Keys.Shift })
        {
            await OitReset(pane, fixture);
            var opener = pane.ActiveTab!;
            pane.FocusAddress();
            var address = Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName == "Pane 1 address");
            var popup = (AddressCandidates)typeof(ExplorerPane).GetField("candidates",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!;
            popup.Present(address, [hit]);
            var rows = popup.Controls.OfType<ListBox>().Single();
            bool middle = modifiers == Keys.None;
            await OitMouse(rows.Handle, new Point(30, rows.ItemHeight / 2),
                middle ? 0x207u : 0x201u, middle ? 0x208u : 0x202u, modifiers);
            Check(pane.TabCount == 2 && OitPath(pane.Tabs[1]) == (hit.IsDirectory ? hit.FullPath : fixture)
                && (modifiers.HasFlag(Keys.Shift) ? pane.ActiveTab == pane.Tabs[1] : pane.ActiveTab == opener),
                $"address candidate {(middle ? "middle" : modifiers.ToString())} opens the real folder or file parent with requested activation");
            if (!hit.IsDirectory)
            {
                pane.SelectTab(1, focus: false);
                await OitReady(pane.ActiveView!);
                Check(SelectedPaths(pane.ActiveView!).SequenceEqual([hit.FullPath]),
                    "opening a file address candidate in a tab selects the file rather than launching it");
            }
        }
    }

    private static async Task OitExternalDrops(MainForm form, ExplorerPane pane, string fixture, string[] folders,
        string file, string link)
    {
        form.SetSidebarVisible(false);
        await OitReset(pane, folders[10], folders[11], folders[12]);
        var original = pane.Tabs.ToArray();
        Point before = TgStripPoint(pane, original[1].Button, after: false);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0], folders[1]]))
        {
            Check(drop.Enter(before) == 4 && drop.Over(before) == 4,
                "registered strip OLE target advertises LINK for a real Shell CF_HDROP folder payload");
            using var marker = TgPrintStrip(pane.TabStrip);
            drop.Leave();
            using var plain = TgPrintStrip(pane.TabStrip);
            Check(OitPixelDifference(marker, plain) >= 3,
                "real OLE drag hover paints the insertion marker and DragLeave clears it");
            string output = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") ?? Path.Combine(Path.GetTempPath(), "FolderSplitter-QA-Captures");
            Directory.CreateDirectory(output);
            TgSave(marker, Path.Combine(output, "open-in-tab-external-insertion-marker.png"));
        }
        Check(pane.Tabs.SequenceEqual(original), "enter/over/leave never opens or moves tabs");
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0], folders[1]]))
        {
            drop.Enter(before);
            Check(drop.Drop(before) == 4 && pane.Tabs.Take(4).Select(OitPath)
                .SequenceEqual(new[] { folders[10], folders[0], folders[1], folders[11] }),
                "real folder drop inserts its ordered batch at the strip marker instead of the default opener position");
        }
        foreach (string formatName in new[] { "FileNameW", "Shell IDList Array" })
        {
            await OitReset(pane, folders[10], folders[11]);
            Point formatPoint = TgStripPoint(pane, pane.Tabs[0].Button, after: false);
            using var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0]], formatName);
            drop.Enter(formatPoint);
            Check(drop.Drop(formatPoint) == 4 && pane.TabCount == 3 && OitPath(pane.Tabs[0]) == folders[0],
                $"registered OLE strip target accepts real Shell data advertised only as {formatName}");
        }
        Point point;
        foreach (var payload in new[] { new[] { file }, new[] { folders[0], file }, new[] { link } })
        {
            await OitReset(pane, folders[10], folders[11]);
            original = pane.Tabs.ToArray();
            point = TgStripPoint(pane, pane.Tabs[0].Button, after: false);
            using var drop = new OitOleDrop(pane.TabStrip, fixture, payload);
            drop.Enter(point);
            uint effect = drop.Drop(point);
            Check(effect == 4 && pane.TabCount == original.Length + payload.Length
                && pane.Tabs.Take(payload.Length).Select(OitPath).SequenceEqual(payload.Select(path =>
                    Directory.Exists(path) ? path : Path.GetDirectoryName(path))),
                "file, mixed, and .lnk CF_HDROP entries open folders in payload order without launching associations");
            var fileTab = pane.Tabs[payload.Length - 1];
            pane.SelectTab(pane.IndexOfTab(fileTab), focus: false);
            await OitReady(fileTab.Host!);
            Check(SelectedPaths(fileTab.Host!).SequenceEqual([payload[^1]], StringComparer.OrdinalIgnoreCase),
                "a dropped file or .lnk remains the real selected Shell item in its containing folder");
        }

        await OitReset(pane, folders[10], folders[11], folders[12]);
        var group = pane.CreateGroup(pane.Tabs[0])!;
        pane.AddToGroup(pane.Tabs[1], group);
        Point inside = TgStripPoint(pane, pane.Tabs[0].Button, after: true);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0], folders[1]]))
        {
            drop.Enter(inside);
            Check(drop.Drop(inside) == 4 && pane.GroupMembers(group).Count == 4
                && pane.GroupMembers(group).Skip(1).Take(2).Select(OitPath).SequenceEqual(folders.Take(2)),
                "external folder drop inside a group joins its ordered tabs at the insertion marker");
        }
        TgContiguous(pane, "external folder group insertion");
        while (pane.GroupMembers(group).Count < TabGroup.MaxTabs - 1) pane.AddTabInGroup(group);
        pane.SelectTab(pane.IndexOfTab(pane.GroupMembers(group)[0]), focus: false);
        inside = TgStripPoint(pane, pane.GroupMembers(group)[0].Button, after: true);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[3]]))
        {
            drop.Enter(inside);
            Check(drop.Drop(inside) == 4 && pane.GroupMembers(group).Count == TabGroup.MaxTabs
                && pane.GroupMembers(group)[1].Host?.LocationPath == folders[3],
                "merged group drop between members joins the twelfth tab at its marker");
        }
        pane.SelectTab(pane.IndexOfTab(pane.GroupMembers(group)[0]), focus: false);
        original = pane.Tabs.ToArray();
        inside = TgStripPoint(pane, pane.GroupMembers(group)[0].Button, after: true);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[2]]))
        {
            Check(drop.Enter(inside) == 0 && drop.Drop(inside) == 0 && pane.Tabs.SequenceEqual(original),
                "external strip drop rejects a full group atomically instead of splitting it");
        }

        TgReset(pane, 31);
        point = TgStripPoint(pane, pane.Tabs[0].Button, after: false);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0], folders[1]]))
        {
            Check(drop.Enter(point) == 0 && drop.Drop(point) == 0 && pane.TabCount == 31,
                "external folder batch exceeding pane capacity is refused atomically without partial insertion");
        }
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0]]))
        {
            drop.Enter(point);
            Check(drop.Drop(point) == 4 && pane.TabCount == 32 && OitPath(pane.Tabs[0]) == folders[0],
                "a fitting external folder batch uses the final pane slot at its marker");
        }
        original = pane.Tabs.ToArray();
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[2]]))
            Check(drop.Enter(point) == 0 && drop.Drop(point) == 0 && pane.Tabs.SequenceEqual(original),
                "external folder drop into a full pane has NONE effect and preserves all existing tabs");

        TgReset(pane, 20);
        form.Width = form.MinimumSize.Width;
        await NativeAppearanceDispatch(form);
        Check(pane.TabList.Available, "overflow drop fixture exposes the real tab-list control");
        Point overflow = pane.TabStrip.PointToScreen(new Point(pane.TabList.Bounds.Left + pane.TabList.Bounds.Width / 2,
            pane.TabList.Bounds.Top + pane.TabList.Bounds.Height / 2));
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[3]]))
        {
            drop.Enter(overflow);
            Check(drop.Over(overflow) == 4 && drop.Drop(overflow) == 4 && pane.TabCount == 21
                && pane.Tabs.Any(tab => OitPath(tab) == folders[3]),
                "external folder drop remains accepted on the overflow/tab-list end of a crowded strip");
        }
        form.Width = 1200;

        await OitReset(pane, folders[10], folders[11]);
        var hoverTarget = pane.Tabs[1];
        var hoverTimer = (System.Windows.Forms.Timer)typeof(ExplorerPane).GetField("dragHoverTimer",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!;
        Point hover = pane.TabStrip.PointToScreen(new Point(hoverTarget.Button.Bounds.Left + hoverTarget.Button.Bounds.Width / 2,
            hoverTarget.Button.Bounds.Top + hoverTarget.Button.Bounds.Height / 2));
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0]]))
        {
            drop.Enter(hover);
            Check(hoverTimer.Enabled && hoverTimer.Interval == 600 && pane.ActiveTab != hoverTarget,
                "external drag over an inactive tab arms the dwell timer without immediately switching");
            OitFireTimer(hoverTimer);
            Check(pane.ActiveTab == hoverTarget && !hoverTimer.Enabled,
                "the real WM_TIMER dwell message switches tabs once without sleeping or dropping");
            drop.Leave();
        }
        pane.SelectTab(0, focus: false);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0]]))
        {
            drop.Enter(hover);
            Check(hoverTimer.Enabled, "a fresh external hover re-arms dwell switching");
            drop.Leave();
            Check(!hoverTimer.Enabled && pane.ActiveTab != hoverTarget, "DragLeave cancels pending hover switching");
        }

        await OitReset(pane, folders[10], folders[11], folders[12]);
        var pinnedPrefix = pane.Tabs.Take(2).ToArray();
        pane.SetTabsPinned(pinnedPrefix, true);
        pane.SelectTab(2, focus: false);
        point = TgStripPoint(pane, pinnedPrefix[0].Button, after: false);
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[0], folders[1]]))
        {
            drop.Enter(point);
            var pinnedPlan = pane.FindStripDropPlan(point, null, null, externalPaths: [folders[0], folders[1]],
                minimumIndex: pane.PinnedCount);
            Check(pinnedPlan is { Allowed: true, InsertionIndex: 2 }
                && drop.Drop(point) == 4 && pane.PinnedCount == 2 && pane.Tabs.Take(2).SequenceEqual(pinnedPrefix)
                && pane.Tabs.Skip(2).Take(2).Select(OitPath).SequenceEqual(folders.Take(2))
                && pane.Tabs.Skip(2).Take(2).All(tab => !tab.Pinned),
                "external OLE drop over the pinned area moves its marker and inserts unpinned tabs after the prefix");
        }
        hoverTarget = pinnedPrefix[0];
        hover = pane.TabStrip.PointToScreen(new Point(hoverTarget.Button.Bounds.Left + hoverTarget.Button.Width / 2,
            hoverTarget.Button.Bounds.Top + hoverTarget.Button.Height / 2));
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[2]]))
        {
            drop.Enter(hover);
            Check(hoverTimer.Enabled && hoverTimer.Interval == 600 && pane.ActiveTab != hoverTarget,
                "external drag over an inactive pinned tab arms the same 600 ms dwell timer");
            OitFireTimer(hoverTimer);
            Check(pane.ActiveTab == hoverTarget && hoverTarget.Pinned,
                "external hover-to-switch activates a pinned tab without changing its pin");
            drop.Leave();
        }

        await OitReset(pane, folders[10], folders[11]);
        var plus = pane.TabStrip.Items.Cast<ToolStripItem>().Single(item => item.AccessibleName == "Pane 1 new tab");
        Point plusPoint = pane.TabStrip.PointToScreen(new Point(plus.Bounds.Left + plus.Bounds.Width / 2,
            plus.Bounds.Top + plus.Bounds.Height / 2));
        using (var drop = new OitOleDrop(pane.TabStrip, fixture, [folders[4]]))
        {
            drop.Enter(plusPoint);
            Check(drop.Drop(plusPoint) == 4 && pane.TabCount == 3 && OitPath(pane.Tabs[^1]) == folders[4],
                "the real plus-button drop appends a folder without creating an extra blank tab");
        }
        TgReset(pane, 0);
        var empty = (Control)typeof(ExplorerPane).GetField("emptyHint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!;
        Point emptyPoint = empty.PointToScreen(new Point(empty.ClientSize.Width / 2, empty.ClientSize.Height / 2));
        using (var drop = new OitOleDrop(empty, fixture, [folders[5]]))
        {
            drop.Enter(emptyPoint);
            Check(drop.Drop(emptyPoint) == 4 && pane.TabCount == 1 && OitPath(pane.ActiveTab!) == folders[5],
                "the registered empty-pane surface opens a real external folder without a prior tab");
        }

        // Exercise the same internal strip plan used by existing tab drags after external OLE registration.
        TgReset(pane, 4);
        var privatePayload = new DataObject();
        privatePayload.SetData(DataFormats.FileDrop, new[] { folders[0] });
        privatePayload.SetData(TabDockOverlay.DragFormat, false, "private-tab-drag");
        nint privateData = Marshal.GetComInterfaceForObject(privatePayload, typeof(System.Runtime.InteropServices.ComTypes.IDataObject));
        nint stripTarget = GetProp(pane.TabStrip.Handle, "OleDropTargetInterface");
        uint privateEffect = 7;
        Point privatePoint = TgStripPoint(pane, pane.Tabs[0].Button, after: false);
        try
        {
            Marshal.ThrowExceptionForHR(OitSlot<OitDragEnter>(stripTarget, 3)(stripTarget, privateData, 0, new OitPoint(privatePoint), ref privateEffect));
            Check(privateEffect == 0 && pane.TabCount == 4,
                "external target ignores a private tab drag even when it also advertises filesystem paths");
            Marshal.ThrowExceptionForHR(OitSlot<OitDragLeave>(stripTarget, 5)(stripTarget));
        }
        finally { Marshal.Release(privateData); }
        original = pane.Tabs.ToArray();
        var plan = pane.FindStripDropPlan(TgStripPoint(pane, original[0].Button, after: false), original[3], null);
        Check(plan is { Allowed: true, InsertionIndex: 0 } && pane.ReorderTab(original[3], plan.InsertionIndex.Value)
            && pane.Tabs.SequenceEqual(new[] { original[3], original[0], original[1], original[2] }),
            "external OLE registration leaves internal tab drag planning and strip reordering unchanged");
    }

    private static void OitFireTimer(System.Windows.Forms.Timer timer)
    {
        var window = typeof(System.Windows.Forms.Timer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.GetValue(timer)).OfType<NativeWindow>().Single();
        var idField = window.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.Name.Contains("timerID", StringComparison.OrdinalIgnoreCase));
        nint id = idField.GetValue(window) switch
        {
            nuint value => unchecked((nint)value),
            nint value => value,
            _ => throw new InvalidOperationException("Unexpected WinForms timer identifier type.")
        };
        if (window.Handle == 0 || id == 0) throw new InvalidOperationException("Hover timer has no active native timer window.");
        SendMessage(window.Handle, 0x113, id, 0);
    }

    private static int OitPixelDifference(Bitmap first, Bitmap second)
    {
        int count = 0;
        for (int y = 0; y < Math.Min(first.Height, second.Height); y++)
        for (int x = 0; x < Math.Min(first.Width, second.Width); x++)
            if (first.GetPixel(x, y) != second.GetPixel(x, y)) count++;
        return count;
    }

    // Drive the actual registered IDropTarget vtable, not Control.OnDrag* or an event delegate.
    // SHCreateDataObject supplies the same native CF_HDROP object as an Explorer drag.
    private sealed class OitOleDrop : IDisposable
    {
        private readonly nint target;
        private nint data;
        private readonly OitDragEnter enter;
        private readonly OitDragOver over;
        private readonly OitDragLeave leave;
        private readonly OitDragEnter drop;
        private System.Runtime.InteropServices.ComTypes.IDataObject? retainedSource;
        private bool entered;
        internal OitOleDrop(Control strip, string parent, string[] paths, string? onlyFormat = null)
        {
            target = GetProp(strip.Handle, "OleDropTargetInterface");
            if (target == 0) throw new InvalidOperationException("The tab strip has no registered OLE IDropTarget.");
            Marshal.AddRef(target);
            enter = OitSlot<OitDragEnter>(target, 3);
            over = OitSlot<OitDragOver>(target, 4);
            leave = OitSlot<OitDragLeave>(target, 5);
            drop = OitSlot<OitDragEnter>(target, 6);
            nint folder = 0;
            var items = new nint[paths.Length];
            try
            {
                Marshal.ThrowExceptionForHR(ShellNative.SHParseDisplayName(parent, 0, out folder, 0, out _));
                for (int i = 0; i < paths.Length; i++)
                    Marshal.ThrowExceptionForHR(ShellNative.SHParseDisplayName(paths[i], 0, out items[i], 0, out _));
                Guid iid = new("0000010E-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(OitSHCreateDataObject(folder, (uint)items.Length,
                    items.Select(ShellNative.ILFindLastID).ToArray(), 0, ref iid, out data));
                var native = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(data);
                try
                {
                    var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
                    Marshal.ThrowExceptionForHR(native.QueryGetData(ref format));
                    native.GetData(ref format, out var medium);
                    try { Check(ShellNative.DragQueryFile(medium.unionmember, uint.MaxValue, null, 0) == paths.Length,
                        "native OLE fixture carries every requested path in CF_HDROP"); }
                    finally { ShellNative.ReleaseStgMedium(ref medium); }
                    if (onlyFormat is not null)
                    {
                        short id = unchecked((short)DataFormats.GetFormat(onlyFormat).Id);
                        var advertised = new FORMATETC { cfFormat = id, dwAspect = DVASPECT.DVASPECT_CONTENT,
                            lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
                        Marshal.ThrowExceptionForHR(native.QueryGetData(ref advertised));
                        retainedSource = native;
                        Marshal.Release(data);
                        data = 0;
                        data = Marshal.GetComInterfaceForObject(new OitRestrictedData(native, advertised),
                            typeof(System.Runtime.InteropServices.ComTypes.IDataObject));
                    }
                }
                finally { if (retainedSource is null) Marshal.ReleaseComObject(native); }
            }
            catch { Dispose(); throw; }
            finally
            {
                if (folder != 0) ShellNative.ILFree(folder);
                foreach (nint item in items) if (item != 0) ShellNative.ILFree(item);
            }
        }
        internal uint Enter(Point point)
        {
            uint effect = 7;
            Marshal.ThrowExceptionForHR(enter(target, data, 0, new OitPoint(point), ref effect));
            entered = true;
            return effect;
        }
        internal uint Over(Point point)
        {
            uint effect = 7;
            Marshal.ThrowExceptionForHR(over(target, 0, new OitPoint(point), ref effect));
            return effect;
        }
        internal uint Drop(Point point)
        {
            uint effect = 7;
            Marshal.ThrowExceptionForHR(drop(target, data, 0, new OitPoint(point), ref effect));
            entered = false;
            return effect;
        }
        internal void Leave()
        {
            if (!entered) return;
            Marshal.ThrowExceptionForHR(leave(target));
            entered = false;
        }
        public void Dispose()
        {
            Leave();
            if (data != 0) { Marshal.Release(data); data = 0; }
            if (retainedSource is not null) { Marshal.ReleaseComObject(retainedSource); retainedSource = null; }
            Marshal.Release(target);
        }
    }

    // A real COM IDataObject exposing one of the native Shell object's existing representations.
    // Restricting the format forces the receiver's non-HDROP decoding path without synthetic PIDLs.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class OitRestrictedData(System.Runtime.InteropServices.ComTypes.IDataObject source, FORMATETC format)
        : System.Runtime.InteropServices.ComTypes.IDataObject
    {
        private const int FormatUnavailable = unchecked((int)0x80040064);
        public void GetData(ref FORMATETC requested, out STGMEDIUM medium)
        {
            Marshal.ThrowExceptionForHR(QueryGetData(ref requested));
            source.GetData(ref requested, out medium);
        }
        public void GetDataHere(ref FORMATETC requested, ref STGMEDIUM medium)
        {
            Marshal.ThrowExceptionForHR(QueryGetData(ref requested));
            source.GetDataHere(ref requested, ref medium);
        }
        public int QueryGetData(ref FORMATETC requested) =>
            requested.cfFormat == format.cfFormat ? source.QueryGetData(ref requested) : FormatUnavailable;
        public int GetCanonicalFormatEtc(ref FORMATETC requested, out FORMATETC canonical) =>
            source.GetCanonicalFormatEtc(ref requested, out canonical);
        public void SetData(ref FORMATETC requested, ref STGMEDIUM medium, bool release) =>
            source.SetData(ref requested, ref medium, release);
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            if (direction != DATADIR.DATADIR_GET) return source.EnumFormatEtc(direction);
            Marshal.ThrowExceptionForHR(OitSHCreateStdEnumFmtEtc(1, [format], out nint pointer));
            try { return (IEnumFORMATETC)Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
        }
        public int DAdvise(ref FORMATETC requested, ADVF flags, IAdviseSink sink, out int connection) =>
            source.DAdvise(ref requested, flags, sink, out connection);
        public void DUnadvise(int connection) => source.DUnadvise(connection);
        public int EnumDAdvise(out IEnumSTATDATA? enumerator) => source.EnumDAdvise(out enumerator);
    }

    private static T OitSlot<T>(nint pointer, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), index * nint.Size));
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct OitPoint(Point point) { public readonly int X = point.X, Y = point.Y; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OitDragEnter(nint target, nint data, uint keys, OitPoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OitDragOver(nint target, uint keys, OitPoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OitDragLeave(nint target);
    [DllImport("shell32.dll", EntryPoint = "SHCreateDataObject")]
    private static extern int OitSHCreateDataObject(nint parent, uint count, [In] nint[] children,
        nint inner, ref Guid iid, out nint data);
    [DllImport("shell32.dll", EntryPoint = "SHCreateStdEnumFmtEtc")]
    private static extern int OitSHCreateStdEnumFmtEtc(uint count, [In] FORMATETC[] formats, out nint enumerator);
}
