using QuadExplorer;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

internal static partial class Program
{
    private static void RunCrossViewFileDragScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "CrossViewFileDrag")).FullName;
        string[] parents = Enumerable.Range(0, 3)
            .Select(i => Directory.CreateDirectory(Path.Combine(fixture, $"Parent-{i}")).FullName).ToArray();
        string fileA = Path.Combine(parents[0], "file-a.txt");
        string fileB = Path.Combine(parents[1], "file-b.txt");
        string fileC = Path.Combine(parents[2], "file-c.txt");
        File.WriteAllText(fileA, "cross-view file A");
        File.WriteAllText(fileB, "cross-view file B");
        File.WriteAllText(fileC, "cross-view file C");
        string folderA = Directory.CreateDirectory(Path.Combine(parents[0], "folder-a")).FullName;
        string folderB = Directory.CreateDirectory(Path.Combine(parents[1], "folder-b")).FullName;
        string folderC = Directory.CreateDirectory(Path.Combine(parents[2], "folder-c")).FullName;
        string copyDestination = Directory.CreateDirectory(Path.Combine(fixture, "copy-destination")).FullName;
        string session = Path.Combine(fixture, "session.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 2, WindowWidth = 1100, WindowHeight = 720,
            ExplorerFavoritesImported = true, GlobalHotkey = false, StartWithWindows = false,
            CheckUpdates = false, AutoUpdate = false, KeepInTray = false, AutoRestartMemory = 0,
            ShowFolderTree = false,
            Panes = [new PaneState { Tabs = [parents[0]] }, new PaneState { Tabs = [parents[1]] }]
        });

        using var context = MultiWindowContext(session);
        RunMultiWindowContext(context, async () =>
        {
            var first = context.Windows.Single();
            await MultiWindowReady(first);
            var second = context.CreateWindowFromActive(first);
            await MultiWindowReady(second);
            second.ApplyLayout(2, vertical: true);
            first.SetSidebarVisible(false);
            second.SetSidebarVisible(false);
            await OitReset(second.GetPane(0), parents[2]);
            await OitReset(second.GetPane(1), parents[0]);
            var a = first.GetPane(0).ActiveView!;
            var b = first.GetPane(1).ActiveView!;
            var c = second.GetPane(0).ActiveView!;
            var duplicate = second.GetPane(1).ActiveView!;
            foreach (var host in new[] { a, b, c, duplicate }) await OitReady(host);

            // Resolve rows before selecting anything: ListedPaths itself temporarily selects rows.
            var aFileRow = CfdRow(a, fileA);
            var aFolderRow = CfdRow(a, folderA);
            var bFolderRow = CfdRow(b, folderB);
            var bFileRow = CfdRow(b, fileB);
            var cFolderRow = CfdRow(c, folderC);
            var cFileRow = CfdRow(c, fileC);
            var duplicateRow = CfdRow(duplicate, fileA);

            await CfdClick(a, aFileRow, false);
            context.CrossViewFileDragOverride = (_, _) =>
                throw new InvalidOperationException("An ordinary single-view drag was intercepted.");
            CfdNativeGesture(a, aFileRow);
            Check(CfdSame(SelectedPaths(a), [fileA]) && File.ReadAllText(fileA) == "cross-view file A",
                "an ordinary single-view native drag leaves its file and selection intact when canceled");
            a.SelectPaths([]);
            context.CrossViewFileDragOverride = null;

            await CfdClick(a, aFileRow, true);
            await CfdClick(b, bFolderRow, true);
            await CfdClick(c, cFolderRow, true);
            second.Hide(); // A previously selected item in a hidden window must not be swept in.
            Check(CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "native Ctrl clicks select items in both visible panes");

            int calls = 0;
            context.CrossViewFileDragOverride = (hwnd, data) =>
            {
                calls++;
                Check(CfdSame(CfdDropPaths(data), [fileA, folderB]),
                    "cross-pane Shell data contains both visible selections and excludes the hidden window");
                return (0x00040101, 0);
            };
            CfdNativeGesture(a, aFileRow);
            if (calls != 1) throw new InvalidOperationException("A native mouse drag did not start the cross-pane operation.");
            Check(CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "a native mouse drag after releasing Ctrl uses both selections and preserves them when canceled");
            second.Show();
            await MultiWindowReady(second);
            await CfdClick(c, cFileRow, true);
            if (!CfdSame(SelectedPaths(c), [folderC, fileC]))
                throw new InvalidOperationException("Could not select both items before minimizing the window.");
            second.WindowState = FormWindowState.Minimized;
            var previousOperation = context.CrossViewFileDragOverride;
            bool minimizedDrag = false;
            context.CrossViewFileDragOverride = (_, data) =>
            {
                minimizedDrag = true;
                Check(CfdSame(CfdDropPaths(data), [fileA, folderB]),
                    "a minimized window's selections are excluded from the combined drag");
                return (0x00040101, 0);
            };
            CfdNativeGesture(a, aFileRow);
            if (!minimizedDrag) throw new InvalidOperationException("Could not start the drag with a minimized peer.");
            context.CrossViewFileDragOverride = previousOperation;
            second.WindowState = FormWindowState.Normal;
            second.Hide();
            await CfdDelayedNativeGesture(a, aFileRow);
            if (calls != 2) throw new InvalidOperationException("A delayed native mouse drag did not start the cross-pane operation.");
            Check(CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "a drag started after holding the mouse down still includes both selections");
            CfdNativeGesture(a, aFileRow, control: true);
            if (calls != 3) throw new InvalidOperationException("A Ctrl-held native drag did not start the cross-pane operation.");
            Check(CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "a native mouse drag while holding Ctrl includes both selections");
            calls = 0;
            CfdNativeGesture(a, aFileRow);
            if (calls != 1) throw new InvalidOperationException("The cross-pane drag was not started.");
            Check(calls == 1 && CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "canceling a cross-pane drag retains both native selections");
            CfdNativeGesture(a, aFileRow);
            if (calls != 2) throw new InvalidOperationException("The canceled cross-pane drag could not be retried.");
            Check(calls == 2 && CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "a canceled cross-pane drag can be retried without reselection");

            int shellResult = int.MinValue;
            uint shellEffect = uint.MaxValue;
            context.CrossViewFileDragOverride = (hwnd, data) =>
            {
                int finished = 0;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    if (Volatile.Read(ref finished) == 0) PostMessage(hwnd, 0x0100, 0x1B, 1); // Escape in the private UI thread.
                });
                try
                {
                    shellResult = CrossViewFileDragData.DoDragDrop(hwnd, data, out shellEffect);
                    return (shellResult, shellEffect);
                }
                finally { Volatile.Write(ref finished, 1); }
            };
            CfdNativeGesture(a, aFileRow);
            Console.WriteLine($"CROSS_VIEW_SHELL_END result=0x{shellResult:X8} effect={shellEffect} " +
                $"a={string.Join("|", SelectedPaths(a).Select(Path.GetFileName))} b={string.Join("|", SelectedPaths(b).Select(Path.GetFileName))}");
            Check(shellResult >= 0 && shellEffect == 0
                && CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "a native Shell drag without an accepting target leaves both selections unchanged");
            shellResult = int.MinValue;
            shellEffect = uint.MaxValue;
            if (!context.TryStartCrossViewFileDrag(a, NativeAppearanceList(a)))
                throw new InvalidOperationException("Could not retry the native Shell drag.");
            Check(shellResult == 0x00040101 && shellEffect == 0
                && CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [folderB]),
                "the native Shell drag can be canceled without changing selections");

            string repeatedCopy = Directory.CreateDirectory(Path.Combine(fixture, "repeated-copy")).FullName;
            context.CrossViewFileDragOverride = (_, data) =>
            {
                CfdFileOperation(data, repeatedCopy, move: false);
                return (0x00040100, 1);
            };
            CfdNativeGesture(a, aFileRow);
            Check(File.ReadAllText(Path.Combine(repeatedCopy, Path.GetFileName(fileA))) == "cross-view file A"
                && Directory.Exists(Path.Combine(repeatedCopy, Path.GetFileName(folderB)))
                && File.Exists(fileA) && Directory.Exists(folderB),
                "a successful cross-pane copy preserves both source selections and contents");
            calls = 0;
            context.CrossViewFileDragOverride = (_, data) =>
            {
                calls++;
                Check(CfdSame(CfdDropPaths(data), [fileA, folderB]),
                    "the same multi-view selection can be dragged again after copying");
                return (0x00040101, 0);
            };
            CfdNativeGesture(a, aFileRow);
            if (calls != 1) throw new InvalidOperationException("The copied selection could not be dragged again.");
            second.Show();
            await MultiWindowReady(second);

            b.SelectPaths([fileB]); // Simulate a changed selection without a new Ctrl gesture.
            bool unexpectedPartialDrag = false;
            context.CrossViewFileDragOverride = (_, _) =>
            {
                unexpectedPartialDrag = true;
                return (0x00040100, 1);
            };
            CfdNativeGesture(a, aFileRow);
            Check(!unexpectedPartialDrag
                && PnStatusText(first.GetPane(0)).Contains("Could not drag all selected files and folders.", StringComparison.Ordinal)
                && CfdSame(SelectedPaths(a), [fileA]) && CfdSame(SelectedPaths(b), [fileB])
                && File.Exists(fileA) && File.Exists(fileB) && Directory.Exists(folderB),
                "a changed selection stops the whole drag and reports the problem without moving anything");
            a.SelectPaths([]);
            b.SelectPaths([]);
            await CfdClick(a, aFileRow, true);
            await CfdClick(b, bFolderRow, true);

            bool invalidCreated = CrossViewFileDragData.TryCreate([fileA, Path.Combine(parents[1], "missing.txt")], out nint invalidData);
            if (invalidData != 0) Marshal.Release(invalidData);
            Check(!invalidCreated && invalidData == 0,
                "an unavailable item prevents creating a partial multi-folder drag");

            string moveSourceA = Directory.CreateDirectory(Path.Combine(fixture, "move-source-a")).FullName;
            string moveSourceB = Directory.CreateDirectory(Path.Combine(fixture, "move-source-b")).FullName;
            string moveTarget = Directory.CreateDirectory(Path.Combine(fixture, "move-target")).FullName;
            string moveFile = Path.Combine(moveSourceA, "move-file.txt");
            string moveFolder = Directory.CreateDirectory(Path.Combine(moveSourceB, "move-folder")).FullName;
            File.WriteAllText(moveFile, "moved file");
            File.WriteAllText(Path.Combine(moveFolder, "nested.txt"), "moved folder");
            if (!CrossViewFileDragData.TryCreate([moveFile, moveFolder], out nint moveData))
                throw new InvalidOperationException("Could not create a mixed-parent Shell data object for the move.");
            try
            {
                CfdFileOperation(moveData, moveTarget, move: true);
                Check(!File.Exists(moveFile) && !Directory.Exists(moveFolder)
                    && File.ReadAllText(Path.Combine(moveTarget, "move-file.txt")) == "moved file"
                    && File.ReadAllText(Path.Combine(moveTarget, "move-folder", "nested.txt")) == "moved folder",
                    "Windows Shell moves a file and a folder from different parents without losing their contents");
            }
            finally { Marshal.Release(moveData); }

            // A non-Ctrl click starts a new session. Rejoin one pane explicitly and ensure
            // the still-visible selection in the old, unjoined pane is not swept into it.
            await CfdClick(c, cFolderRow, false);
            await CfdClick(c, cFileRow, true);
            await CfdClick(b, bFileRow, true);
            string[] bSelection = SelectedPaths(b);
            Check(bSelection.Contains(fileB, StringComparer.OrdinalIgnoreCase)
                && CfdSame(SelectedPaths(c), [folderC, fileC]) && CfdSame(SelectedPaths(a), [fileA]),
                "new session has explicit selections and an unrelated retained old selection");
            string[] afterReset = bSelection.Concat([folderC, fileC]).ToArray();
            calls = 0;
            context.CrossViewFileDragOverride = (_, data) =>
            {
                calls++;
                Check(CfdSame(CfdDropPaths(data), afterReset),
                    "non-Ctrl click resets the session and excludes an unjoined stale selection");
                return (0x00040101, 0);
            };
            CfdNativeGesture(c, cFolderRow);
            if (calls != 1) throw new InvalidOperationException("The cross-window drag was not started.");

            await CfdClick(a, aFolderRow, true);
            await CfdClick(duplicate, duplicateRow, true);
            string[] aSelection = SelectedPaths(a);
            Check(CfdSame(aSelection, [fileA, folderA]) && CfdSame(SelectedPaths(duplicate), [fileA]),
                "the same file is selected in two views alongside a folder");
            string[] expected = afterReset.Concat(aSelection).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var target = second.GetPane(0);
            var previousTabs = target.Tabs.ToArray();
            Point dropPoint = TgStripPoint(target, target.Tabs[0].Button, after: false);
            calls = 0;
            context.CrossViewFileDragOverride = (hwnd, data) =>
            {
                calls++;
                string[] actual = CfdDropPaths(data);
                Check(CfdSame(actual, expected) && actual.Length == expected.Length,
                    "real CF_HDROP includes every explicitly selected item once across panes and windows");
                CfdFileOperation(data, copyDestination, move: false);
                Check(expected.All(path => File.Exists(Path.Combine(copyDestination, Path.GetFileName(path)))
                    || Directory.Exists(Path.Combine(copyDestination, Path.GetFileName(path))))
                    && File.ReadAllText(Path.Combine(copyDestination, Path.GetFileName(fileA))) == "cross-view file A"
                    && File.ReadAllText(Path.Combine(copyDestination, Path.GetFileName(fileB))) == "cross-view file B"
                    && File.ReadAllText(Path.Combine(copyDestination, Path.GetFileName(fileC))) == "cross-view file C",
                    "Windows Shell copies every selected file and folder from the merged data object");
                uint effect = CfdDropOnStrip(target, data, dropPoint);
                Check(effect == 4, "registered tab-strip IDropTarget accepts the actual merged data as a link");
                return (0x00040100, effect);
            };
            CfdNativeGesture(a, aFolderRow);
            if (calls != 1) throw new InvalidOperationException("The merged drag did not reach the destination.");
            var opened = target.Tabs.Where(tab => !previousTabs.Contains(tab)).ToArray();
            Check(opened.Length == expected.Length,
                "the real OLE drop opens one tab for every deduplicated item");
            string[] expectedLocations = expected.Select(path => File.Exists(path) ? Path.GetDirectoryName(path)! : path).ToArray();
            Check(CfdSame(opened.Select(tab => tab.Host?.LocationPath ?? "").ToArray(), expectedLocations),
                "the actual target opens each dropped folder and each dropped file's containing folder");
            foreach (string file in new[] { fileA, fileB, fileC })
            {
                var tab = opened.Single(candidate => string.Equals(candidate.Host?.LocationPath,
                    Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase));
                target.SelectTab(target.IndexOfTab(tab), focus: false);
                await OitReady(tab.Host!);
                Check(CfdSame(SelectedPaths(tab.Host!), [file]),
                    "a dropped file is selected in its newly opened tab");
            }
            Check(File.ReadAllText(fileA) == "cross-view file A" && File.ReadAllText(fileB) == "cross-view file B"
                && File.ReadAllText(fileC) == "cross-view file C"
                && new[] { folderA, folderB, folderC }.All(Directory.Exists),
                "cancel and tab-strip drop leave all original fixture items intact");

            foreach (uint mode in new uint[] { 4, 1, 2, 3, 6, 8 })
            {
                a.SetViewModeAndSize(mode, mode == 1 ? 48 : 16);
                b.SetViewModeAndSize(mode, mode == 1 ? 48 : 16);
                await NativeAppearanceDispatch(a);
                await NativeAppearanceDispatch(b);
                a.SelectPaths([]);
                b.SelectPaths([]);
                Point fileRow = CfdRow(a, fileA), folderRow = CfdRow(a, folderA), otherRow = CfdRow(b, fileB);
                await CfdClick(a, fileRow, false);
                await CfdClick(a, folderRow, true);
                await CfdClick(b, otherRow, true);
                calls = 0;
                context.CrossViewFileDragOverride = (_, data) =>
                {
                    calls++;
                    Check(CfdSame(CfdDropPaths(data), [fileA, folderA, fileB]),
                        $"native view mode {mode} includes selected files and folders across panes");
                    return (0x00040101, 0);
                };
                CfdNativeGesture(a, fileRow);
                if (calls != 1) throw new InvalidOperationException($"View mode {mode} did not start the combined drag.");
            }
            a.SetViewModeAndSize(4, 16);
            b.SetViewModeAndSize(4, 16);

            string sessionSourceA = Directory.CreateDirectory(Path.Combine(fixture, "session-move-a")).FullName;
            string sessionSourceB = Directory.CreateDirectory(Path.Combine(fixture, "session-move-b")).FullName;
            string sessionTarget = Directory.CreateDirectory(Path.Combine(fixture, "session-move-target")).FullName;
            string sessionFileA = Path.Combine(sessionSourceA, "move-a.txt");
            string sessionFileB = Path.Combine(sessionSourceB, "move-b.txt");
            string remainingA = Path.Combine(sessionSourceA, "remain-a.txt");
            string remainingB = Path.Combine(sessionSourceB, "remain-b.txt");
            File.WriteAllText(sessionFileA, "move A");
            File.WriteAllText(sessionFileB, "move B");
            File.WriteAllText(remainingA, "remain A");
            File.WriteAllText(remainingB, "remain B");
            await Navigate(a, () => a.Navigate(sessionSourceA));
            await Navigate(b, () => b.Navigate(sessionSourceB));
            await OitReady(a);
            await OitReady(b);
            Point moveRowA = CfdRow(a, sessionFileA), moveRowB = CfdRow(b, sessionFileB);
            await CfdClick(a, moveRowA, false);
            a.SelectPaths([]);
            await CfdClick(a, moveRowA, true);
            await CfdClick(b, moveRowB, true);
            context.CrossViewFileDragOverride = (_, data) =>
            {
                CfdFileOperation(data, sessionTarget, move: true);
                return (0x00040100, 2);
            };
            CfdNativeGesture(a, moveRowA);
            Check(!File.Exists(sessionFileA) && !File.Exists(sessionFileB)
                && File.ReadAllText(Path.Combine(sessionTarget, "move-a.txt")) == "move A"
                && File.ReadAllText(Path.Combine(sessionTarget, "move-b.txt")) == "move B",
                "a combined move relocates both selected files with their contents");

            a.RefreshFolder();
            b.RefreshFolder();
            await NativeAppearanceDispatch(a);
            await NativeAppearanceDispatch(b);
            if (a.SelectPaths([remainingA]) != 1 || b.SelectPaths([remainingB]) != 1)
                throw new InvalidOperationException("Could not select the remaining native files after the move.");
            Point remainingRow = CfdRow(a, remainingA);
            first.GetPane(0).SetLocalizedStatus(() => "move complete");
            context.CrossViewFileDragOverride = (_, _) =>
                throw new InvalidOperationException("The completed move retained the old combined selection.");
            CfdNativeGesture(a, remainingRow);
            Check(!PnStatusText(first.GetPane(0)).Contains("Could not drag all selected files and folders.", StringComparison.Ordinal)
                && File.Exists(remainingA) && File.Exists(remainingB),
                "after a move, an ordinary new selection uses the native single-view drag");
            context.CrossViewFileDragOverride = null;
        });
    }

    private static Point CfdRow(ShellViewHost host, string path) => OitRowPoint(host, path);

    private static Task CfdClick(ShellViewHost host, Point row, bool control) =>
        OitMouse(NativeAppearanceList(host), row, 0x201, 0x202, control ? Keys.Control : Keys.None);

    private static bool CfdSame(IEnumerable<string> actual, IEnumerable<string> expected) =>
        actual.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(expected.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);


    private static void CfdNativeGesture(ShellViewHost host, Point row, bool control = false)
    {
        nint list = NativeAppearanceList(host);
        byte[] saved = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("Could not read thread keyboard state.");
        byte[] state = (byte[])saved.Clone();
        state[0x01] = 0x80; // VK_LBUTTON, thread-local only
        state[0x11] = state[0xA2] = control ? (byte)0x80 : (byte)0;
        if (!PnSetKeyboardState(state)) throw new InvalidOperationException("Could not set thread keyboard state.");
        try
        {
            nint start = (row.Y << 16) | (row.X & 0xffff);
            nint moved = ((row.Y + 2) << 16) | ((row.X + 40) & 0xffff);
            nint modifiers = control ? 8 : 0;
            SendMessage(list, 0x0200, modifiers, start);
            if (!PostMessage(list, 0x0200, modifiers | 1, moved) || !PostMessage(list, 0x0202, modifiers, moved))
                throw new InvalidOperationException("Could not queue private mouse messages.");
            SendMessage(list, 0x0201, modifiers | 1, start);
            if (OitPeekMessage(out _, list, 0x0202, 0x0202, 1)) SendMessage(list, 0x0202, modifiers, moved);
        }
        finally { if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore thread keyboard state."); }
    }

    private static async Task CfdDelayedNativeGesture(ShellViewHost host, Point row)
    {
        nint list = NativeAppearanceList(host);
        byte[] saved = new byte[256];
        if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("Could not read thread keyboard state.");
        byte[] state = (byte[])saved.Clone();
        state[0x01] = 0x80;
        state[0x11] = state[0xA2] = 0;
        if (!PnSetKeyboardState(state)) throw new InvalidOperationException("Could not set thread keyboard state.");
        try
        {
            nint start = (row.Y << 16) | (row.X & 0xffff);
            nint moved = ((row.Y + 2) << 16) | ((row.X + 40) & 0xffff);
            Task input = Task.Run(async () =>
            {
                await Task.Delay(120);
                if (!PostMessage(list, 0x0200, 1, moved)) throw new InvalidOperationException("Could not queue private mouse movement.");
                await Task.Delay(120);
                if (!PostMessage(list, 0x0202, 0, moved)) throw new InvalidOperationException("Could not queue private mouse release.");
            });
            SendMessage(list, 0x0200, 0, start);
            SendMessage(list, 0x0201, 1, start);
            await input.WaitAsync(TimeSpan.FromSeconds(5));
            await NativeAppearanceDispatch(host);
            if (OitPeekMessage(out _, list, 0x0202, 0x0202, 1)) SendMessage(list, 0x0202, 0, moved);
        }
        finally { if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore thread keyboard state."); }
    }


    private static string[] CfdDropPaths(nint pointer)
    {
        if (pointer == 0) throw new InvalidOperationException("The Shell returned a null data object.");
        var data = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
            Marshal.ThrowExceptionForHR(data.QueryGetData(ref format));
            data.GetData(ref format, out var medium);
            try
            {
                uint count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
                var result = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint length = DragQueryFile(medium.unionmember, i, null, 0);
                    var buffer = new StringBuilder(checked((int)length + 1));
                    DragQueryFile(medium.unionmember, i, buffer, length + 1);
                    result[i] = buffer.ToString();
                }
                return result;
            }
            finally { ReleaseStgMedium(ref medium); }
        }
        finally { Marshal.ReleaseComObject(data); }
    }

    private static void CfdFileOperation(nint items, string destination, bool move)
    {
        Guid iid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        Marshal.ThrowExceptionForHR(CfdCreateShellItem(destination, 0, ref iid, out nint folder));
        var type = Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), throwOnError: true)!;
        var operation = (ICfdFileOperation)Activator.CreateInstance(type)!;
        try
        {
            operation.SetOperationFlags(0x0614); // FOF_SILENT | NOCONFIRMATION | NOCONFIRMMKDIR | NOERRORUI
            if (move) operation.MoveItems(items, folder); else operation.CopyItems(items, folder);
            operation.PerformOperations();
            if (operation.GetAnyOperationsAborted()) throw new InvalidOperationException("Windows aborted the disposable file operation.");
        }
        finally { Marshal.ReleaseComObject(operation); Marshal.Release(folder); }
    }

    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", CharSet = CharSet.Unicode)]
    private static extern int CfdCreateShellItem(string path, nint context, ref Guid iid, out nint item);

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICfdFileOperation
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
        void RenameItem(nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
        void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
        void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, nint sink);
        void CopyItems(nint items, nint destination);
        void DeleteItem(nint item, nint sink);
        void DeleteItems(nint items);
        void NewItem(nint folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, nint sink);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
    }

    private static uint CfdDropOnStrip(ExplorerPane pane, nint data, Point point)
    {
        nint target = GetProp(pane.TabStrip.Handle, "OleDropTargetInterface");
        if (target == 0) throw new InvalidOperationException("The tab strip has no registered OLE drop target.");
        Marshal.AddRef(target);
        bool entered = false;
        try
        {
            uint effect = 7;
            Marshal.ThrowExceptionForHR(OitSlot<OitDragEnter>(target, 3)(target, data, 0, new OitPoint(point), ref effect));
            entered = true;
            Check(effect == 4, "the tab strip advertises link for the merged Shell data");
            effect = 7;
            Marshal.ThrowExceptionForHR(OitSlot<OitDragEnter>(target, 6)(target, data, 0, new OitPoint(point), ref effect));
            entered = false;
            return effect;
        }
        finally
        {
            if (entered) OitSlot<OitDragLeave>(target, 5)(target);
            Marshal.Release(target);
        }
    }

}
