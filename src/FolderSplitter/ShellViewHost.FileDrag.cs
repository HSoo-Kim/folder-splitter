using System.Runtime.InteropServices;

namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private bool fileSelectionButtonDown;
    private bool fileSelectionControlDown;
    private bool fileSelectionHitItem;

    private FolderSplitterApplicationContext? FileDragContext => (FindForm() as MainForm)?.ApplicationContext;

    private void FileSelectionMouseDown(ref Message message)
    {
        var hit = new OpenInTabNative.ListHitTest
        {
            Point = new Point(unchecked((short)(long)message.LParam), unchecked((short)((long)message.LParam >> 16)))
        };
        bool onItem = OpenInTabNative.HitTest(message.HWnd, 0x1039, 0, ref hit) >= 0
            && hit.Item >= 0 && (hit.Flags & 0x000E) != 0; // LVM_SUBITEMHITTEST / LVHT_ONITEM
        bool control = ((long)message.WParam & 0x0008) != 0; // MK_CONTROL
        bool selected = onItem && ((long)SendMessage(message.HWnd, 0x102C, hit.Item, 0x0002) & 0x0002) != 0; // LVM_GETITEMSTATE
        fileSelectionButtonDown = true;
        fileSelectionControlDown = control;
        fileSelectionHitItem = onItem;
        FileDragContext?.CrossViewFileMouseDown(this, control, selected);
    }

    private void CompleteFileSelectionClick()
    {
        if (!fileSelectionButtonDown) return;
        fileSelectionButtonDown = false;
        FileDragContext?.CrossViewFileMouseUp(this, fileSelectionControlDown && fileSelectionHitItem);
        fileSelectionControlDown = false;
        fileSelectionHitItem = false;
    }

    private bool TryBeginCrossViewFileDrag(nint notification)
    {
        nint list = detailsList?.Handle ?? 0;
        if (list == 0 || notification == 0 || Marshal.ReadIntPtr(notification) != list
            || Marshal.ReadInt32(notification, 2 * IntPtr.Size) != -109) return false; // LVN_BEGINDRAG
        var context = FileDragContext;
        if (context is null) return false;
        if (fileSelectionButtonDown && fileSelectionControlDown && fileSelectionHitItem)
            context.RecordCrossViewFileSelection(this);
        bool handled = context.TryStartCrossViewFileDrag(this, list);
        if (handled)
        {
            fileSelectionButtonDown = false;
            fileSelectionControlDown = false;
            fileSelectionHitItem = false;
        }
        return handled;
    }

    private void CancelCrossViewFileSelection()
    {
        fileSelectionButtonDown = false;
        fileSelectionControlDown = false;
        fileSelectionHitItem = false;
        FileDragContext?.ClearCrossViewFileSelection();
    }
}
