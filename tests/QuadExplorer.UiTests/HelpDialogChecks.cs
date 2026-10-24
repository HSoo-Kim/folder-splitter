using QuadExplorer;

internal static partial class Program
{
    private static void RunHelpDialogScenario(string root)
    {
        string? expected = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_DESKTOP");
        var actual = new System.Text.StringBuilder(256);
        if (expected is null || !expected.StartsWith("FolderSplitter-QA-", StringComparison.Ordinal)
            || !GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, actual, 512, out _)
            || actual.ToString() != expected)
            throw new InvalidOperationException("Help dialog checks require the private QA desktop.");

        string? output = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR");
        if (output is { Length: > 0 }) Directory.CreateDirectory(output);
        foreach (string mode in new[] { "light", "dark" })
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        foreach (float points in new[] { 9.5f, 18f })
        {
            Theme.Set(mode);
            var text = new UiText(language);
            using var font = new Font(Theme.UiFace, points);
            using var dialog = new ShortcutHelpDialog(text, font);
            string name = $"help {mode}/{language}/{points}";
            dialog.Shown += (_, _) =>
            {
                try
                {
                    var controls = Descendants(dialog).ToArray();
                    var content = controls.OfType<Panel>().Single(panel => panel.AccessibleName == "Help content");
                    var navigation = controls.OfType<FlowLayoutPanel>().Single(panel => panel.AccessibleName == "Help sections");
                    var headings = Descendants(content).OfType<Label>()
                        .Where(label => label.AccessibleName?.StartsWith("Help section ", StringComparison.Ordinal) == true).ToArray();
                    var labels = Descendants(content).OfType<Label>().ToArray();
                    var buttons = navigation.Controls.OfType<Button>().ToArray();
                    string[] paragraphs = text.Help.Replace("\r", "").Trim()
                        .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
                    Check(headings.Length == paragraphs.Length - 1
                        && buttons.Select(button => button.Text).SequenceEqual(headings.Select(label => label.Text)),
                        name + " has localized category navigation for every section");
                    static string Words(string value) =>
                        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    Check(Words(string.Join(" ", labels.Select(label => label.Text))) == Words(text.Help)
                        && labels.Single(label => label.AccessibleName == "Help footer").Text == paragraphs[^1],
                        name + " preserves all help rows and the complete footer without translation copies");
                    Check(labels.Any(label => label.AccessibleName == "Help shortcut" && label.Text == "Ctrl+Shift+T")
                        && labels.Any(label => label.AccessibleName == "Help shortcut" && label.Text.StartsWith("Ctrl+W", StringComparison.Ordinal)),
                        name + " includes close and restore shortcuts");
                    Check(dialog.BackColor == Theme.Canvas && content.BackColor == Theme.Surface
                        && labels.Where(label => label.AccessibleName == "Help shortcut")
                            .All(label => label.BackColor == Theme.Canvas && label.ForeColor == Theme.Text)
                        && labels.Where(label => label.AccessibleName is "Help note" or "Help footer")
                            .All(label => label.ForeColor == Theme.Muted)
                        && labels.All(label => label.Font.SizeInPoints >= points),
                        name + " uses the active palette and supplied readable font");
                    var work = Screen.FromControl(dialog).WorkingArea;
                    Check(dialog.Width <= work.Width && dialog.Height <= work.Height
                        && dialog.MinimumSize.Width <= work.Width && dialog.MinimumSize.Height <= work.Height,
                        name + " fits the work area with a usable minimum size");
                    Size regular = dialog.Size;
                    foreach (bool narrow in new[] { false, true })
                    {
                        dialog.Size = narrow ? dialog.MinimumSize : regular;
                        dialog.PerformLayout();
                        content.AutoScrollPosition = Point.Empty;
                        CheckHelpGeometry(dialog, content, navigation, name + (narrow ? " narrow" : " regular"));
                        foreach (var button in buttons)
                        {
                            button.PerformClick();
                            int index = Array.IndexOf(buttons, button);
                            var headingBounds = content.RectangleToClient(headings[index].RectangleToScreen(headings[index].ClientRectangle));
                            Check(headingBounds.Bottom > 0 && headingBounds.Top < content.ClientSize.Height
                                && button.BackColor == Theme.Selected && button.ForeColor == Theme.Accent,
                                name + " category " + index + " reveals its heading and selected state"
                                + $" (heading={headingBounds}, client={content.ClientSize}, scroll={content.AutoScrollPosition}, range={content.AutoScrollMinSize})");
                        }
                        InvokeHelpKey(dialog, Keys.Control | Keys.End);
                        var footer = labels.Single(label => label.AccessibleName == "Help footer");
                        var footerBounds = content.RectangleToClient(footer.RectangleToScreen(footer.ClientRectangle));
                        Check(footerBounds.Bottom <= content.ClientSize.Height && footerBounds.Bottom > 0
                            && !content.HorizontalScroll.Visible,
                            name + " reaches the footer without horizontal scrolling"
                            + $" (footer={footerBounds}, client={content.ClientSize}, scroll={content.AutoScrollPosition}, range={content.AutoScrollMinSize})");
                        InvokeHelpKey(dialog, Keys.Control | Keys.Home);
                        Check(content.AutoScrollPosition.Y == 0, name + " keyboard returns to the beginning");
                        InvokeHelpKey(dialog, Keys.PageDown);
                        Check(content.AutoScrollPosition.Y < 0, name + " keyboard pages through long help");
                        InvokeHelpKey(dialog, Keys.PageUp);
                        Check(content.AutoScrollPosition.Y == 0, name + " keyboard pages back without global input");
                        buttons[0].PerformClick();
                        if (output is { Length: > 0 })
                        {
                            dialog.Refresh();
                            using var image = new Bitmap(dialog.Width, dialog.Height);
                            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                            string scale = points > 10 ? "large" : "regular";
                            image.Save(Path.Combine(output, $"help-{mode}-{language}-{scale}-{(narrow ? "narrow" : "regular")}.png"));
                        }
                    }
                    bool closed = false;
                    dialog.FormClosed += (_, _) => closed = true;
                    Check(InvokeHelpKey(dialog, Keys.Escape) && closed && dialog.DialogResult == DialogResult.Cancel,
                        name + " Escape closes modeless help through the dialog key route");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + name + ": " + ex);
                    if (output is { Length: > 0 })
                    {
                        using var image = new Bitmap(dialog.Width, dialog.Height);
                        dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                        image.Save(Path.Combine(output, $"help-failed-{mode}-{language}-{points}.png"));
                    }
                }
                finally { if (!dialog.IsDisposed) dialog.Close(); }
            };
            // Application.Run shows a modeless form and dispatches Shown on this
            // thread's already-verified private desktop. No global input is sent.
            Application.Run(dialog);
        }
        Theme.Set("light");
    }

    private static bool InvokeHelpKey(ShortcutHelpDialog dialog, Keys keys)
    {
        var method = typeof(ShortcutHelpDialog).GetMethod("ProcessCmdKey",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (bool)method.Invoke(dialog, [Message.Create(dialog.Handle, 0x100, (nint)keys, 0), keys])!;
    }

    private static void CheckHelpGeometry(Form dialog, Panel content, FlowLayoutPanel navigation, string name)
    {
        var labels = Descendants(content).OfType<Label>().ToArray();
        Check(labels.All(label => label.Width > 0
            && label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height
            && label.Width >= label.GetPreferredSize(new Size(label.Width, 0)).Width
            && label.Left >= 0 && label.Right <= content.ClientSize.Width),
            name + " wraps each shortcut, description, note and heading without clipping");
        Check(!labels.SelectMany((label, index) => labels.Skip(index + 1)
                .Select(other => (label, other))).Any(pair => pair.label.Bounds.IntersectsWith(pair.other.Bounds)),
            name + " content rows do not overlap");
        Check(navigation.Controls.OfType<Button>().All(button =>
            navigation.ClientRectangle.Contains(button.Bounds)
            && button.Height >= button.GetPreferredSize(Size.Empty).Height
            && button.Width >= button.GetPreferredSize(Size.Empty).Width),
            name + " category buttons wrap inside navigation without clipping");
        var close = Descendants(dialog).OfType<Button>().Single(button => button.AccessibleName == "Close help");
        Rectangle closeBounds = dialog.RectangleToClient(close.RectangleToScreen(close.ClientRectangle));
        Rectangle contentBounds = dialog.RectangleToClient(content.RectangleToScreen(content.ClientRectangle));
        Rectangle navigationBounds = dialog.RectangleToClient(navigation.RectangleToScreen(navigation.ClientRectangle));
        Check(dialog.ClientRectangle.Contains(closeBounds) && !closeBounds.IntersectsWith(contentBounds)
            && !navigationBounds.IntersectsWith(contentBounds) && content.Height >= dialog.Font.Height * 2,
            name + " keeps close controls, navigation and scrolling content separate");
    }
}
