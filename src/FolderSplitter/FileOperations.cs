using System.Runtime.InteropServices;

namespace QuadExplorer;

// Copies or moves items with Windows' own IFileOperation, so progress, conflicts, undo and
// recycle-bin rules match File Explorer. It runs on its own STA thread to keep the window responsive.
internal static class FileOperations
{
    private const uint AllowUndo = 0x40, NoConfirmMakeDirectory = 0x200, AddUndoRecord = 0x20000000;
    private const int Cancelled = unchecked((int)0x800704C7);

    internal static void Start(IReadOnlyList<string> sources, string destination, bool move, nint owner,
        Action<bool, string?> completed)
    {
        var thread = new Thread(() =>
        {
            bool done = false;
            string? error = null;
            try { done = Run(sources, destination, move, owner); }
            catch (COMException exception) when (exception.HResult == Cancelled) { }
            catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException
                or UnauthorizedAccessException) { error = exception.Message; }
            completed(done, error);
        }) { IsBackground = true, Name = "File operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static bool Run(IReadOnlyList<string> sources, string destination, bool move, nint owner)
    {
        var type = Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), throwOnError: true)!;
        var operation = (IFileOperation)Activator.CreateInstance(type)!;
        var items = new List<IShellItem>();
        try
        {
            operation.SetOwnerWindow(owner);
            operation.SetOperationFlags(AllowUndo | NoConfirmMakeDirectory | AddUndoRecord);
            var target = ShellItem(destination);
            items.Add(target);
            foreach (string source in sources)
            {
                var item = ShellItem(source);
                items.Add(item);
                if (move) operation.MoveItem(item, target, null, 0);
                else operation.CopyItem(item, target, null, 0);
            }
            operation.PerformOperations();
            return !operation.GetAnyOperationsAborted();
        }
        finally
        {
            foreach (var item in items) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    // Renames several items in one operation, so one Undo in File Explorer restores them all.
    internal static void StartRename(IReadOnlyList<(string Path, string Name)> renames, nint owner, Action<bool, string?> completed)
    {
        var thread = new Thread(() =>
        {
            bool done = false;
            string? error = null;
            try { done = RunRename(renames, owner); }
            catch (COMException exception) when (exception.HResult == Cancelled) { }
            catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException
                or UnauthorizedAccessException) { error = exception.Message; }
            completed(done, error);
        }) { IsBackground = true, Name = "Rename operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static bool RunRename(IReadOnlyList<(string Path, string Name)> renames, nint owner)
    {
        var type = Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), throwOnError: true)!;
        var operation = (IFileOperation)Activator.CreateInstance(type)!;
        var items = new List<IShellItem>();
        try
        {
            operation.SetOwnerWindow(owner);
            operation.SetOperationFlags(AllowUndo | AddUndoRecord);
            foreach (var (path, name) in renames)
            {
                var item = ShellItem(path);
                items.Add(item);
                operation.RenameItem(item, name, 0);
            }
            operation.PerformOperations();
            return !operation.GetAnyOperationsAborted();
        }
        finally
        {
            foreach (var item in items) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    private static IShellItem ShellItem(string path)
    {
        Guid shellItem = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref shellItem, out IShellItem item));
        return item;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid interfaceId,
        out IShellItem item);

    // Only passed through to IFileOperation, so no members are declared.
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem;

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
        void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination,
            [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
        void MoveItems(nint items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination,
            [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
        void CopyItems(nint items, IShellItem destination);
        void DeleteItem(nint item, nint sink);
        void DeleteItems(nint items);
        void NewItem(nint folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, nint sink);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
    }
}
