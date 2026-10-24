namespace QuadExplorer;

// Compares the items directly inside two folders: what exists on one side only, and files whose
// size or modified time differ. Subfolders are compared by name only.
internal static class FolderComparison
{
    internal enum Kind { OnlyLeft, OnlyRight, LeftNewer, RightNewer, SizeDiffers, Different, Same }

    internal sealed record Entry(string Name, Kind Kind, FileSystemInfo? Left, FileSystemInfo? Right);

    internal static IReadOnlyList<Entry> Compare(string left, string right, int limit = 20_000)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        Dictionary<string, FileSystemInfo> Read(string folder) => new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options)
            .Take(limit).ToDictionary(info => info.Name, StringComparer.OrdinalIgnoreCase);
        var a = Read(left);
        var b = Read(right);
        var entries = new List<Entry>();
        foreach (var name in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            a.TryGetValue(name, out var l);
            b.TryGetValue(name, out var r);
            entries.Add(new Entry(l?.Name ?? r!.Name, Classify(l, r), l, r));
        }
        return entries;
    }

    internal static Kind Classify(FileSystemInfo? left, FileSystemInfo? right)
    {
        if (right is null) return Kind.OnlyLeft;
        if (left is null) return Kind.OnlyRight;
        if (left is DirectoryInfo != right is DirectoryInfo) return Kind.Different;
        if (left is not FileInfo l || right is not FileInfo r) return Kind.Same;
        double seconds = (l.LastWriteTimeUtc - r.LastWriteTimeUtc).TotalSeconds;
        if (Math.Abs(seconds) > 2) return seconds > 0 ? Kind.LeftNewer : Kind.RightNewer; // FAT keeps 2-second times
        return l.Length != r.Length ? Kind.SizeDiffers : Kind.Same;
    }
}

// Shows the comparison and selects the differing items in either pane.
internal sealed class CompareDialog : Form
{
    private readonly UiText text;
    private readonly string left, right;
    private readonly int leftPane, rightPane;
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, AccessibleName = "Comparison" };
    private readonly CheckBox showSame = new() { AutoSize = true };
    private readonly Label summary = new() { AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
    private readonly Button selectLeft, selectRight;

    internal IReadOnlyList<FolderComparison.Entry> Entries { get; private set; } = [];
    internal Task Loading { get; }

    internal CompareDialog(string left, int leftPane, string right, int rightPane, UiText text,
        Action<IReadOnlyList<string>> selectInLeft, Action<IReadOnlyList<string>> selectInRight)
    {
        this.text = text;
        this.left = left;
        this.right = right;
        this.leftPane = leftPane;
        this.rightPane = rightPane;
        Text = text.T("Compare panes");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(900, 600);
        MinimumSize = new Size(600, 400);
        var top = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 48, Padding = new Padding(10, 8, 10, 0), AutoEllipsis = true,
            Text = text.T("Pane {0}", leftPane) + ": " + left + "\n" + text.T("Pane {0}", rightPane) + ": " + right
        };
        list.Columns.Add(text.T("Name"), 280);
        list.Columns.Add(text.T("Pane {0}", leftPane), 200);
        list.Columns.Add(text.T("Pane {0}", rightPane), 200);
        list.Columns.Add(text.T("Result"), 160);
        showSame.Text = text.T("Show identical items");
        showSame.CheckedChanged += (_, _) => Fill();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 6, 10, 10) };
        var close = Theme.Button(text.T("Close"), "Close", Close, 90);
        selectRight = Theme.Button(text.T("Select in pane {0}", rightPane), "Select in right pane", () => selectInRight(Differing(right: true)), 150);
        selectLeft = Theme.Button(text.T("Select in pane {0}", leftPane), "Select in left pane", () => selectInLeft(Differing(right: false)), 150);
        foreach (var button in new[] { close, selectRight, selectLeft }) { button.Dock = DockStyle.None; button.AutoSize = true; buttons.Controls.Add(button); }
        buttons.Controls.Add(showSame);
        buttons.Controls.Add(summary);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        body.Controls.Add(list);
        Controls.Add(body);
        Controls.Add(buttons);
        Controls.Add(top);
        CancelButton = close;
        Theme.Apply(this);
        summary.Text = text.T("Comparing...");
        selectLeft.Enabled = selectRight.Enabled = false;
        Loading = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try { Entries = await Task.Run(() => FolderComparison.Compare(left, right)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            summary.Text = error.Message;
            return;
        }
        if (!IsDisposed) Fill();
    }

    // Items to select on one side: those missing from or different on the other side.
    internal IReadOnlyList<string> Differing(bool right) => Entries
        .Where(entry => entry.Kind != FolderComparison.Kind.Same && (right ? entry.Right : entry.Left) is not null)
        .Select(entry => (right ? entry.Right : entry.Left)!.FullName).ToList();

    private void Fill()
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var entry in Entries.Where(entry => showSame.Checked || entry.Kind != FolderComparison.Kind.Same).Take(5000))
        {
            string result = entry.Kind switch
            {
                FolderComparison.Kind.OnlyLeft => text.T("Only in pane {0}", leftPane),
                FolderComparison.Kind.OnlyRight => text.T("Only in pane {0}", rightPane),
                FolderComparison.Kind.LeftNewer => text.T("Newer in pane {0}", leftPane),
                FolderComparison.Kind.RightNewer => text.T("Newer in pane {0}", rightPane),
                FolderComparison.Kind.SizeDiffers => text.T("Different size"),
                FolderComparison.Kind.Different => text.T("Folder on one side, file on the other"),
                _ => text.T("Same")
            };
            var item = new ListViewItem([entry.Name, Describe(entry.Left), Describe(entry.Right), result]);
            list.Items.Add(item);
        }
        list.EndUpdate();
        int different = Entries.Count(entry => entry.Kind != FolderComparison.Kind.Same);
        summary.Text = text.T("{0} of {1} items differ", different, Entries.Count);
        selectLeft.Enabled = Differing(false).Count > 0;
        selectRight.Enabled = Differing(true).Count > 0;
    }

    private string Describe(FileSystemInfo? info) => info switch
    {
        null => "",
        FileInfo file => ExplorerPane.FormatSize(file.Length) + "  " + file.LastWriteTime.ToString("g"),
        _ => text.T("Folder") + "  " + info.LastWriteTime.ToString("g")
    };
}
