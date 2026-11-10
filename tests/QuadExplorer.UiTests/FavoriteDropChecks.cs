using System.Reflection;
using System.Runtime.InteropServices;
using QuadExplorer;

internal static partial class Program
{
    // Real Shell CF_HDROP objects reach the sidebar's registered OLE targets. Export is never invoked here:
    // it changes the user's File Explorer Quick Access, which private desktops do not isolate.
    private static void RunFavoriteDropScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "FavoriteDrops")).FullName;
        string[] folders = Enumerable.Range(0, 4).Select(i =>
            Directory.CreateDirectory(Path.Combine(fixture, $"Favorite-{i}")).FullName).ToArray();
        string file = Path.Combine(fixture, "favorite-file.txt");
        File.WriteAllText(file, "Favorite drop fixture; never moved.");
        string session = Path.Combine(root, "favorite-drops.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, ExplorerFavoritesImported = true, FavoritesExpanded = false,
            CheckUpdates = false, GlobalHotkey = false, KeepInTray = false, AutoRestartMemory = 0,
            Panes = [new PaneState { Tabs = [fixture] }]
        });
        using var form = new MainForm(session, []);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                await OitReady(pane.ActiveView!);
                var list = Descendants(form).OfType<ListBox>().Single(item => item.AccessibleName == "Favorites");
                var header = Descendants(form).OfType<Button>().Single(item => item.AccessibleName == "Expand favorites");
                Check(!form.FavoritesExpanded && !list.Visible, "the fixture starts with Favorites collapsed");

                Point headerPoint = header.PointToScreen(new Point(header.Width / 2, header.Height / 2));
                using (var drop = new OitOleDrop(header, fixture, [folders[0], file, folders[1]]))
                {
                    Check(drop.Enter(headerPoint) == 4 && drop.Over(headerPoint) == 4,
                        "a real Shell drag with folders over the collapsed Favorites header is accepted as a link");
                    Check(drop.Drop(headerPoint) == 4 && form.FavoritePaths.SequenceEqual([folders[0], folders[1]]),
                        "dropping folders and a file adds only the folders, in drag order");
                }
                Check(form.FavoritesExpanded && list.Visible && list.Items.Count == 2 && list.SelectedIndex == 1
                    && PnStatusText(pane).Contains("Added 2 folders to favorites.", StringComparison.Ordinal),
                    "a drop on the collapsed header expands Favorites, selects the last added folder and reports the count");
                Check(File.ReadAllText(file) == "Favorite drop fixture; never moved." && folders.All(Directory.Exists)
                    && Directory.EnumerateFileSystemEntries(fixture).Count() == folders.Length + 1,
                    "a favorites drop never moves, copies or links the dropped items");

                Point listPoint = list.PointToScreen(new Point(list.Width / 2, list.ItemHeight / 2));
                using (var drop = new OitOleDrop(list, fixture, [file]))
                    Check(drop.Enter(listPoint) == 0 && drop.Over(listPoint) == 0,
                        "a file-only drag is refused by Favorites");
                using (var drop = new OitOleDrop(list, fixture, [folders[0], folders[2]]))
                {
                    drop.Enter(listPoint);
                    Check(drop.Drop(listPoint) == 4 && form.FavoritePaths.SequenceEqual([folders[0], folders[1], folders[2]])
                        && list.SelectedIndex == 2 && PnStatusText(pane).Contains("Added to favorites.", StringComparison.Ordinal),
                        "dropping a known and a new folder on the list adds only the new folder");
                }
                using (var drop = new OitOleDrop(list, fixture, [folders[1]]))
                {
                    drop.Enter(listPoint);
                    Check(drop.Drop(listPoint) == 4 && form.FavoritePaths.Count == 3 && list.SelectedIndex == 1
                        && PnStatusText(pane).Contains("Already in favorites.", StringComparison.Ordinal),
                        "dropping an existing favorite selects it without a duplicate");
                }

                form.ToggleFavorites();
                await PaneChange(pane, () => pane.Navigate(folders[3]),
                    () => pane.ActiveView is { HasNavigated: true } && pane.CurrentPath == folders[3]);
                pane.ActiveView!.FocusView();
                TgKey(form, Keys.Control | Keys.D);
                Check(form.FavoritePaths.LastOrDefault() == folders[3] && form.FavoritesExpanded && list.SelectedIndex == 3,
                    "Ctrl+D through the application's key filter adds the current folder and reveals it");
                FavoriteReorderChecks(form, list, header, folders);

                var menu = list.ContextMenuStrip!;
                var export = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Export to File Explorer");
                Check(menu.Items.IndexOf(export) == menu.Items.IndexOf(menu.Items.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == "Import from File Explorer")) + 1,
                    "Export to File Explorer sits directly after Import in the favorites management menu");
                menu.Show(list, Point.Empty);
                Check(export.Enabled, "Export to File Explorer is available while favorites exist");
                menu.Close();
                form.ClearFavorites();
                menu.Show(list, Point.Empty);
                Check(!export.Enabled, "Export to File Explorer is unavailable without favorites");
                menu.Close();
            }
            catch (Exception ex) { failure = ex; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Favorite drop scenario failed.", failure);
    }

    private static Func<object, DragDropEffects, DragDropEffects>? favoriteDrag;

    private static bool DispatchFavoriteDrag(object data, DragDropEffects allowedEffects, ref DragDropEffects __result)
    {
        if (favoriteDrag is null) return true;
        __result = favoriteDrag(data, allowedEffects);
        return false;
    }

    // Real WM_LBUTTONDOWN/WM_MOUSEMOVE messages reach the list, whose own handlers start the drag. Only OLE's
    // global-input loop is replaced (private-desktop QA sends no mouse input): the drag's data object goes
    // straight to the real drop handlers of the target control.
    private static void FavoriteReorderChecks(MainForm form, ListBox list, Button header, string[] folders)
    {
        var transport = new HarmonyLib.Harmony("FolderSplitter.FavoriteReorder");
        var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
            [typeof(object), typeof(DragDropEffects)]);
        transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchFavoriteDrag)));
        try
        {
            void Raise(Control target, string name, EventArgs args) =>
                typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [args]);
            static nint Position(Point point) => (point.Y << 16) | (point.X & 0xFFFF);
            Point Row(int index, bool lower)
            {
                var bounds = list.GetItemRectangle(index);
                return list.PointToScreen(new Point(bounds.Left + bounds.Width / 2, lower ? bounds.Bottom - 3 : bounds.Top + 3));
            }
            bool Marker(int y)
            {
                using var image = new Bitmap(list.Width, list.Height);
                list.DrawToBitmap(image, new Rectangle(Point.Empty, list.Size));
                return Enumerable.Range(list.Width / 4, list.Width / 2)
                    .All(x => image.GetPixel(x, y).ToArgb() == Theme.Accent.ToArgb());
            }
            (IDataObject Data, DragDropEffects Allowed, DragDropEffects Effect) Drag(int from, Control target, Point screen,
                Action<DragEventArgs>? over = null, bool drop = true)
            {
                var bounds = list.GetItemRectangle(from);
                var press = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                var moved = new Point(press.X, press.Y + SystemInformation.DragSize.Height);
                (IDataObject, DragDropEffects, DragDropEffects)? result = null;
                favoriteDrag = (data, allowed) =>
                {
                    FdReleaseCapture(); // OLE's DoDragDrop takes the mouse capture from the list as the drag starts.
                    var args = new DragEventArgs((IDataObject)data, 0, screen.X, screen.Y, allowed, DragDropEffects.None);
                    Raise(target, "OnDragEnter", args);
                    Raise(target, "OnDragOver", args);
                    over?.Invoke(args);
                    if (drop) Raise(target, "OnDragDrop", args);
                    else Raise(target, "OnDragLeave", EventArgs.Empty);
                    result = ((IDataObject)data, allowed, args.Effect);
                    return args.Effect;
                };
                // WinForms reads MouseMove's button from the thread key state; set VK_LBUTTON there, never globally.
                byte[] saved = new byte[256];
                if (!PnGetKeyboardState(saved)) throw new InvalidOperationException("Could not read keyboard state.");
                byte[] pressed = (byte[])saved.Clone();
                pressed[0x01] = 0x80;
                try
                {
                    if (!PnSetKeyboardState(pressed)) throw new InvalidOperationException("Could not set thread key state.");
                    SendMessage(list.Handle, 0x0201, 1, Position(press)); // WM_LBUTTONDOWN, MK_LBUTTON
                    SendMessage(list.Handle, 0x0200, 1, Position(moved)); // WM_MOUSEMOVE past the drag threshold
                }
                finally
                {
                    favoriteDrag = null;
                    FdReleaseCapture();
                    if (!PnSetKeyboardState(saved)) throw new InvalidOperationException("Could not restore thread key state.");
                }
                return result ?? throw new InvalidOperationException("Moving a pressed favorite past the drag threshold did not start a drag.");
            }

            list.TopIndex = 0;
            int scrollable = list.TopIndex;
            Drag(0, list, list.PointToScreen(new Point(list.Width / 2, list.ClientSize.Height - 3)), drop: false);
            Check(list.ClientSize.Height < list.Items.Count * list.ItemHeight && list.TopIndex == scrollable + 1
                && form.FavoritePaths.SequenceEqual(folders),
                "holding a reordering drag at the bottom edge of a long list scrolls it by one row");
            // Give the list room for every row, so each gesture below addresses visible rows only.
            form.SetFolderTreeVisible(false);
            int missing = (list.Items.Count + 1) * list.ItemHeight - list.ClientSize.Height;
            if (missing > 0) form.Height += missing;
            list.TopIndex = 0;
            if (list.ClientSize.Height < list.Items.Count * list.ItemHeight)
                throw new InvalidOperationException("The favorites list could not show every row.");
            bool marked = false;
            var first = Drag(0, list, Row(2, lower: true),
                over: args => marked = args.Effect == DragDropEffects.Move && Marker(list.GetItemRectangle(3).Top));
            Check(first.Allowed == DragDropEffects.Move && !first.Data.GetDataPresent(DataFormats.FileDrop)
                && !first.Data.GetDataPresent("Shell IDList Array"),
                "dragging a favorite past the threshold starts a Move-only drag that carries no file paths");
            Check(marked, "hovering below the middle of a row marks the gap after it with an accent line");
            Check(first.Effect == DragDropEffects.Move && form.FavoritePaths.SequenceEqual([folders[1], folders[2], folders[0], folders[3]])
                && list.SelectedIndex == 2,
                "dropping moves the favorite into the marked gap and selects it");

            Drag(3, list, Row(0, lower: false));
            Check(form.FavoritePaths.SequenceEqual([folders[3], folders[1], folders[2], folders[0]]) && list.SelectedIndex == 0,
                "dragging the last favorite above the first moves it to the top");
            Drag(1, list, Row(0, lower: true));
            Check(form.FavoritePaths.SequenceEqual([folders[3], folders[1], folders[2], folders[0]]),
                "dropping a favorite into its own gap leaves the order unchanged");
            var onHeader = Drag(0, header, header.PointToScreen(new Point(header.Width / 2, header.Height / 2)));
            Check(onHeader.Effect == DragDropEffects.None && form.FavoritePaths.SequenceEqual([folders[3], folders[1], folders[2], folders[0]]),
                "the Favorites header refuses a reordering drag");
            Drag(2, list, Row(0, lower: false), drop: false);
            Check(form.FavoritePaths.SequenceEqual([folders[3], folders[1], folders[2], folders[0]])
                && !Marker(list.GetItemRectangle(0).Top),
                "cancelling a reordering drag keeps the order and removes the marker");
            Drag(0, list, Row(3, lower: true));
            Check(form.FavoritePaths.SequenceEqual([folders[1], folders[2], folders[0], folders[3]]) && list.SelectedIndex == 3,
                "dragging below the middle of the last row moves the favorite to the end");
        }
        finally
        {
            favoriteDrag = null;
            transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
        }
    }

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
    private static extern bool FdReleaseCapture();
}
