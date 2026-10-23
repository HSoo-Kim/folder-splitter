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

// Windows 11 registers no preview handler for photos or videos; File Explorer shows the shell's
// large thumbnail for them instead. THUMBNAILONLY keeps file-type icons out of the preview.
internal static class ShellThumbnail
{
    internal static Bitmap? Load(string path, int size)
    {
        Guid factoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        if (SHCreateItemFromParsingName(path, 0, ref factoryId, out object item) < 0) return null;
        nint bitmap = 0;
        try
        {
            if (((IShellItemImageFactory)item).GetImage(new NativeSize(size, size), 0x1 | 0x8, out bitmap) < 0 || bitmap == 0)
                return null; // SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY
            return ToBitmap(bitmap);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or ArgumentException
            or ExternalException or OverflowException) { return null; }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            Marshal.FinalReleaseComObject(item);
        }
    }

    // Image.FromHbitmap drops the alpha channel. Reread the pixels as ARGB, unless every alpha byte is
    // zero (opaque photos often come back that way), in which case the opaque copy is already right.
    private static Bitmap ToBitmap(nint handle)
    {
        var opaque = Image.FromHbitmap(handle);
        if (Image.GetPixelFormatSize(opaque.PixelFormat) != 32) return opaque;
        var area = new Rectangle(0, 0, opaque.Width, opaque.Height);
        // Bottom-up bitmaps report a negative stride, so copy row by row from Scan0.
        int rowBytes = area.Width * 4;
        var bytes = new byte[rowBytes * area.Height];
        var data = opaque.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, opaque.PixelFormat);
        try
        {
            for (int y = 0; y < area.Height; y++)
                Marshal.Copy(data.Scan0 + (nint)y * data.Stride, bytes, y * rowBytes, rowBytes);
        }
        finally { opaque.UnlockBits(data); }
        bool hasAlpha = false;
        for (int i = 3; i < bytes.Length && !hasAlpha; i += 4) hasAlpha = bytes[i] != 0;
        if (!hasAlpha) return opaque;
        opaque.Dispose();
        var result = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var target = result.LockBits(area, System.Drawing.Imaging.ImageLockMode.WriteOnly, result.PixelFormat);
        try
        {
            for (int y = 0; y < area.Height; y++)
                Marshal.Copy(bytes, y * rowBytes, target.Scan0 + (nint)y * target.Stride, rowBytes);
        }
        finally { result.UnlockBits(target); }
        return result;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize(int width, int height)
{
    public int Width = width, Height = height;
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(NativeSize size, int flags, out nint bitmap);
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
