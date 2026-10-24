using System.Runtime.InteropServices;

namespace QuadExplorer;

// The properties File Explorer's details pane shows for an item: the item's own PreviewDetails list
// (dimensions for images, duration for media, authors for documents...), named and formatted by Windows.
internal static class ShellProperties
{
    private static readonly string[] Leading = ["System.ItemTypeText", "System.ItemFolderPathDisplay", "System.Size"];
    private static readonly string[] Fallback = ["System.DateModified", "System.DateCreated"];

    internal static IReadOnlyList<(string Name, string Value)> Read(string path)
    {
        var result = new List<(string, string)>();
        Guid storeId = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreFromParsingName(path, 0, 0x40, ref storeId, out IPropertyStore? store) < 0 || store is null) // GPS_BESTEFFORT
            return result;
        nint value = Marshal.AllocHGlobal(32);
        try
        {
            var names = new List<string>(Leading);
            string? list = ReadString(store, "System.PropList.PreviewDetails", value);
            names.AddRange(list is null ? Fallback : list.Replace("prop:", "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.TrimStart('*', '~', '#', '!')));
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (PSGetPropertyKeyFromName(name, out var key) < 0) continue;
                string? label = DisplayName(ref key);
                string? text = Format(store, ref key, value);
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(text)) result.Add((label, text.Trim('\u200E', '\u200F', ' ')));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(value);
            Marshal.ReleaseComObject(store);
        }
        return result;
    }

    private static string? ReadString(IPropertyStore store, string name, nint value)
    {
        if (PSGetPropertyKeyFromName(name, out var key) < 0) return null;
        Clear(value);
        if (store.GetValue(ref key, value) < 0) return null;
        try { return Marshal.ReadInt16(value) == 31 ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(value, 8)) : null; } // VT_LPWSTR
        finally { PropVariantClear(value); }
    }

    private static string? Format(IPropertyStore store, ref PropertyKey key, nint value)
    {
        Clear(value);
        if (store.GetValue(ref key, value) < 0) return null;
        try
        {
            if (Marshal.ReadInt16(value) == 0) return null; // VT_EMPTY
            if (PSFormatForDisplayAlloc(ref key, value, 0, out nint text) < 0) return null;
            try { return Marshal.PtrToStringUni(text); }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        finally { PropVariantClear(value); }
    }

    private static string? DisplayName(ref PropertyKey key)
    {
        Guid descriptionId = typeof(IPropertyDescription).GUID;
        if (PSGetPropertyDescription(ref key, ref descriptionId, out IPropertyDescription? description) < 0 || description is null) return null;
        try
        {
            if (description.GetDisplayName(out nint name) < 0) return null;
            try { return Marshal.PtrToStringUni(name); }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.ReleaseComObject(description); }
    }

    private static void Clear(nint value) { for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(value, i, 0); }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, nint value);
    }

    [ComImport, Guid("6F79D558-3E96-4549-A1D1-7D75D2288814"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyDescription
    {
        [PreserveSig] int GetPropertyKey(out PropertyKey key);
        [PreserveSig] int GetCanonicalName(out nint name);
        [PreserveSig] int GetPropertyType(out ushort type);
        [PreserveSig] int GetDisplayName(out nint name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, nint bindContext, uint flags, ref Guid interfaceId,
        out IPropertyStore? store);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    private static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);

    [DllImport("propsys.dll")]
    private static extern int PSGetPropertyDescription(ref PropertyKey key, ref Guid interfaceId, out IPropertyDescription? description);

    [DllImport("propsys.dll")]
    private static extern int PSFormatForDisplayAlloc(ref PropertyKey key, nint value, uint flags, out nint text);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(nint value);
}
