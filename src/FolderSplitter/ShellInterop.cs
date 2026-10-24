using System.Runtime.InteropServices;

namespace QuadExplorer;

// The vtable order follows the Windows SDK's shobjidl_core.h.
internal static class ShellNative
{
    public static readonly Guid BrowserClass = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    public const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    public static readonly string Recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent,
        Environment.SpecialFolderOption.DoNotVerify);
    public const uint SvgioAllView = 0x2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHParseDisplayName(string name, nint bindingContext,
        out nint pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);

    [DllImport("shell32.dll")]
    public static extern nint ILFindLastID(nint pidl);

    [DllImport("shell32.dll")]
    public static extern nint ILCombine(nint parent, nint child);

    [DllImport("shell32.dll")]
    public static extern void ILFree(nint pidl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll")]
    public static extern nint GetFocus();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFile(nint drop, uint index, System.Text.StringBuilder? file, uint length);

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

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

// Windows SDK shobjidl_core.h: the folder view's own colors, including the sorted-column tint.
// https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivisualproperties
[ComImport, Guid("E693CF68-D967-4112-8763-99172AEE5E5A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IVisualProperties
{
    [PreserveSig] int SetWatermark(nint bitmap, uint flags);
    [PreserveSig] int SetColor(uint flags, int color);
    [PreserveSig] int GetColor(uint flags, out int color);
    [PreserveSig] int SetItemHeight(int height);
    [PreserveSig] int GetItemHeight(out int height);
    [PreserveSig] int SetFont(nint logFont, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [PreserveSig] int GetFont(nint logFont);
    [PreserveSig] int SetTheme([MarshalAs(UnmanagedType.LPWStr)] string? subAppName,
        [MarshalAs(UnmanagedType.LPWStr)] string? subIdList);
}

// Windows SDK shobjidl_core.h, IUnknown followed by these fourteen methods:
// https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/ShObjIdl_core.h
// ItemCount counts the displayed files and folders, not filesystem entries:
// https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-itemcount
[ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFolderView
{
    [PreserveSig] int GetCurrentViewMode(out uint viewMode);
    [PreserveSig] int SetCurrentViewMode(uint viewMode);
    [PreserveSig] int GetFolder(ref Guid interfaceId, out nint folder);
    [PreserveSig] int Item(int itemIndex, out nint pidl);
    [PreserveSig] int ItemCount(uint flags, out int count);
    [PreserveSig] int Items(uint flags, ref Guid interfaceId, out nint items);
    [PreserveSig] int GetSelectionMarkedItem(out int item);
    [PreserveSig] int GetFocusedItem(out int item);
    [PreserveSig] int GetItemPosition(nint pidl, out Point position);
    [PreserveSig] int GetSpacing(ref Point spacing);
    [PreserveSig] int GetDefaultSpacing(out Point spacing);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int item, uint flags);
    [PreserveSig] int SelectAndPositionItems(uint count, nint pidls, nint positions, uint flags);
}

[StructLayout(LayoutKind.Sequential)]
public struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

[StructLayout(LayoutKind.Sequential)]
public struct SortColumn
{
    public PropertyKey Property;
    public int Direction;
}

// Only the sort methods are called; the other slots keep the vtable order of IFolderView and IFolderView2.
[ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFolderView2
{
    void GetCurrentViewMode(); void SetCurrentViewMode(); void GetFolder(); void Item(); void ItemCount();
    void Items(); void GetSelectionMarkedItem(); void GetFocusedItem(); void GetItemPosition(); void GetSpacing();
    void GetDefaultSpacing(); void GetAutoArrange(); void SelectItem(); void SelectAndPositionItems();
    [PreserveSig] int SetGroupBy(ref PropertyKey key, [MarshalAs(UnmanagedType.Bool)] bool ascending);
    [PreserveSig] int GetGroupBy(out PropertyKey key, [MarshalAs(UnmanagedType.Bool)] out bool ascending);
    void SetViewProperty(); void GetViewProperty(); void SetTileViewProperties();
    void SetExtendedTileViewProperties(); void SetText(); void SetCurrentFolderFlags(); void GetCurrentFolderFlags();
    [PreserveSig] int GetSortColumnCount(out int count);
    [PreserveSig] int SetSortColumns([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SortColumn[] columns, int count);
    [PreserveSig] int GetSortColumns([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SortColumn[] columns, int count);
    void GetItem(); void GetVisibleItem(); void GetSelectedItem(); void GetSelection(); void GetSelectionState();
    [PreserveSig] int InvokeVerbOnSelection([MarshalAs(UnmanagedType.LPStr)] string verb);
    [PreserveSig] int SetViewModeAndIconSize(uint mode, int size);
    [PreserveSig] int GetViewModeAndIconSize(out uint mode, out int size);
    void SetGroupSubsetCount(); void GetGroupSubsetCount(); void SetRedraw(); void IsMoveInSameFolder();
    [PreserveSig] int DoRename();
}

// The folder view's background menu (New folder, Paste) is invoked by its canonical verb, as File Explorer does.
[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint firstCommand, uint lastCommand, uint flags);
    [PreserveSig] int InvokeCommand(nint info);
    [PreserveSig] int GetCommandString(nuint command, uint type, nint reserved, nint name, uint length);
}

[StructLayout(LayoutKind.Sequential)]
public struct InvokeCommandInfo
{
    public int Size;
    public uint Mask;
    public nint Window;
    public nint Verb;
    public nint Parameters;
    public nint Directory;
    public int Show;
    public uint HotKey;
    public nint Icon;
    public nint Title;
    public nint VerbW;
    public nint ParametersW;
    public nint DirectoryW;
    public nint TitleW;
    public int X, Y;
}

// ExplorerBrowser asks a folder filter which items to show while it enumerates a folder.
[ComImport, Guid("C0A651F5-B48B-11d2-B5ED-006097C686F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFolderFilterSite
{
    [PreserveSig] int SetFilter([MarshalAs(UnmanagedType.IUnknown)] object? filter);
}

[ComImport, Guid("9CC22886-DC8E-11d2-B1D0-00C04F8EEB3E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFolderFilter
{
    [PreserveSig] int ShouldShow([MarshalAs(UnmanagedType.IUnknown)] object folder, nint folderIdList, nint itemIdList);
    [PreserveSig] int GetEnumFlags([MarshalAs(UnmanagedType.IUnknown)] object folder, nint folderIdList, ref nint window, ref uint flags);
}
