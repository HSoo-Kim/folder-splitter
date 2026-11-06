namespace QuadExplorer;

internal sealed class GroupCloseDialog : Form
{
    private readonly CheckBox? dontAskAgain;

    internal bool DontAskAgain => dontAskAgain?.Checked == true;

    internal GroupCloseDialog(UiText text, string name, int count, bool deleteSaved, Font font)
    {
        SuspendLayout();
        Text = text.T(deleteSaved ? "Delete group" : "Close group");
        Name = "GroupCloseConfirmation";
        AccessibleName = Text;
        Font = font;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;

        var content = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            ColumnCount = 1, Padding = new Padding(20), Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460));
        string message = deleteSaved
            ? text.T("Delete the saved group '{0}'? Its {1} open tabs will also close.", name, count)
            : string.IsNullOrWhiteSpace(name) ? text.T("Close {0} tabs in this group?", count)
            : text.T("Close {1} tabs in the '{0}' group?", name, count);
        Label Caption(string controlName, string value) => new()
        {
            Name = controlName, Text = value, AccessibleName = value, AutoSize = true,
            MaximumSize = new Size(460, 0), Margin = new Padding(0, 0, 0, 12), UseMnemonic = false
        };
        content.Controls.Add(Caption("GroupCloseMessage", message));
        if (!deleteSaved)
        {
            var hint = Caption("GroupCloseUndoHint", text.T("You can reopen them with Ctrl+Shift+T after closing."));
            hint.ForeColor = Theme.Muted;
            content.Controls.Add(hint);
            string caption = text.T("Don't ask again");
            dontAskAgain = new CheckBox
            {
                Name = "GroupCloseDontAskAgain", Text = caption, AccessibleName = caption,
                AutoSize = true, Margin = new Padding(0, 0, 0, 16), UseMnemonic = false
            };
            content.Controls.Add(dontAskAgain);
        }
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty
        };
        string cancelText = text.T("Cancel"), acceptText = text.T(deleteSaved ? "Delete" : "Close");
        var cancel = Theme.Button(cancelText, cancelText, () => { DialogResult = DialogResult.Cancel; Close(); }, 90);
        var accept = Theme.Button(acceptText, acceptText, () => { DialogResult = DialogResult.OK; Close(); }, 90);
        cancel.Name = "GroupCloseCancel";
        accept.Name = "GroupCloseAccept";
        cancel.Dock = accept.Dock = DockStyle.None;
        cancel.DialogResult = DialogResult.Cancel;
        accept.DialogResult = DialogResult.OK;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        content.Controls.Add(buttons);
        Controls.Add(content);
        AcceptButton = accept;
        CancelButton = cancel;
        FormClosing += (_, _) => { if (DialogResult != DialogResult.OK) DialogResult = DialogResult.Cancel; };
        Theme.Apply(this);
        ResumeLayout(performLayout: true);
    }
}
