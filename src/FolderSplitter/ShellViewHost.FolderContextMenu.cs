using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    internal event Action<IReadOnlyList<string>>? OpenFoldersInNewWindowRequested;
    internal event Action<IReadOnlyList<string>>? AddFoldersToFavoritesRequested;

    private const uint FolderMenuFirstCommand = 1;
    private const uint FolderMenuLastCommand = 0x6FFF;
    private const uint FolderMenuNewTabCommand = 0x7000;
    private const uint FolderMenuNewWindowCommand = 0x7001;
    private const uint FolderMenuFavoritesCommand = 0x7002;
    private IContextMenu? activeFolderContextMenu;
    private bool folderMenuAppsArmed, folderMenuF10Handled;

    private bool TryFolderContextMenuKey(ref Message message)
    {
        if (detailsList is not { Handle: var list } || list == 0 || message.HWnd != list) return false;
        if (message.Msg == 0x0100 && message.WParam == 0x5D) // WM_KEYDOWN, VK_APPS
        {
            if (OpenFoldersInTabsRequested is null) return false;
            try { if (SelectedTabFolders().Count == 0) return false; }
            catch (ExternalException) { return false; }
            folderMenuAppsArmed = true;
            message.Result = 0;
            return true; // Reserve the paired keyup so Shell accelerators cannot also open a menu.
        }
        bool apps = message.Msg == 0x0101 && message.WParam == 0x5D && folderMenuAppsArmed;
        if (apps) folderMenuAppsArmed = false;
        if ((message.Msg is 0x0101 or 0x0105) && message.WParam == 0x79 && folderMenuF10Handled) // VK_F10
        {
            folderMenuF10Handled = false;
            message.Result = 0;
            return true;
        }
        bool shiftF10 = (message.Msg is 0x0100 or 0x0104) && message.WParam == 0x79
            && OpenInTabNative.GetKeyState(0x10) < 0 && OpenInTabNative.GetKeyState(0x11) >= 0
            && OpenInTabNative.GetKeyState(0x12) >= 0;
        if (!apps && !shiftF10) return false;
        if (shiftF10 && folderMenuF10Handled) { message.Result = 0; return true; }
        var context = Message.Create(list, 0x007B, list, -1); // WM_CONTEXTMENU, focused-item anchor
        if (shiftF10) folderMenuF10Handled = true; // The queued keyup can arrive inside the modal menu loop.
        if (!TryFolderContextMenuMessage(ref context))
        {
            if (shiftF10) folderMenuF10Handled = false;
            return false;
        }
        message.Result = 0;
        return true;
    }

    private bool TryFolderContextMenuMessage(ref Message message)
    {
        if (activeFolderContextMenu is { } active)
        {
            // Shell extensions own their submenu population, owner drawing and mnemonic handling.
            // The list owns TrackPopupMenuEx, so these messages come through its existing subclass.
            try
            {
                if (message.Msg is 0x0117 or 0x0120 || message.Msg is 0x002B or 0x002C && message.WParam == 0)
                {
                    if (active is IContextMenu3 menu3
                        && menu3.HandleMenuMsg2((uint)message.Msg, (nuint)message.WParam, message.LParam, out nint result) == 0)
                    {
                        message.Result = result;
                        return true;
                    }
                    if (active is IContextMenu2 menu2
                        && menu2.HandleMenuMsg((uint)message.Msg, (nuint)message.WParam, message.LParam) == 0)
                    {
                        message.Result = message.Msg is 0x002B or 0x002C ? 1 : 0;
                        return true;
                    }
                }
            }
            catch (ExternalException) { return false; } // A failed extension must not escape the native window callback.
            return message.Msg == 0x007B; // Do not enter a second modal popup while this one is tracking.
        }
        if (message.Msg != 0x007B || view is null || OpenFoldersInTabsRequested is null) return false;
        if (message.LParam != -1)
        {
            var mousePoint = new Point(unchecked((short)(long)message.LParam), unchecked((short)((long)message.LParam >> 16)));
            if (!FolderMenuNative.ScreenToClient(message.HWnd, ref mousePoint)) return false;
            var hit = new OpenInTabNative.ListHitTest { Point = mousePoint };
            nint hitResult = OpenInTabNative.HitTest(message.HWnd, 0x1039, 0, ref hit);
            if (hitResult < 0 || hit.Item < 0 || (hit.Flags & 0x000E) == 0)
                return false; // A selected folder does not turn blank-space/background right-click into an item menu.
        }
        IReadOnlyList<string> folders;
        try { folders = SelectedTabFolders(); }
        catch (ExternalException) { return false; }
        if (folders.Count == 0) return false; // Background and file-only menus remain wholly native.
        Guid menuId = typeof(IContextMenu).GUID;
        IContextMenu menu;
        try
        {
            if (view.GetItemObject(1, ref menuId, out nint pointer) < 0 || pointer == 0) return false; // SVGIO_SELECTION
            try { menu = (IContextMenu)Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
        }
        catch (ExternalException) { return false; }
        nint popup = CreatePopupMenu();
        if (popup == 0)
        {
            Marshal.ReleaseComObject(menu);
            return false;
        }
        Point point = FolderContextMenuPoint(message);
        nint verbBuffer = Marshal.AllocHGlobal(1024);
        activeFolderContextMenu = menu;
        try
        {
            // Restrict the fallback catch to extension calls during construction, never app events.
            try
            {
                uint flags = 0x10u | (OpenInTabNative.GetKeyState(0x10) < 0 ? 0x100u : 0u); // CANRENAME | EXTENDEDVERBS
                if (menu.QueryContextMenu(popup, 0, FolderMenuFirstCommand, FolderMenuLastCommand, flags) < 0)
                    return false;
                uint appItems = 0;
                if (!HasFolderMenuCommand(menu, popup, verbBuffer, "opennewtab"))
                {
                    if (!FolderMenuNative.InsertMenuW(popup, 0, 0x0400, FolderMenuNewTabCommand, text.T("Open in new tab")))
                        return false; // MF_BYPOSITION | MF_STRING
                    appItems = 1;
                }
                // DefView adds Open in new window outside the raw IContextMenu payload on Win11.
                // Preserve that host command without depending on DefView's private command IDs.
                if (OpenFoldersInNewWindowRequested is not null && !HasFolderMenuCommand(menu, popup, verbBuffer, "opennewwindow"))
                {
                    if (!FolderMenuNative.InsertMenuW(popup, 1, 0x0400, FolderMenuNewWindowCommand, text.T("Open in new window")))
                        return false;
                    appItems = 2;
                }
                // Windows' own "Pin to Quick access" (localized as a favorites pin) changes File Explorer only,
                // so the app's sidebar favorites get their own, explicitly named command next to the app's items.
                if (AddFoldersToFavoritesRequested is not null
                    && !FolderMenuNative.InsertMenuW(popup, appItems, 0x0400, FolderMenuFavoritesCommand,
                        text.T("Add to Folder Splitter favorites")))
                    return false;
            }
            catch (ExternalException) { return false; } // Cleanup below, then the original DefView builds its own menu.
            uint selected = FolderMenuNative.TrackPopupMenuEx(popup, 0x0102, point.X, point.Y, message.HWnd, 0); // RETURNCMD | RIGHTBUTTON
            activeFolderContextMenu = null;
            if (selected != 0)
            {
                string? verb = null;
                ExternalException? failure = null;
                try
                {
                    if (selected != FolderMenuNewTabCommand && selected != FolderMenuNewWindowCommand
                        && selected != FolderMenuFavoritesCommand)
                        verb = FolderContextMenuVerb(menu, selected, verbBuffer);
                }
                catch (ExternalException error) { failure = error; }
                if (failure is not null)
                {
                    ReportFolderContextMenuFailure(failure);
                    message.Result = 0;
                    return true;
                }
                if (selected == FolderMenuNewTabCommand || string.Equals(verb, "opennewtab", StringComparison.OrdinalIgnoreCase))
                    OpenFoldersInTabsRequested.Invoke(folders, false);
                else if ((selected == FolderMenuNewWindowCommand || string.Equals(verb, "opennewwindow", StringComparison.OrdinalIgnoreCase))
                    && OpenFoldersInNewWindowRequested is { } openWindow)
                    openWindow(folders);
                else if (selected == FolderMenuFavoritesCommand && AddFoldersToFavoritesRequested is { } addFavorites)
                    addFavorites(folders);
                else
                {
                    try
                    {
                        // DefView owns in-place Rename and default Open navigation; raw item
                        // handlers cannot reproduce the hosting view's editor/browser callbacks.
                        bool rename = string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase);
                        bool open = string.Equals(verb, "open", StringComparison.OrdinalIgnoreCase);
                        int result = view is IFolderView2 folderView && (rename || open)
                            ? rename ? folderView.DoRename() : folderView.InvokeVerbOnSelection("open")
                            : InvokeFolderContextMenuCommand(menu, selected, message.HWnd, point);
                        if (result < 0) failure = Marshal.GetExceptionForHR(result) as ExternalException ?? new COMException(null, result);
                    }
                    catch (ExternalException error) { failure = error; }
                    if (failure is not null) ReportFolderContextMenuFailure(failure);
                }
            }
            message.Result = 0;
            return true;
        }
        finally
        {
            activeFolderContextMenu = null;
            DestroyMenu(popup);
            Marshal.FreeHGlobal(verbBuffer);
            Marshal.ReleaseComObject(menu);
        }
    }

    private void ReportFolderContextMenuFailure(ExternalException error) =>
        NavigationError?.Invoke(this, text.T("Context menu command failed: {0}", error.Message));

    private Point FolderContextMenuPoint(Message message)
    {
        if (message.LParam != -1)
            return new Point(unchecked((short)(long)message.LParam), unchecked((short)((long)message.LParam >> 16)));
        // Keyboard context menus anchor to the focused item, rather than the unrelated cursor.
        Point point = Point.Empty;
        if (view is IFolderView folderView && folderView.GetFocusedItem(out int index) >= 0 && index >= 0)
        {
            var bounds = new DetailsNative.Rect { Left = 2 }; // LVIR_LABEL
            if (DetailsNative.SendMessage(message.HWnd, 0x100E, index, ref bounds) != 0) // LVM_GETITEMRECT
                point = new Point(bounds.Left, bounds.Bottom);
        }
        FolderMenuNative.ClientToScreen(message.HWnd, ref point);
        return point;
    }

    private static bool HasFolderMenuCommand(IContextMenu handler, nint menu, nint verbBuffer, string verb)
    {
        for (int i = 0, count = FolderMenuNative.GetMenuItemCount(menu); i < count; i++)
        {
            uint id = FolderMenuNative.GetMenuItemID(menu, i);
            if (id is >= FolderMenuFirstCommand and <= FolderMenuLastCommand
                && string.Equals(FolderContextMenuVerb(handler, id, verbBuffer), verb, StringComparison.OrdinalIgnoreCase))
                return true;
            nint submenu = FolderMenuNative.GetSubMenu(menu, i);
            if (submenu != 0 && HasFolderMenuCommand(handler, submenu, verbBuffer, verb)) return true;
        }
        return false;
    }

    private static string? FolderContextMenuVerb(IContextMenu menu, uint command, nint name)
    {
        if (command < FolderMenuFirstCommand || command > FolderMenuLastCommand) return null;
        Marshal.WriteInt16(name, 0);
        return menu.GetCommandString(command - FolderMenuFirstCommand, 4, 0, name, 512) >= 0
            ? Marshal.PtrToStringUni(name) : null; // GCS_VERBW, independent of menu language or position
    }

    private static int InvokeFolderContextMenuCommand(IContextMenu menu, uint command, nint owner, Point point)
    {
        nint info = Marshal.AllocHGlobal(Marshal.SizeOf<InvokeCommandInfo>());
        try
        {
            // Numeric offsets preserve every extension's command without guessing its display text.
            nint offset = (nint)(command - FolderMenuFirstCommand);
            uint modifiers = (OpenInTabNative.GetKeyState(0x10) < 0 ? 0x10000000u : 0u)
                | (OpenInTabNative.GetKeyState(0x11) < 0 ? 0x40000000u : 0u); // SHIFT_DOWN | CONTROL_DOWN
            Marshal.StructureToPtr(new InvokeCommandInfo
            {
                Size = Marshal.SizeOf<InvokeCommandInfo>(), Mask = 0x20004000 | modifiers, // PTINVOKE | UNICODE
                Window = owner, Verb = offset, VerbW = offset, Show = 1, X = point.X, Y = point.Y
            }, info, false);
            return menu.InvokeCommand(info);
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    private static class FolderMenuNative
    {
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window, ref Point point);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint window, ref Point point);
        [DllImport("user32.dll")] internal static extern int GetMenuItemCount(nint menu);
        [DllImport("user32.dll")] internal static extern uint GetMenuItemID(nint menu, int position);
        [DllImport("user32.dll")] internal static extern nint GetSubMenu(nint menu, int position);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern bool InsertMenuW(nint menu, uint position, uint flags, nuint command, string text);
        [DllImport("user32.dll")] internal static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    }
}
