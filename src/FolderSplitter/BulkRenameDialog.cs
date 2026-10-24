namespace QuadExplorer;

// Rename several selected items at once, with every new name previewed before anything changes.
internal sealed class BulkRenameDialog : Form
{
    private readonly UiText text;
    private readonly IReadOnlyList<string> paths;
    private readonly TextBox find = new() { Dock = DockStyle.Fill, AccessibleName = "Find" };
    private readonly TextBox replace = new() { Dock = DockStyle.Fill, AccessibleName = "Replace with" };
    private readonly TextBox pattern = new() { Dock = DockStyle.Fill, Text = "{name}", AccessibleName = "Name pattern" };
    private readonly CheckBox regex = new() { AutoSize = true };
    private readonly CheckBox matchCase = new() { AutoSize = true };
    private readonly NumericUpDown start = new() { Minimum = 0, Maximum = 999999, Value = 1, Width = 80 };
    private readonly NumericUpDown digits = new() { Minimum = 1, Maximum = 9, Value = 1, Width = 60 };
    private readonly ListView preview = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowItemToolTips = true, AccessibleName = "Rename preview"
    };
    private readonly Label summary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button apply, cancel;

    internal IReadOnlyList<BulkRename.Plan> Plans { get; private set; } = [];
    internal event Action<bool, string?>? Completed;

    internal BulkRenameDialog(IReadOnlyList<string> paths, UiText text)
    {
        this.text = text;
        this.paths = paths;
        Text = text.T("Rename {0} items", paths.Count);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(760, 560);
        MinimumSize = new Size(560, 420);
        var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(10, 10, 10, 4) };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Label Caption(string key) => new() { Text = text.T(key), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };
        options.Controls.Add(Caption("Find"), 0, 0);
        options.Controls.Add(find, 1, 0);
        options.Controls.Add(Caption("Replace with"), 2, 0);
        options.Controls.Add(replace, 3, 0);
        regex.Text = text.T("Regular expression");
        matchCase.Text = text.T("Match case");
        var flags = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        flags.Controls.Add(regex);
        flags.Controls.Add(matchCase);
        options.Controls.Add(flags, 1, 1);
        options.SetColumnSpan(flags, 3);
        options.Controls.Add(Caption("New name"), 0, 2);
        options.Controls.Add(pattern, 1, 2);
        var numbering = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        numbering.Controls.Add(Caption("Start at"));
        numbering.Controls.Add(start);
        numbering.Controls.Add(Caption("Digits"));
        numbering.Controls.Add(digits);
        options.Controls.Add(numbering, 2, 2);
        options.SetColumnSpan(numbering, 2);
        var hint = new Label
        {
            Text = text.T("{0}: the name after find and replace   {1}: a running number   The extension is kept.", "{name}", "{n}"),
            AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 2, 0, 4)
        };
        options.Controls.Add(hint, 1, 3);
        options.SetColumnSpan(hint, 3);
        preview.Columns.Add(text.T("Current name"), 300);
        preview.Columns.Add(text.T("New name"), 300);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 6, 10, 10) };
        cancel = Theme.Button(text.T("Cancel"), "Cancel", Close, 90);
        apply = Theme.Button(text.T("Rename"), "Rename", Apply, 110);
        cancel.Dock = apply.Dock = DockStyle.None;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(summary);
        summary.AutoSize = true;
        summary.Margin = new Padding(0, 8, 12, 0);
        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        list.Controls.Add(preview);
        Controls.Add(list);
        Controls.Add(buttons);
        Controls.Add(options);
        AcceptButton = apply;
        CancelButton = cancel;
        foreach (Control control in new Control[] { find, replace, pattern, regex, matchCase, start, digits })
        {
            control.TextChanged += (_, _) => Refresh();
            if (control is CheckBox box) box.CheckedChanged += (_, _) => Refresh();
            if (control is NumericUpDown number) number.ValueChanged += (_, _) => Refresh();
        }
        preview.Resize += (_, _) =>
        {
            int width = Math.Max(200, preview.ClientSize.Width - 4);
            preview.Columns[0].Width = width / 2;
            preview.Columns[1].Width = width - width / 2;
        };
        Theme.Apply(this);
        Refresh();
    }

    internal void SetOptions(string find, string replace, string pattern = "{name}", bool regex = false, bool matchCase = false, int start = 1, int digits = 1)
    {
        this.find.Text = find;
        this.replace.Text = replace;
        this.pattern.Text = pattern;
        this.regex.Checked = regex;
        this.matchCase.Checked = matchCase;
        this.start.Value = start;
        this.digits.Value = digits;
        Refresh();
    }

    internal bool CanApply => apply.Enabled;

    public new void Refresh()
    {
        Plans = BulkRename.Build(paths, find.Text, replace.Text, regex.Checked, matchCase.Checked, pattern.Text, (int)start.Value, (int)digits.Value);
        preview.BeginUpdate();
        preview.Items.Clear();
        foreach (var plan in Plans)
        {
            var item = new ListViewItem([plan.OldName, plan.NewName]) { UseItemStyleForSubItems = false };
            if (plan.Problem is { } problem)
            {
                item.SubItems[1].ForeColor = Theme.ErrorText;
                item.ToolTipText = Problem(problem);
            }
            else if (plan.NewName == plan.OldName) item.SubItems[1].ForeColor = Theme.Muted;
            preview.Items.Add(item);
        }
        preview.EndUpdate();
        int changes = Plans.Count(plan => plan.Problem is null && plan.NewName != plan.OldName);
        int problems = Plans.Count(plan => plan.Problem is not null);
        summary.Text = problems > 0 ? text.T("{0} problems: {1}", problems, Problem(Plans.First(plan => plan.Problem is not null).Problem!))
            : text.T("{0} of {1} names change", changes, Plans.Count);
        summary.ForeColor = problems > 0 ? Theme.ErrorText : Theme.Muted;
        apply.Enabled = problems == 0 && changes > 0;
    }

    private string Problem(string key) => text.T(key switch
    {
        "pattern" => "The regular expression is not valid.",
        "empty" => "A name would be empty.",
        "invalid" => "A name contains characters that are not allowed.",
        "duplicate" => "Two items would get the same name.",
        _ => "An item with that name already exists."
    });

    private void Apply()
    {
        var changes = Plans.Where(plan => plan.Problem is null && plan.NewName != plan.OldName).Select(plan => (plan.Path, plan.NewName)).ToList();
        if (changes.Count == 0) return;
        apply.Enabled = false;
        FileOperations.StartRename(changes, Handle, (done, error) =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                Completed?.Invoke(done, error);
                if (error is null) Close();
                else { summary.Text = error; summary.ForeColor = Theme.ErrorText; apply.Enabled = true; }
            });
        });
    }
}
