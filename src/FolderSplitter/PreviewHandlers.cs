using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace QuadExplorer;

// Hosts the same preview handlers File Explorer uses (images, PDF, Office, text). Handlers are
// started out of process in Windows' prevhost.exe surrogate when registered for it, so a crashing
// handler cannot take the app down; handlers that opt out of isolation load in process.
internal static class PreviewHandlers
{
    private const string PreviewHandlerExtension = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    private static readonly Guid PreviewHandlerId = new("8895b1c6-b41f-4c1c-a562-0d564250836f");

    internal static Guid? Find(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        uint length = 64;
        var value = new StringBuilder((int)length);
        // ASSOCF_INIT_DEFAULTTOSTAR | ASSOCF_NOTRUNCATE, ASSOCSTR_SHELLEXTENSION
        int result = AssocQueryString(0x4 | 0x20, 16, extension, PreviewHandlerExtension, value, ref length);
        return result == 0 && Guid.TryParse(value.ToString(), out var id) ? id : null;
    }

    internal static IPreviewHandler Create(Guid handler, string path)
    {
        Guid iid = PreviewHandlerId;
        // CLSCTX_LOCAL_SERVER first (isolated surrogate), then CLSCTX_INPROC_SERVER.
        if (CoCreateInstance(ref handler, 0, 0x4, ref iid, out object instance) < 0
            && CoCreateInstance(ref handler, 0, 0x1, ref iid, out instance) < 0)
            throw new COMException("The preview handler could not be started.");
        var preview = (IPreviewHandler)instance;
        try
        {
            Initialize(preview, path);
            return preview;
        }
        catch
        {
            Marshal.FinalReleaseComObject(preview);
            throw;
        }
    }

    private static void Initialize(object handler, string path)
    {
        if (handler is IInitializeWithStream withStream)
        {
            Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(path, 0x40, 0, false, null, out var stream)); // STGM_READ|STGM_SHARE_DENY_NONE
            withStream.Initialize(stream, 0);
            return;
        }
        if (handler is IInitializeWithItem withItem)
        {
            Guid shellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref shellItem, out object item));
            withItem.Initialize(item, 0);
            return;
        }
        if (handler is IInitializeWithFile withFile)
        {
            withFile.Initialize(path, 0);
            return;
        }
        throw new COMException("The preview handler does not accept files.");
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, int value, string association, string extra,
        StringBuilder output, ref uint length);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object instance);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create,
        IStream? template, out IStream stream);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object item);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PreviewRect(int left, int top, int right, int bottom)
{
    public int Left = left, Top = top, Right = right, Bottom = bottom;
}

[ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPreviewHandler
{
    void SetWindow(nint parent, ref PreviewRect rectangle);
    void SetRect(ref PreviewRect rectangle);
    void DoPreview();
    void Unload();
    void SetFocus();
    void QueryFocus(out nint window);
    [PreserveSig] int TranslateAccelerator(nint message);
}

[ComImport, Guid("196bf9a5-b346-4ef0-aa1e-5dcdb76768b1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPreviewHandlerVisuals
{
    void SetBackgroundColor(int color);
    void SetFont(nint font);
    void SetTextColor(int color);
}

[ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithStream
{
    void Initialize(IStream stream, uint mode);
}

[ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithItem
{
    void Initialize([MarshalAs(UnmanagedType.Interface)] object item, uint mode);
}

[ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode);
}
