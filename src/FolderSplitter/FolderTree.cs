using System.Security;

namespace QuadExplorer;

internal enum FolderTreeTarget { CurrentPane, NewTab, NewPane, ExplorerWindow }

internal sealed class FolderTree : UserControl
{
    private sealed class Folder(string path, bool home = false)
    {
        public string Path { get; } = path;
        public bool Home { get; } = home;
        public bool Loaded { get; set; }
        public bool Unavailable { get; set; }
        public int Generation { get; set; }
        public Task<DirectoryListing>? Pending { get; set; }
    }

    private sealed record Notice(string Key, string? Detail = null);
    private sealed record DirectoryEntry(string Path, string Name);
    private sealed record DirectoryListing(List<DirectoryEntry> Entries, Notice? Notice = null);
    private readonly UiText text;
    private readonly string[]? suppliedRoots;
    private readonly Image folderIcon = Theme.FolderIcon();
    private readonly ImageList images = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(20, 20) };
    private readonly Label heading = new()
    {
        Dock = DockStyle.Top, Height = 28, Padding = new Padding(7, 0, 0, 0),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ContextMenuStrip menu = new();
    private readonly List<(ToolStripItem Item, string Key)> menuLabels = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private CancellationTokenSource? followCancellation;
    private Task? rootsLoading;
    private int followGeneration;

    internal TreeView Tree { get; } = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        HideSelection = false,
        FullRowSelect = false,
        HotTracking = true,
        Scrollable = true,
        ShowLines = true,
        ShowPlusMinus = true,
        ShowRootLines = true
    };

    public event Action<string, FolderTreeTarget>? OpenRequested;

    public FolderTree(UiText text, IEnumerable<string>? roots = null)
    {
        this.text = text;
        suppliedRoots = roots?.ToArray();
        lifetimeToken = lifetime.Token;
        Dock = DockStyle.Fill;
        Padding = new Padding(1);
        images.Images.Add(folderIcon);
        Tree.ImageList = images;
        Controls.Add(Tree);
        Controls.Add(heading);
        Tree.BeforeSelect += (_, e) =>
            e.Cancel = e.Node?.Tag is not Folder;
        Tree.BeforeExpand += async (_, e) =>
        {
            if (e.Node is not { Tag: Folder } node) { e.Cancel = true; return; }
            await LoadForUserAsync(node);
        };
        Tree.MouseDown += TreeMouseDown;
        Tree.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                RequestOpen(e.Control ? FolderTreeTarget.NewTab : FolderTreeTarget.CurrentPane);
            }
            else if (e.KeyCode == Keys.F5)
            {
                e.Handled = e.SuppressKeyPress = true;
                await RefreshSelectedAsync();
            }
        };
        AddMenu("Open in current pane", () => RequestOpen(FolderTreeTarget.CurrentPane));
        AddMenu("Open in new tab", () => RequestOpen(FolderTreeTarget.NewTab));
        AddMenu("Open in new pane", () => RequestOpen(FolderTreeTarget.NewPane));
        AddMenu("Open in File Explorer", () => RequestOpen(FolderTreeTarget.ExplorerWindow));
        menu.Items.Add(new ToolStripSeparator());
        AddMenu("Refresh    F5", async () => await RefreshSelectedAsync());
        menu.Opening += (_, e) =>
        {
            e.Cancel = Tree.SelectedNode?.Tag is not Folder;
            bool available = Tree.SelectedNode?.Tag is Folder { Unavailable: false };
            foreach (var (item, _) in menuLabels.Take(4)) item.Enabled = available;
        };
        Tree.ContextMenuStrip = menu;
        RefreshLanguage();
        RefreshTheme();
    }

    private void AddMenu(string key, Action action)
    {
        var item = new ToolStripMenuItem();
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        menuLabels.Add((item, key));
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { await EnsureRootsAsync(); }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    }

    public void RefreshLanguage()
    {
        AccessibleName = Tree.AccessibleName = text.T("Folder tree");
        heading.Text = text.T("Folder tree");
        foreach (var (item, key) in menuLabels) item.Text = text.T(key);
        foreach (TreeNode node in Tree.Nodes) LocalizeNode(node);
    }

    private void LocalizeNode(TreeNode node)
    {
        if (node.Tag is Notice notice)
            node.Text = notice.Detail is null ? text.T(notice.Key) : text.T(notice.Key, notice.Detail);
        else if (node.Tag is Folder { Home: true }) node.Text = text.T("Home");
        foreach (TreeNode child in node.Nodes) LocalizeNode(child);
    }

    public void RefreshTheme()
    {
        BackColor = Theme.Border;
        ForeColor = Theme.Text;
        Tree.BackColor = Theme.Canvas;
        Tree.ForeColor = Theme.Text;
        Tree.LineColor = Theme.Border;
        heading.BackColor = Theme.Canvas;
        heading.ForeColor = Theme.Muted;
        menu.BackColor = Theme.Canvas;
        menu.ForeColor = Theme.Text;
    }

    internal void RequestOpen(FolderTreeTarget target)
    {
        if (!lifetimeToken.IsCancellationRequested &&
            Tree.SelectedNode?.Tag is Folder { Unavailable: false } folder)
            OpenRequested?.Invoke(folder.Path, target);
    }

    private void TreeMouseDown(object? sender, MouseEventArgs e)
    {
        var hit = Tree.HitTest(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            // Blank space and informative children must not open the previous folder's menu.
            Tree.SelectedNode = hit.Node?.Tag is Folder ? hit.Node : null;
            return;
        }
        if (e.Clicks != 1 || e.Button is not (MouseButtons.Left or MouseButtons.Middle) ||
            (hit.Location & (TreeViewHitTestLocations.Label | TreeViewHitTestLocations.Image)) == 0 ||
            hit.Node?.Tag is not Folder) return;
        Tree.SelectedNode = hit.Node;
        RequestOpen(e.Button == MouseButtons.Middle || (ModifierKeys & Keys.Control) != 0
            ? FolderTreeTarget.NewTab : FolderTreeTarget.CurrentPane);
    }

    public async Task FollowPathAsync(string path)
    {
        if (lifetimeToken.IsCancellationRequested) return;
        // Owners call from their UI thread. Creating a hidden handle does not focus the tree.
        if (!IsHandleCreated)
        {
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Create the folder tree handle on its owning UI thread first.");
            _ = Handle;
        }
        try { await FollowPathCoreAsync(path).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    }

    private async Task FollowPathCoreAsync(string path)
    {
        var request = await OnUiAsync(() =>
        {
            followCancellation?.Cancel();
            var source = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            followCancellation = source;
            return (Source: source, Generation: ++followGeneration);
        }, lifetimeToken);
        var token = request.Source.Token;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
            string fullPath;
            try { fullPath = Normalize(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            await EnsureRootsAsync().WaitAsync(token).ConfigureAwait(false);
            TreeNode node = await OnUiAsync(() =>
            {
                var match = Tree.Nodes.Cast<TreeNode>()
                    .Where(n => n.Tag is Folder folder && ContainsPath(folder.Path, fullPath))
                    .OrderByDescending(n => ((Folder)n.Tag!).Path.Length).FirstOrDefault();
                if (match is not null) return match;
                // UNC/removable roots can be followed without probing every drive or network share.
                var root = CreateFolderNode(Normalize(Path.GetPathRoot(fullPath)!));
                Tree.Nodes.Add(root);
                return root;
            }, token).ConfigureAwait(false);

            while (true)
            {
                await EnsureLoadedAsync(node, token).ConfigureAwait(false);
                var step = await OnUiAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (request.Generation != followGeneration || node.TreeView != Tree)
                        return (Done: true, Next: (TreeNode?)null);
                    if (node.Tag is not Folder { Unavailable: false } folder)
                    {
                        node.Expand();
                        return (Done: true, Next: (TreeNode?)null);
                    }
                    node.Expand();
                    if (string.Equals(folder.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Tree.SelectedNode = node;
                        node.EnsureVisible();
                        return (Done: true, Next: (TreeNode?)null);
                    }
                    var next = node.Nodes.Cast<TreeNode>().FirstOrDefault(n =>
                        n.Tag is Folder child && ContainsPath(child.Path, fullPath));
                    return (Done: next is null, Next: next);
                }, token).ConfigureAwait(false);
                if (step.Done) return;
                node = step.Next!;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            try
            {
                await OnUiAsync(() =>
                {
                    if (ReferenceEquals(followCancellation, request.Source)) followCancellation = null;
                    return true;
                }, lifetimeToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
            request.Source.Dispose();
        }
    }

    private async Task EnsureRootsAsync()
    {
        var task = await OnUiAsync(() => rootsLoading ??= LoadRootsAsync(), lifetimeToken).ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    private async Task LoadRootsAsync()
    {
        var roots = await Task.Run(() =>
        {
            lifetimeToken.ThrowIfCancellationRequested();
            if (suppliedRoots is not null)
                return suppliedRoots.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal)
                    .Select(p => (Path: p, Home: false)).ToArray();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new List<(string Path, bool Home)>();
            if (!string.IsNullOrEmpty(home)) paths.Add((Normalize(home), true));
            foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                lifetimeToken.ThrowIfCancellationRequested();
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady &&
                        !paths.Any(p => string.Equals(p.Path, drive.Name, StringComparison.OrdinalIgnoreCase)))
                        paths.Add((Normalize(drive.Name), false));
                }
                catch (Exception ex) when (IsFileSystemError(ex)) { }
            }
            return paths.ToArray();
        }, lifetimeToken).ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            foreach (var root in roots) Tree.Nodes.Add(CreateFolderNode(root.Path, root.Home));
            return true;
        }, lifetimeToken).ConfigureAwait(false);
    }

    private TreeNode CreateFolderNode(string path, bool home = false)
    {
        string name = Path.GetFileName(path);
        var node = new TreeNode(home ? text.T("Home") : string.IsNullOrEmpty(name) ? path : name)
        {
            Name = path, Tag = new Folder(path, home), ImageIndex = 0, SelectedImageIndex = 0
        };
        node.Nodes.Add(CreateNoticeNode(new Notice("Loading folders...")));
        return node;
    }

    private TreeNode CreateNoticeNode(Notice notice) => new(
        notice.Detail is null ? text.T(notice.Key) : text.T(notice.Key, notice.Detail))
    {
        Tag = notice, ImageIndex = -1, SelectedImageIndex = -1
    };

    private async Task LoadForUserAsync(TreeNode node)
    {
        try { await EnsureLoadedAsync(node, lifetimeToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    }

    private async Task EnsureLoadedAsync(TreeNode node, CancellationToken token)
    {
        var load = await OnUiAsync(() =>
        {
            if (node.TreeView != Tree || node.Tag is not Folder folder || folder.Loaded)
                return (Folder: (Folder?)null, Generation: 0, Pending: (Task<DirectoryListing>?)null);
            folder.Pending ??= Task.Run(() => ReadDirectory(folder.Path, lifetimeToken), lifetimeToken);
            return (Folder: (Folder?)folder, folder.Generation, Pending: folder.Pending);
        }, token).ConfigureAwait(false);
        if (load.Pending is null) return;
        var result = await load.Pending.WaitAsync(token).ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            var folder = load.Folder!;
            if (node.TreeView != Tree || folder.Generation != load.Generation || folder.Loaded ||
                !ReferenceEquals(folder.Pending, load.Pending)) return false;
            Tree.BeginUpdate();
            try
            {
                bool expanded = node.IsExpanded;
                node.Nodes.Clear();
                foreach (var entry in result.Entries)
                {
                    var child = CreateFolderNode(entry.Path);
                    child.Text = entry.Name;
                    node.Nodes.Add(child);
                }
                if (result.Notice is { } notice) node.Nodes.Add(CreateNoticeNode(notice));
                folder.Unavailable = result.Notice is { Key: not "Folder link (expand disabled)" };
                folder.Loaded = true;
                folder.Pending = null;
                if (expanded) node.Expand();
            }
            finally { Tree.EndUpdate(); }
            return true;
        }, token).ConfigureAwait(false);
    }

    private static DirectoryListing ReadDirectory(string path, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var directory = new DirectoryInfo(path);
            // Cloud placeholders can be reparse points with no LinkTarget: keep those expandable.
            // Actual links stay openable, but never recurse through junctions or symlink cycles.
            if (directory.LinkTarget is not null)
                return new([], new Notice("Folder link (expand disabled)"));
            var entries = new List<DirectoryEntry>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false, IgnoreInaccessible = false,
                AttributesToSkip = 0, ReturnSpecialDirectories = false
            };
            foreach (var child in directory.EnumerateDirectories("*", options))
            {
                token.ThrowIfCancellationRequested();
                entries.Add(new(Normalize(child.FullName), child.Name));
            }
            entries.Sort((left, right) =>
            {
                int order = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
                return order != 0 ? order : StringComparer.Ordinal.Compare(left.Path, right.Path);
            });
            return new(entries);
        }
        catch (UnauthorizedAccessException) { return new([], new Notice("Access denied")); }
        catch (SecurityException) { return new([], new Notice("Access denied")); }
        catch (DirectoryNotFoundException) { return new([], new Notice("Folder no longer exists")); }
        catch (FileNotFoundException) { return new([], new Notice("Folder no longer exists")); }
        catch (IOException ex) { return new([], new Notice("Cannot read folders: {0}", ex.Message)); }
    }

    private async Task RefreshSelectedAsync()
    {
        try
        {
            var node = await OnUiAsync(() =>
            {
                if (Tree.SelectedNode is not { Tag: Folder folder } selected) return null;
                followCancellation?.Cancel();
                ++followGeneration;
                ++folder.Generation;
                folder.Loaded = folder.Unavailable = false;
                folder.Pending = null;
                selected.Nodes.Clear();
                selected.Nodes.Add(CreateNoticeNode(new Notice("Loading folders...")));
                return selected;
            }, lifetimeToken).ConfigureAwait(false);
            if (node is null) return;
            await EnsureLoadedAsync(node, lifetimeToken).ConfigureAwait(false);
            await OnUiAsync(() => { node.Expand(); return true; }, lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    }

    private async Task<T> OnUiAsync<T>(Func<T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lifetimeToken.ThrowIfCancellationRequested();
        try
        {
            return await InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                lifetimeToken.ThrowIfCancellationRequested();
                if (IsDisposed || Disposing) throw new OperationCanceledException(lifetimeToken);
                return action();
            }, token).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (lifetimeToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(lifetimeToken);
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool ContainsPath(string parent, string path) =>
        string.Equals(parent, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFileSystemError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!lifetimeToken.IsCancellationRequested) lifetime.Cancel();
            var follow = followCancellation;
            followCancellation = null;
            follow?.Cancel();
            Tree.ImageList = null;
            menu.Dispose();
            images.Dispose();
            folderIcon.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) lifetime.Dispose();
    }
}
