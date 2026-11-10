using System.Runtime.InteropServices;

namespace QuadExplorer;

internal sealed record ExplorerFavoritesExport(int Pinned, int AlreadyPinned, int Failed, bool Unreadable = false);

public static partial class ExplorerFavorites
{
    private static readonly Guid BhidSfuiObject = new("3981E225-F559-11D3-8E3A-00C04F6837D5");

    // Pins through the Shell command behind File Explorer's own "Pin to Quick access". That changes the user's
    // Explorer state, so only the explicit Export command calls this. The command toggles: run on a folder that is
    // already pinned, it unpins it, and the same may happen when another path reaches a pinned folder. Unknown pin
    // state therefore exports nothing; a pin that removed an existing pin is toggled back, and export stops if
    // Quick Access cannot be confirmed.
    internal static ExplorerFavoritesExport Export(IReadOnlyList<string> favorites, nint owner) =>
        ReadPinnedQuickAccess() is { } pinned ? PinPlanned(PlanExport(favorites, pinned), pinned, owner)
            : new ExplorerFavoritesExport(0, 0, 0, Unreadable: true);

    // Pins each planned folder and confirms the change in Quick Access, given the pins read before planning.
    internal static ExplorerFavoritesExport PinPlanned(ExplorerFavoritesExportPlan plan, IReadOnlyList<string> pinned, nint owner)
    {
        int done = 0, alreadyPinned = plan.AlreadyPinned, failed = plan.Unavailable;
        for (int i = 0; i < plan.ToPin.Count; i++)
        {
            string path = plan.ToPin[i];
            if (!Pin(path, owner)) { failed++; continue; } // Nothing was invoked, so the state is unchanged.
            var after = AwaitPins(pins => Lost(pins, pinned) || Lost(pinned, pins));
            if (after is not null && !Lost(pinned, after))
            {
                done++;
                pinned = after;
                continue;
            }
            // An existing pin was removed instead: toggle it back, and stop unless Quick Access is restored.
            if (after is not null && Pin(path, owner) && AwaitPins(pins => !Lost(pinned, pins)) is { } restored)
            {
                alreadyPinned++;
                pinned = restored;
                continue;
            }
            failed += plan.ToPin.Count - i;
            break;
        }
        return new ExplorerFavoritesExport(done, alreadyPinned, failed);
    }

    // True when a path in the first list is missing from the second.
    private static bool Lost(IReadOnlyList<string> before, IReadOnlyList<string> after) =>
        before.Any(path => !after.Contains(path, StringComparer.OrdinalIgnoreCase));

    // Quick Access reflects a pin change shortly after the command returns; null when the state stays unknown.
    private static IReadOnlyList<string>? AwaitPins(Func<IReadOnlyList<string>, bool> settled)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var pins = ReadPinnedQuickAccess();
            if (pins is not null && settled(pins)) return pins;
            if (watch.ElapsedMilliseconds > 3000) return null;
            Thread.Sleep(50);
        }
    }

    private static bool Pin(string path, nint owner)
    {
        Guid itemId = typeof(IPinShellItem).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref itemId, out IPinShellItem? item) < 0 || item is null) return false;
        nint pointer = 0, popup = 0, name = 0, info = 0;
        IContextMenu? menu = null;
        try
        {
            Guid handler = BhidSfuiObject, menuId = typeof(IContextMenu).GUID;
            if (item.BindToHandler(0, ref handler, ref menuId, out pointer) < 0 || pointer == 0) return false;
            menu = (IContextMenu)Marshal.GetObjectForIUnknown(pointer);
            popup = CreatePopupMenu();
            // CMF_NORMAL: CMF_OPTIMIZEFORINVOKE omits Windows' static verbs, including the Quick Access pin.
            if (popup == 0 || menu.QueryContextMenu(popup, 0, 1, 0x7FFF, 0) < 0) return false;
            // Invoke the folder menu's own pintohome item by its position, so no other command (such as Open) can run.
            name = Marshal.AllocHGlobal(1024);
            nint offset = -1;
            for (int i = 0, count = GetMenuItemCount(popup); i < count && offset < 0; i++)
            {
                uint id = GetMenuItemID(popup, i);
                if (id is < 1 or > 0x7FFF) continue;
                Marshal.WriteInt16(name, 0);
                if (menu.GetCommandString(id - 1, 4, 0, name, 512) >= 0 // GCS_VERBW
                    && Marshal.PtrToStringUni(name) == "pintohome") offset = (nint)(id - 1);
            }
            if (offset < 0) return false;
            info = Marshal.AllocHGlobal(Marshal.SizeOf<InvokeCommandInfo>());
            Marshal.StructureToPtr(new InvokeCommandInfo
            {
                Size = Marshal.SizeOf<InvokeCommandInfo>(), Mask = 0x4000 | 0x400, // CMIC_MASK_UNICODE | FLAG_NO_UI
                Window = owner, Verb = offset, VerbW = offset, Show = 1
            }, info, false);
            return menu.InvokeCommand(info) >= 0;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException) { return false; }
        finally
        {
            if (info != 0) Marshal.FreeHGlobal(info);
            if (name != 0) Marshal.FreeHGlobal(name);
            if (popup != 0) DestroyMenu(popup);
            if (menu is not null) Marshal.ReleaseComObject(menu);
            if (pointer != 0) Marshal.Release(pointer);
            Marshal.ReleaseComObject(item);
        }
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPinShellItem
    {
        [PreserveSig] int BindToHandler(nint bindingContext, ref Guid handler, ref Guid interfaceId, out nint result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindingContext, ref Guid interfaceId,
        out IPinShellItem? item);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(nint menu, int position);
}
