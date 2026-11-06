using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using QuadExplorer;

internal static partial class Program
{
    private static void JfShellDescriptionChecks(MainForm form, ExplorerPane pane, string fixture, string[] folders)
    {
        string originalLanguage = form.Language;
        try
        {
            foreach (var (language, named, unnamed, ungrouped) in new[]
            {
                ("ko", "'연구 Research 日本語' 그룹에 추가", "그룹에 추가", "새 탭으로 열기"),
                ("en", "Add to '연구 Research 日本語' group", "Add to group", "Open in new tabs"),
                ("zh-CN", "添加到“연구 Research 日本語”组", "添加到组", "在新标签页中打开"),
                ("ja", "「연구 Research 日本語」グループに追加", "グループに追加", "新しいタブで開く")
            })
            foreach (bool collapsed in new[] { false, true })
            foreach (bool hasName in new[] { false, true })
            foreach (bool drop in new[] { false, true })
            {
                form.SetLanguage(language);
                TgReset(pane, 3);
                var group = pane.CreateGroup(pane.Tabs[1])!;
                pane.AddToGroup(pane.Tabs[2], group);
                pane.RenameGroup(group, hasName ? "연구 Research 日本語" : "");
                pane.SelectTab(0, focus: false);
                pane.SetGroupCollapsed(group, collapsed);
                Point point = TgJoinPoint(pane, group);
                string scenario = $"Shell description {language} {(collapsed ? "collapsed" : "expanded")} {(hasName ? "named" : "unnamed")} {(drop ? "drop" : "leave")}";
                using var ole = new OitOleDrop(pane.TabStrip, fixture, folders.Take(2).ToArray());
                Check(ole.Enter(point) == 4, scenario + " uses the original native Shell drag data object");
                JfAssertShellDescription(ole, 4, ungrouped, scenario + " starts with the localized new-tabs action");
                OitFireTimer(TgJoinTimer(pane));
                // Read directly from the source COM object without another DragOver.
                JfAssertShellDescription(ole, 4, hasName ? named : unnamed,
                    scenario + " stationary dwell updates the source's Unicode DROPIMAGE_LINK description");
                var label = typeof(ExplorerPane).GetMethod("JoinDropLabel", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(label is not null && (string?)label.Invoke(pane, [group]) == (hasName ? named : unnamed)
                    && (string?)label.Invoke(pane, [null]) == ungrouped,
                    scenario + " internal overlay and Shell feedback share the same localized action label");
                Point outsideGroup = TgStripPoint(pane, pane.Tabs[0].Button, after: false);
                Check(ole.Over(outsideGroup) == 4, scenario + " preserves ordinary insertion outside the group");
                JfAssertShellDescription(ole, 4, ungrouped, scenario + " leaving the group resets the action");
                Check(ole.Over(point) == 4, scenario + " permits a fresh group dwell");
                OitFireTimer(TgJoinTimer(pane));
                JfAssertShellDescription(ole, 4, hasName ? named : unnamed, scenario + " rearms the group action");
                if (drop) Check(ole.Drop(point) == 4, scenario + " preserves the Shell link drop effect");
                else ole.Leave();
                JfAssertShellDescription(ole, -1, "", scenario + " clears the original Shell description on completion");
                Check(typeof(ExplorerPane).GetField("externalDropData", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pane) is null,
                    scenario + " releases the retained source after completion");
            }
            JfReadOnlyShellDescriptionCheck(pane, fixture, folders);
        }
        finally { form.SetLanguage(originalLanguage); }
    }

    private static void JfAssertShellDescription(OitOleDrop ole, int image, string message, string scenario)
    {
        nint pointer = (nint)typeof(OitOleDrop).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ole)!;
        var source = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            var format = new FORMATETC
            {
                cfFormat = unchecked((short)DataFormats.GetFormat("DropDescription").Id),
                dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL
            };
            Check(source.QueryGetData(ref format) >= 0, scenario + " stores CFSTR_DROPDESCRIPTION on the incoming COM object");
            source.GetData(ref format, out var medium);
            try
            {
                Check(medium.tymed == TYMED.TYMED_HGLOBAL && medium.unionmember != 0
                    && JfGlobalSize(medium.unionmember) >= 1044, scenario + " uses the complete 1044-byte Unicode DROPDESCRIPTION layout");
                nint memory = JfGlobalLock(medium.unionmember);
                Check(memory != 0, scenario + " returns a lockable movable HGLOBAL");
                try
                {
                    Check(Marshal.ReadInt32(memory) == image && Marshal.PtrToStringUni(memory + 4) == message
                        && Marshal.ReadInt16(memory + 522) == 0 && Marshal.ReadInt16(memory + 524) == 0
                        && Marshal.ReadInt16(memory + 1042) == 0,
                        scenario + " preserves image type, Unicode message, empty insert, and both MAX_PATH terminators");
                }
                finally { JfGlobalUnlock(medium.unionmember); }
            }
            finally { ShellNative.ReleaseStgMedium(ref medium); }
        }
        finally { Marshal.ReleaseComObject(source); }
    }

    private static void JfReadOnlyShellDescriptionCheck(ExplorerPane pane, string fixture, string[] folders)
    {
        TgReset(pane, 3);
        var group = pane.CreateGroup(pane.Tabs[1])!;
        pane.AddToGroup(pane.Tabs[2], group);
        pane.SelectTab(0, focus: false);
        pane.SetGroupCollapsed(group, true);
        var ole = new OitOleDrop(pane.TabStrip, fixture, folders.Take(2).ToArray());
        var field = typeof(OitOleDrop).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)!;
        nint original = (nint)field.GetValue(ole)!;
        var source = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(original);
        try
        {
            var readOnly = new JfReadOnlyShellData(source);
            nint replacement = Marshal.GetComInterfaceForObject(readOnly, typeof(System.Runtime.InteropServices.ComTypes.IDataObject));
            field.SetValue(ole, replacement);
            Marshal.Release(original);
            Point point = TgJoinPoint(pane, group);
            Check(ole.Enter(point) == 4, "read-only Shell source keeps the native link effect when SetData rejects feedback");
            OitFireTimer(TgJoinTimer(pane));
            Check(readOnly.SetAttempts >= 2 && TgChip(pane, group).DropJoinArmed,
                "read-only Shell source safely rejects initial and stationary-armed descriptions without cancelling dwell");
            Check(ole.Drop(point) == 4 && readOnly.SetAttempts >= 3,
                "read-only Shell source safely rejects clearing feedback without preventing the real drop");
        }
        finally
        {
            ole.Dispose();
            Marshal.ReleaseComObject(source);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class JfReadOnlyShellData(System.Runtime.InteropServices.ComTypes.IDataObject source)
        : System.Runtime.InteropServices.ComTypes.IDataObject
    {
        internal int SetAttempts { get; private set; }
        public void GetData(ref FORMATETC format, out STGMEDIUM medium) => source.GetData(ref format, out medium);
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => source.GetDataHere(ref format, ref medium);
        public int QueryGetData(ref FORMATETC format) => source.QueryGetData(ref format);
        public int GetCanonicalFormatEtc(ref FORMATETC format, out FORMATETC canonical) =>
            source.GetCanonicalFormatEtc(ref format, out canonical);
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release)
        {
            SetAttempts++;
            throw new COMException("The Shell drag source is read-only.", unchecked((int)0x80004001));
        }
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => source.EnumFormatEtc(direction);
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) =>
            source.DAdvise(ref format, flags, sink, out connection);
        public void DUnadvise(int connection) => source.DUnadvise(connection);
        public int EnumDAdvise(out IEnumSTATDATA? enumerator) => source.EnumDAdvise(out enumerator);
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock")]
    private static extern nint JfGlobalLock(nint memory);
    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool JfGlobalUnlock(nint memory);
    [DllImport("kernel32.dll", EntryPoint = "GlobalSize")]
    private static extern nuint JfGlobalSize(nint memory);
}
