using QuadExplorer;
using System.Reflection;

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
                    EditCustomDialog(true);
                    Check(form.ColorTheme == "custom", "applying the custom color dialog selects the custom theme");
                    CheckCustomSurfaces(form, true, "dialog custom");
                    savedCanvas = Theme.Canvas;
                }
                catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + exception); }
                finally { form.ExitApplication(); }
            };
            Application.Run(form);

            void EditCustomDialog(bool apply)
            {
                Exception? failure = null;
                using var timer = new System.Windows.Forms.Timer { Interval = 30 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<CustomColorsDialog>().FirstOrDefault();
                    if (dialog is null) return;
                    timer.Stop();
                    try
                    {
                        var before = dialog.PreviewColors;
                        dialog.PickSwatch(true, 6);
                        dialog.PickSwatch(false, 11);
                        savedAccent = dialog.Accent;
                        savedBackground = dialog.Background;
                        var expected = PaletteGenerator.Generate(savedAccent, savedBackground);
                        Check(savedAccent.ToArgb() == Color.FromArgb(202, 77, 140).ToArgb()
                            && savedBackground.ToArgb() == Color.FromArgb(48, 35, 54).ToArgb(), "dialog swatches update both seeds");
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
                var themeMenu = Descendants(form).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>())
                    .Single(item => item.AccessibleName == "Color theme");
                themeMenu.ShowDropDown();
                var custom = themeMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.AccessibleName == "Custom colors");
                Check(!custom.Checked, "custom menu entry reflects the active preset");
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
        using (var reopened = new MainForm(session, [], null, [folder]))
        {
            Check(reopened.ColorTheme == "custom" && reopened.BackColor.ToArgb() == savedCanvas.ToArgb()
                && Theme.Canvas.ToArgb() == savedCanvas.ToArgb(), "reopened form restores the same generated custom canvas");
            reopened.Shown += (_, _) => reopened.ExitApplication();
            Application.Run(reopened);
        }
        Theme.Set("light");
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
