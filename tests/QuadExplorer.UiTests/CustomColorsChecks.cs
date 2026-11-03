using QuadExplorer;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunCustomColorsScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "CustomColors")).FullName;
        File.WriteAllText(Path.Combine(folder, "sample.txt"), "custom colors");
        string session = Path.Combine(root, "custom-colors.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", GlobalHotkey = false, StartWithWindows = false, KeepInTray = false,
            CheckUpdates = false, AutoUpdate = false, ExplorerFavoritesImported = true,
            Favorites = [folder], Panes = [new PaneState { Tabs = [folder] }]
        });
        Color savedAccent = default, savedBackground = default, savedCanvas = default;
        CustomColorPair[] savedRecent = [];
        using (var form = new MainForm(session, [], null, [folder]))
        {
            int beforeCanvas = 0, beforeAccent = 0;
            var pane = form.GetPane(0);
            var navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
            form.Shown += async (_, _) =>
            {
                try
                {
                    await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                    CheckCustomColorDialogDetails(form);
                    form.SetColorTheme("custom");
                    Check(form.ColorTheme == "light", "custom theme without stored seeds falls back to light");
                    var light = PaletteGenerator.Generate(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
                    form.SetCustomColors(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
                    CheckCustomSurfaces(form, false, "light custom");
                    var dark = PaletteGenerator.Generate(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
                    form.SetCustomColors(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
                    CheckCustomSurfaces(form, true, "dark custom");
                    CheckNoCustomColors(form, light, "custom A to custom B");
                    form.SetColorTheme("light");
                    CheckNoCustomColors(form, dark, "custom B to preset light");
                    form.SetColorTheme("dark");
                    CheckNoCustomColors(form, dark, "preset light to dark after custom");
                    CheckNoCustomColors(form, light, "old custom A is absent after later transitions");
                    form.SetColorTheme("custom");
                    Check(Theme.Canvas.ToArgb() == dark.Canvas.ToArgb(), "stored custom seeds can be selected after a preset");
                    // A deep blue background generates a surface equal to Nord's sorted-column color. After the
                    // switch is applied, re-theming the panes (as a layout change does) must not read it as Nord's.
                    form.SetColorTheme("nord");
                    form.SetCustomColors(Color.FromArgb(0, 120, 212), Color.FromArgb(30, 58, 138));
                    var themed = Descendants(pane).Prepend(pane)
                        .Select(control => (Control: control, Back: control.BackColor.ToArgb(), Fore: control.ForeColor.ToArgb())).ToArray();
                    form.ApplyLayout(2);
                    form.ApplyLayout(1);
                    var changed = themed.Where(entry => !entry.Control.IsDisposed
                            && (entry.Control.BackColor.ToArgb() != entry.Back || entry.Control.ForeColor.ToArgb() != entry.Fore))
                        .Select(entry => $"{entry.Control.GetType().Name} {entry.Control.AccessibleName} back {entry.Back:X8}->{entry.Control.BackColor.ToArgb():X8}"
                            + $" fore {entry.Fore:X8}->{entry.Control.ForeColor.ToArgb():X8}").ToArray();
                    Check(changed.Length == 0, "a layout change after Nord to custom keeps every pane color"
                        + (changed.Length == 0 ? "" : " (changed: " + string.Join(", ", changed) + ")"));
                    form.SetColorTheme("light");
                    beforeCanvas = Theme.Canvas.ToArgb();
                    beforeAccent = Theme.Accent.ToArgb();
                    EditCustomDialog(false);
                    Check(form.ColorTheme == "light" && Theme.Canvas.ToArgb() == beforeCanvas && Theme.Accent.ToArgb() == beforeAccent,
                        "cancelling custom colors leaves the active theme unchanged");
                    CheckPickerDialog(form, null, []);
                    CheckPickerDialog(form, "#123456", ["#123456"]);
                    Check(form.ColorTheme == "light" && Theme.Canvas.ToArgb() == beforeCanvas && Theme.Accent.ToArgb() == beforeAccent,
                        "picker slots survive cancelling the theme dialog without applying its preview");
                    Check(SessionStore.Load(session).PickerColors.SequenceEqual(new[] { "#123456" }),
                        "picker slots save immediately even when the theme dialog is cancelled");
                    CheckPickerDialog(form, null, ["#123456"]);
                    CheckPickerDialog(form, "#FFFFFF", ["#FFFFFF"]);
                    CheckPickerDialog(form, "#123456", ["#123456"]);
                    EditCustomDialog(true);
                    Check(form.ColorTheme == "custom", "applying the custom color dialog selects the custom theme");
                    CheckCustomSurfaces(form, true, "dialog custom");
                    EditCustomDialog(true, "#008B8B", "#FFFFFF");
                    var recentItems = OpenRecentMenu();
                    Check(recentItems.Length >= 2
                        && recentItems[0].AccessibleName == "Custom colors #FFFFFF #008B8B"
                        && recentItems[1].AccessibleName == "Custom colors #302336 #CA4D8C"
                        && recentItems[0].Checked && !recentItems[1].Checked,
                        "dialog-applied combinations appear newest first with the active pair checked");
                    var themeMenu = ThemeMenu(form);
                    Check(themeMenu.DropDownItems[themeMenu.DropDownItems.IndexOf(recentItems[0]) - 1] is ToolStripSeparator
                        && themeMenu.DropDownItems.IndexOf(recentItems[^1])
                            == themeMenu.DropDownItems.IndexOf(themeMenu.DropDownItems.OfType<ToolStripMenuItem>()
                                .Single(item => item.AccessibleName == "Custom colors")) - 1,
                        "remembered combinations sit between the preset separator and Custom colors dialog item");
                    Check(recentItems[0].Text == "#FFFFFF · #008B8B", "recent menu item displays background then accent hex");
                    int menuDpi = themeMenu.Owner!.DeviceDpi;
                    using (var image = new Bitmap(recentItems[0].Image!))
                    {
                        Check(image.Size == new Size((int)Math.Round(24 * menuDpi / 96d), (int)Math.Round(16 * menuDpi / 96d))
                            && image.GetPixel(image.Width / 4, image.Height / 2).ToArgb() == Color.White.ToArgb()
                            && image.GetPixel(image.Width * 3 / 4, image.Height / 2).ToArgb() == SessionStore.ParseColor("#008B8B").ToArgb(),
                            "recent menu image uses toolbar DPI with background on the left and accent on the right");
                    }
                    recentItems[1].PerformClick();
                    themeMenu.HideDropDown();
                    Check(Theme.Canvas.ToArgb() == PaletteGenerator.Generate(SessionStore.ParseColor("#CA4D8C"),
                        SessionStore.ParseColor("#302336")).Canvas.ToArgb(), "clicking an older combination immediately applies its generated canvas");
                    var reordered = OpenRecentMenu();
                    Check(reordered[0].AccessibleName == "Custom colors #302336 #CA4D8C" && reordered[0].Checked
                        && reordered[1].AccessibleName == "Custom colors #FFFFFF #008B8B" && !reordered[1].Checked
                        && recentItems.All(item => item.IsDisposed),
                        "choosing an older combination moves it to the front and rebuilding disposes the old menu items");
                    themeMenu.HideDropDown();
                    var distinctBackgrounds = new[] { "#FFFFFF", "#F7F3E8", "#E6E5E1", "#E6F2EA", "#E8F1F8", "#EEEAF7", "#F8E9ED" };
                    foreach (string background in distinctBackgrounds)
                        form.SetCustomColors(SessionStore.ParseColor("#0078D4"), SessionStore.ParseColor(background));
                    Check(form.RecentCustomColors.Count == SessionStore.RecentCustomColorLimit
                        && form.RecentCustomColors.Select(pair => pair.Background).SequenceEqual(distinctBackgrounds.Reverse().Take(6))
                        && form.RecentCustomColors.All(pair => pair.Accent == "#0078D4"),
                        "applying seven distinct combinations retains only the six newest in order");
                    savedAccent = SessionStore.ParseColor("#0078D4");
                    savedBackground = SessionStore.ParseColor(distinctBackgrounds[^1]);
                    savedRecent = form.RecentCustomColors.ToArray();
                    savedCanvas = Theme.Canvas;
                }
                catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + exception); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);

            ToolStripMenuItem[] OpenRecentMenu()
            {
                var menu = ThemeMenu(form);
                menu.ShowDropDown();
                return menu.DropDownItems.OfType<ToolStripMenuItem>()
                    .Where(item => item.AccessibleName?.StartsWith("Custom colors #", StringComparison.Ordinal) == true).ToArray();
            }

            void EditCustomDialog(bool apply, string accentHex = "#CA4D8C", string backgroundHex = "#302336")
            {
                Exception? failure = null;
                beforeCanvas = Theme.Canvas.ToArgb();
                beforeAccent = Theme.Accent.ToArgb();
                using var timer = new System.Windows.Forms.Timer { Interval = 30 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<CustomColorsDialog>().FirstOrDefault();
                    if (dialog is null) return;
                    timer.Stop();
                    try
                    {
                        var before = dialog.PreviewColors;
                        CheckCustomColorDialogLayout(dialog, "menu dialog");
                        var recent = Descendants(dialog).OfType<Button>()
                            .Where(button => button.AccessibleName?.StartsWith("Recent combinations #", StringComparison.Ordinal) == true).ToArray();
                        Check(recent.Length == form.RecentCustomColors.Count, "dialog has one swatch per remembered combination");
                        if (recent.Length > 0)
                        {
                            var pair = form.RecentCustomColors[^1];
                            recent[^1].PerformClick();
                            Check(dialog.Background.ToArgb() == SessionStore.ParseColor(pair.Background!).ToArgb()
                                && dialog.Accent.ToArgb() == SessionStore.ParseColor(pair.Accent!).ToArgb()
                                && dialog.PreviewColors == PaletteGenerator.Generate(dialog.Accent, dialog.Background)
                                && recent[^1].AccessibleDescription == "Selected"
                                && recent.Where(button => button != recent[^1]).All(button => button.AccessibleDescription is null),
                                "clicking a recent swatch sets both seeds, selects only that pair and updates its preview");
                            Check(Theme.Canvas.ToArgb() == beforeCanvas && Theme.Accent.ToArgb() == beforeAccent,
                                "recent swatches preview without applying the application theme");
                            var swatch = recent[^1];
                            using var split = new Bitmap(swatch.Width, swatch.Height);
                            swatch.DrawToBitmap(split, swatch.ClientRectangle);
                            Check(split.GetPixel(swatch.Width / 3, swatch.Height / 2).ToArgb() == dialog.Background.ToArgb()
                                && split.GetPixel(swatch.Width * 2 / 3, swatch.Height / 2).ToArgb() == dialog.Accent.ToArgb(),
                                "recent dialog swatch paints a background-left accent-right split circle");
                        }
                        Descendants(dialog).OfType<Button>().Single(button => button.Visible && button.AccessibleName == "Accent color " + accentHex).PerformClick();
                        Descendants(dialog).OfType<Button>().Single(button => button.Visible && button.AccessibleName == "Background color " + backgroundHex).PerformClick();
                        savedAccent = dialog.Accent;
                        savedBackground = dialog.Background;
                        var expected = PaletteGenerator.Generate(savedAccent, savedBackground);
                        Check(savedAccent.ToArgb() == SessionStore.ParseColor(accentHex).ToArgb()
                            && savedBackground.ToArgb() == SessionStore.ParseColor(backgroundHex).ToArgb(), "dialog swatches update both seeds");
                        Check(dialog.PreviewColors == expected && dialog.PreviewColors != before, "dialog preview updates from the generated palette");
                        var preview = Descendants(dialog).Single(control => control.AccessibleName == "Custom colors preview");
                        using var bitmap = new Bitmap(preview.Width, preview.Height);
                        preview.DrawToBitmap(bitmap, preview.ClientRectangle);
                        Check(bitmap.GetPixel(preview.Width - 4, 4).ToArgb() == expected.Canvas.ToArgb(), "painted preview uses the generated canvas");
                        Check(Theme.Canvas.ToArgb() == beforeCanvas && Theme.Accent.ToArgb() == beforeAccent,
                            "editing swatches previews without changing the application theme");
                        Descendants(dialog).OfType<Button>().Single(button => button.AccessibleName == (apply ? "Apply" : "Cancel")).PerformClick();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        dialog.DialogResult = DialogResult.Cancel;
                    }
                };
                var themeMenu = ThemeMenu(form);
                themeMenu.ShowDropDown();
                var custom = themeMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.AccessibleName == "Custom colors");
                Check(!custom.Checked && themeMenu.DropDownItems.OfType<ToolStripMenuItem>().Count(item => item.Checked) == 1,
                    "only the active preset or combination is checked, never the dialog entry as well");
                timer.Start();
                custom.PerformClick();
                themeMenu.HideDropDown();
                if (failure is not null) throw failure;
            }
        }
        var restored = SessionStore.Load(session, out var warning);
        Check(warning is null && restored.ColorTheme == "custom"
            && restored.CustomAccent == SessionStore.FormatColor(savedAccent)
            && restored.CustomBackground == SessionStore.FormatColor(savedBackground), "custom seed choices persist when the form closes");
        Check(restored.RecentCustomColors.SequenceEqual(savedRecent), "recent combinations persist in order when the form closes");
        Check(restored.PickerColors.SequenceEqual(new[] { "#123456" }),
            "the native picker's first custom slot persists without unused trailing white slots when the form closes");
        using (var reopened = new MainForm(session, [], null, [folder]))
        {
            Check(reopened.ColorTheme == "custom" && reopened.BackColor.ToArgb() == savedCanvas.ToArgb()
                && Theme.Canvas.ToArgb() == savedCanvas.ToArgb(), "reopened form restores the same generated custom canvas");
            Check(reopened.RecentCustomColors.SequenceEqual(savedRecent), "reopened form restores the remembered combinations in order");
            reopened.Shown += (_, _) =>
            {
                try
                {
                    var menu = ThemeMenu(reopened);
                    menu.ShowDropDown();
                    var recent = menu.DropDownItems.OfType<ToolStripMenuItem>()
                        .Where(item => item.AccessibleName?.StartsWith("Custom colors #", StringComparison.Ordinal) == true).ToArray();
                    Check(recent.Select(item => item.AccessibleName)
                            .SequenceEqual(savedRecent.Select(pair => "Custom colors " + pair.Background + " " + pair.Accent))
                        && recent[0].Checked && recent.Skip(1).All(item => !item.Checked),
                        "reopened theme menu restores all recent items and checks the active pair");
                    menu.HideDropDown();
                    CheckPickerDialog(reopened, null, ["#123456"]);
                }
                catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + exception); }
                finally { reopened.ExitApplication(); }
            };
            Application.Run(reopened);
        }
        Theme.Set("light");
    }

    private static ToolStripDropDownButton ThemeMenu(MainForm form) =>
        Descendants(form).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>())
            .Single(item => item.AccessibleName == "Color theme");

    private static void CheckPickerDialog(MainForm owner, string? addHex, string[] expected)
    {
        Exception? failure = null;
        CustomColorsDialog? activeDialog = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            activeDialog = Application.OpenForms.OfType<CustomColorsDialog>().FirstOrDefault();
            if (activeDialog is null)
            {
                if (DateTime.UtcNow > deadline)
                {
                    timer.Stop();
                    failure = new TimeoutException("Custom colors dialog did not open.");
                }
                return;
            }
            timer.Stop();
            try
            {
                Check(activeDialog.DeviceDpi == 120, "native picker checks run from a 120 DPI custom colors dialog");
                OpenPicker("Background color");
                Check(activeDialog.PickerColors.SequenceEqual(expected), "native picker stores exactly the changed slot prefix including explicit white");
                // The other row must reuse the same slots without turning untouched white padding into colors.
                OpenPicker("Accent color", false);
                Check(activeDialog.PickerColors.SequenceEqual(expected), "both More colors rows share custom slots and unchanged pickers retain them");
            }
            catch (Exception exception) { failure = exception; }
            finally { activeDialog.DialogResult = DialogResult.Cancel; }
        };
        var menu = ThemeMenu(owner);
        try
        {
            menu.ShowDropDown();
            timer.Start();
            menu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.AccessibleName == "Custom colors").PerformClick();
        }
        finally
        {
            timer.Stop();
            if (activeDialog is { IsDisposed: false }) activeDialog.DialogResult = DialogResult.Cancel;
            menu.HideDropDown();
        }
        if (failure is not null) throw failure;

        void OpenPicker(string row, bool add = true)
        {
            nint picker = 0;
            Exception? nativeFailure = null;
            bool handled = false;
            var nativeDeadline = DateTime.UtcNow.AddSeconds(10);
            using var poll = new System.Windows.Forms.Timer { Interval = 30 };
            poll.Tick += (_, _) =>
            {
                EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
                {
                    var name = new System.Text.StringBuilder(32);
                    GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "#32770" && GetWindowOwner(window, 4) == activeDialog!.Handle) picker = window;
                    return true;
                }, 0);
                // The nested native loop can pump our timer before the picker finishes showing its controls.
                bool ready = picker != 0 && DemoIsVisible(picker) && DemoIsVisible(PickerGetDlgItem(picker, 712));
                if (!ready && DateTime.UtcNow <= nativeDeadline) return;
                poll.Stop();
                try
                {
                    if (!ready) throw new TimeoutException("Native color picker did not finish opening.");
                    handled = true;
                    nint define = PickerGetDlgItem(picker, 719);
                    Check(define != 0 && !DemoIsVisible(define), "expanded native picker hides Define Custom Colors instead of showing a disabled button");
                    Check(DemoIsVisible(PickerGetDlgItem(picker, 712)), "expanded native picker keeps Add to Custom Colors available");
                    if (add && addHex is not null)
                    {
                        var color = SessionStore.ParseColor(addHex);
                        PickerSetText(PickerGetDlgItem(picker, 706), color.R.ToString());
                        PickerSetText(PickerGetDlgItem(picker, 707), color.G.ToString());
                        PickerSetText(PickerGetDlgItem(picker, 708), color.B.ToString());
                        SendMessage(PickerGetDlgItem(picker, 712), 0x00F5, 0, 0); // BM_CLICK
                    }
                    SendMessage(PickerGetDlgItem(picker, 1), 0x00F5, 0, 0);
                }
                catch (Exception exception) { nativeFailure = exception; }
                finally
                {
                    if (picker != 0 && GetWindowOwner(picker, 4) == activeDialog!.Handle)
                        SendMessage(picker, 0x0111, 2, 0);
                }
            };
            try
            {
                poll.Start();
                Descendants(activeDialog!).OfType<Button>().Single(button => button.AccessibleName == row + " more colors").PerformClick();
            }
            finally
            {
                poll.Stop();
                if (picker != 0 && GetWindowOwner(picker, 4) == activeDialog!.Handle)
                    SendMessage(picker, 0x0111, 2, 0);
            }
            if (nativeFailure is not null) throw nativeFailure;
            if (!handled) throw new InvalidOperationException("Native color picker was not exercised.");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint PickerGetDlgItem(nint dialog, int id);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern bool PickerSetText(nint window, string text);

    private static void CheckCustomColorDialogDetails(Form owner)
    {
        foreach (string language in new[] { "en", "ko", "zh-CN", "ja" })
        {
            var text = new UiText(language);
            (Color Background, Color Accent)[] recentColors = language == "en" ? []
                : [(SessionStore.ParseColor("#F6F7F9"), SessionStore.ParseColor("#626525"))];
            using var dialog = new CustomColorsDialog(text, SessionStore.ParseColor("#626525"), SessionStore.ParseColor("#F6F7F9"), owner, recentColors, []);
            dialog.Show(owner);
            CheckCustomColorDialogLayout(dialog, language + " saved colors");
            var recentLabel = Descendants(dialog).OfType<Label>().SingleOrDefault(label => label.AccessibleName == "Recent combinations");
            Check(language == "en" ? recentLabel is null : recentLabel?.Text == text.T("Recent combinations"),
                language + " shows a localized recent combinations label only when combinations exist");
            var accent = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CurrentAccentSwatch");
            var background = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CurrentBackgroundSwatch");
            Check(accent.Visible && accent.AccessibleName == text.T("Accent color") + " #626525"
                && accent.AccessibleDescription == text.T("Selected"),
                language + " non-swatch accent has a visible selected swatch with its hex and localized description");
            Check(background.Visible && background.AccessibleName == text.T("Background color") + " #F6F7F9"
                && background.AccessibleDescription == text.T("Selected"),
                language + " non-swatch background has a visible selected swatch with its hex and localized description");
            foreach (var current in new[] { accent, background })
            {
                var row = current.Parent!;
                var more = row.Controls.OfType<Button>().Single(button => button.AccessibleName!.EndsWith(" more colors", StringComparison.Ordinal));
                Check(row.Controls.GetChildIndex(current) == row.Controls.GetChildIndex(more) - 1
                    && current.Right <= more.Left && row.ClientRectangle.Contains(current.Bounds)
                    && row.ClientRectangle.Contains(more.Bounds),
                    language + " current swatch fits immediately before More colors");
            }
            var accentChoice = Descendants(dialog).OfType<Button>()
                .Single(button => button.AccessibleName == text.T("Accent color") + " #CA4D8C");
            var backgroundChoice = Descendants(dialog).OfType<Button>()
                .Single(button => button.AccessibleName == text.T("Background color") + " #FFFFFF");
            accentChoice.PerformClick();
            backgroundChoice.PerformClick();
            Check(!accent.Visible && !background.Visible
                && accentChoice.AccessibleDescription == text.T("Selected")
                && backgroundChoice.AccessibleDescription == text.T("Selected"),
                language + " clicking preset swatches hides the extra current swatches and selects the choices");
            Check(dialog.Accent.ToArgb() == SessionStore.ParseColor("#CA4D8C").ToArgb()
                && dialog.Background.ToArgb() == Color.White.ToArgb(),
                language + " real swatch button clicks update both seed colors");
            dialog.Close();
        }

        foreach (int dpi in new[] { 96, 120, 144 })
        {
            var size = new Size((int)Math.Round(600 * dpi / 96d), (int)Math.Round(220 * dpi / 96d));
            CheckCustomPreviewLayout(CustomColorsDialog.CalculatePreviewLayout(size, dpi), dpi + " DPI");
        }
    }

    private static void CheckCustomColorDialogLayout(CustomColorsDialog dialog, string label)
    {
        int S(int value) => (int)Math.Round(value * dialog.DeviceDpi / 96d);
        var swatches = Descendants(dialog).OfType<Button>()
            .Where(button => button.AccessibleName?.Contains(" #", StringComparison.Ordinal) == true).ToArray();
        Check(swatches.Length >= 24 && swatches.All(button => button.Size == new Size(S(32), S(32))
                && button.Margin == new Padding(0, 0, S(3), 0)),
            label + ": shown swatches and their margins use current DeviceDpi " + dialog.DeviceDpi);
        var preview = Descendants(dialog).Single(control => control.AccessibleName == "Custom colors preview");
        Check(dialog.Padding == new Padding(S(16))
                && preview.MinimumSize == new Size(S(600), S(220))
                && preview.Width >= S(600) && preview.Height >= S(220)
                && preview.Margin == new Padding(0, S(12), 0, S(8)),
            label + ": dialog padding and preview size and margins use current DeviceDpi");
        CheckCustomPreviewLayout(dialog.PreviewBounds, label + " actual preview");
        // The background is chosen first: its row sits above the accent row. Recent combinations also end in
        // a hex value, so each row is found by holding every one of its own swatch colors.
        var recent = swatches.Where(button => button.AccessibleName!.StartsWith("Recent combinations #", StringComparison.Ordinal)).ToArray();
        Control RowHolding(params string[] hexes) => swatches.Except(recent).Select(button => button.Parent!).Distinct()
            .Single(row => hexes.All(hex => row.Controls.OfType<Button>().Any(button => button.AccessibleName!.EndsWith(" " + hex, StringComparison.Ordinal))));
        var backgroundRow = RowHolding("#FFFFFF", "#F7F3E8", "#E6E5E1", "#25282D", "#302336");
        var accentRow = RowHolding("#0078D4", "#008B8B", "#CA4D8C", "#64748B");
        Check(backgroundRow.PointToScreen(Point.Empty).Y < accentRow.PointToScreen(Point.Empty).Y,
            label + ": the background row comes before the accent row");
        if (recent.Length > 0)
        {
            var recentRow = recent[0].Parent!;
            Check(recent.All(button => button.Parent == recentRow)
                && recentRow.PointToScreen(Point.Empty).Y < backgroundRow.PointToScreen(Point.Empty).Y
                && Descendants(dialog).OfType<Label>().Any(control => control.AccessibleName == "Recent combinations"),
                label + ": the Recent combinations row sits above the background row");
        }
    }

    private static void CheckCustomPreviewLayout(CustomColorsDialog.PreviewLayout layout, string label)
    {
        var siblings = new[] { layout.Title, layout.SidebarSelected, layout.SidebarSecondary, layout.FileCard };
        var rows = new[] { layout.FileHeader, layout.FirstRow, layout.SelectedRow, layout.LastRow, layout.Link };
        Check(siblings.All(bounds => bounds.Width > 0 && bounds.Height > 0 && layout.Bounds.Contains(bounds))
                && rows.All(bounds => bounds.Width > 0 && bounds.Height > 0 && layout.FileCard.Contains(bounds)),
            label + ": preview regions fit inside the preview and every file row and link fits inside its card");
        Check(DoNotOverlap(siblings) && DoNotOverlap(rows),
            label + ": preview siblings, file rows and link do not overlap");

        static bool DoNotOverlap(Rectangle[] bounds)
        {
            for (int i = 0; i < bounds.Length; i++)
                for (int j = i + 1; j < bounds.Length; j++)
                    if (bounds[i].IntersectsWith(bounds[j])) return false;
            return true;
        }
    }

    private static void CheckCustomSurfaces(MainForm form, bool dark, string label)
    {
        var sidebar = (Panel)typeof(MainForm).GetField("sidebar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var visual = (IVisualProperties)typeof(ShellViewHost).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form.GetPane(0).ActiveView!)!;
        Check(form.ColorTheme == "custom" && Theme.IsDark == dark && form.BackColor == Theme.Canvas
            && form.FolderTree.Tree.BackColor == Theme.Sidebar && sidebar.BackColor == Theme.Sidebar,
            label + " recolors form, tree and sidebar");
        Check(visual.GetColor(2, out int back) == 0 && back == ColorTranslator.ToWin32(Theme.ListBack)
            && visual.GetColor(3, out int sort) == 0 && sort == ColorTranslator.ToWin32(Theme.ListBack)
            && visual.GetColor(1, out int fore) == 0 && fore == ColorTranslator.ToWin32(Theme.Text),
            label + " updates native list background, sorted column and text");
    }

    private static void CheckNoCustomColors(MainForm form, PaletteGenerator.ThemeColors old, string label)
    {
        var previous = new[] { old.Canvas, old.Surface, old.Border, old.Text, old.Muted, old.Accent, old.Selected, old.ErrorText, old.Hover, old.Pressed }
            .Select(color => color.ToArgb()).ToHashSet();
        var current = new[] { Theme.Canvas, Theme.Surface, Theme.Border, Theme.Text, Theme.Muted, Theme.Accent,
            Theme.Selected, Theme.ErrorText, Theme.Hover, Theme.Sidebar, Theme.ListBack, Theme.SortColumn }.Select(color => color.ToArgb()).ToHashSet();
        foreach (var control in Descendants(form).Prepend(form))
        {
            Check(!previous.Contains(control.BackColor.ToArgb()) || current.Contains(control.BackColor.ToArgb()),
                $"{label}: {control.GetType().Name} {control.AccessibleName} background has no stale custom role");
            Check(!previous.Contains(control.ForeColor.ToArgb()) || current.Contains(control.ForeColor.ToArgb()),
                $"{label}: {control.GetType().Name} {control.AccessibleName} foreground has no stale custom role");
        }
    }
}
