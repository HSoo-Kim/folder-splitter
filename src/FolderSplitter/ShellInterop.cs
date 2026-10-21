using System.Runtime.InteropServices;

namespace QuadExplorer;

// The vtable order follows the Windows SDK's shobjidl_core.h.
internal static class ShellNative
{
    public static readonly Guid BrowserClass = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    public const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHParseDisplayName(string name, nint bindingContext,
        out nint pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll")]
    public static extern nint GetFocus();

    [DllImport("user32.dll")]
    public static extern nint SetFocus(nint window);

    public static string? Name(nint pidl, uint nameType)
    {
        if (SHGetNameFromIDList(pidl, nameType, out nint value) < 0) return null;
        try { return Marshal.PtrToStringUni(value); }
        finally { Marshal.FreeCoTaskMem(value); }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left, Top, Right, Bottom;
    public NativeRect(int width, int height) { Left = Top = 0; Right = width; Bottom = height; }
}

[StructLayout(LayoutKind.Sequential)]
public struct FolderSettings
{
    public uint ViewMode;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeMessage
{
    public nint Window;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public int X, Y;
    public uint Private;
}

[ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowser
{
    void Initialize(nint parent, ref NativeRect rectangle, ref FolderSettings settings);
    void Destroy();
    void SetRect(nint deferWindowPosition, NativeRect rectangle);
    void SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string propertyBag);
    void SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string emptyText);
    void SetFolderSettings(ref FolderSettings settings);
    void Advise([MarshalAs(UnmanagedType.Interface)] IExplorerBrowserEvents events, out uint cookie);
    void Unadvise(uint cookie);
    void SetOptions(uint flags);
    void GetOptions(out uint flags);
    void BrowseToIDList(nint pidl, uint flags);
    void BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object target, uint flags);
    void FillFromObject(nint dataObject, uint flags);
    void RemoveAll();
    void GetCurrentView(ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object view);
}

[ComVisible(true), Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(nint pidlFolder);
    [PreserveSig] int OnViewCreated([MarshalAs(UnmanagedType.Interface)] IShellView view);
    [PreserveSig] int OnNavigationComplete(nint pidlFolder);
    [PreserveSig] int OnNavigationFailed(nint pidlFolder);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellView
{
    [PreserveSig] int GetWindow(out nint window);
    [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    [PreserveSig] int TranslateAccelerator(ref NativeMessage message);
    [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);
    [PreserveSig] int UIActivate(uint state);
    [PreserveSig] int Refresh();
    [PreserveSig] int CreateViewWindow(nint previous, nint settings, nint browser, nint rectangle, out nint window);
    [PreserveSig] int DestroyViewWindow();
    [PreserveSig] int GetCurrentInfo(out FolderSettings settings);
    [PreserveSig] int AddPropertySheetPages(uint reserved, nint callback, nint lParam);
    [PreserveSig] int SaveViewState();
    [PreserveSig] int SelectItem(nint pidl, uint flags);
    [PreserveSig] int GetItemObject(uint item, ref Guid interfaceId, out nint result);
}
