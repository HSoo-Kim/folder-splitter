using QuadExplorer.AiSearch;
using QuadExplorer.Search;

namespace QuadExplorer;

public sealed partial class MainForm
{
    private readonly ToolStripButton aiSearchButton = new() { AccessibleName = "AI search", CheckOnClick = true };
    private readonly AiSearchPane aiSearchPane;
    private readonly Splitter aiSearchSplitter;
    private readonly string aiSearchSettingsPath;
    private readonly AiSearchSession? aiSearchSession;
    private AiSearchSettings aiSearchSettings;
    private PreviewWindow? aiSearchWindow;
    private string? aiSearchSettingsWarning;
    private bool restoringAiSearch = true;

    internal bool AiSearchVisible => aiSearchButton.Checked;
    internal AiSearchPane AiSearch => aiSearchPane;
    internal PreviewWindow? FloatingAiSearch => aiSearchWindow;

    private void InitializeAiSearch(Panel workspace)
    {
        // Independent right sidecars: search hits can drive the existing preview.
        workspace.Controls.Add(aiSearchSplitter);
        workspace.Controls.Add(aiSearchPane);
        aiSearchPane.Submit = query => aiSearchSession is null
            ? Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, text.T("AI search index is unavailable.")))
            : aiSearchSession.SubmitAsync(query, AiSearchRoots());
        aiSearchPane.CancelRequested = () => aiSearchSession?.Cancel();
        aiSearchPane.ResetRequested = () => aiSearchSession?.Reset();
        aiSearchPane.Probe = AiSearchSession.ProbeAsync;
        aiSearchPane.ListModels = AiSearchSession.ListModelsAsync;
        aiSearchPane.DocumentTargetChanged += path =>
            aiSearchSession?.SetDocumentTargets(path is null ? [] : [path]);
        if (aiSearchSession is not null)
            aiSearchSession.DocumentProgress += (_, progress) =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() => { if (!aiSearchPane.IsDisposed) aiSearchPane.SetDocumentProgress(progress); });
            };
        aiSearchPane.OpenRequested += (hit, action) =>
        {
            if (!(hit.IsDirectory ? Directory.Exists(hit.FullPath) : File.Exists(hit.FullPath)))
            { Active.SetLocalizedStatus("No longer available: {0}", hit.FullPath); return; }
            switch (action)
            {
                case AiHitAction.Open:
                    if (hit.IsDirectory) Active.Navigate(hit.FullPath);
                    else
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hit.FullPath) { UseShellExecute = true }); }
                        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                        { Active.SetStatus(error.Message); }
                    }
                    break;
                case AiHitAction.NewPane:
                    OpenTreeFolder(hit.IsDirectory ? hit.FullPath : Path.GetDirectoryName(hit.FullPath)!, FolderTreeTarget.NewPane);
                    break;
                case AiHitAction.NewTab:
                    Active.OpenSearchHit(new SearchHit(hit.FullPath, hit.IsDirectory), true);
                    break;
                case AiHitAction.Preview:
                    SetPreviewVisible(true);
                    if (hit.IsDirectory) previewPane.ShowMessage("Folders are not previewed.");
                    else previewPane.ShowFile(hit.FullPath);
                    break;
                default:
                    Active.OpenSearchHit(new SearchHit(hit.FullPath, hit.IsDirectory));
                    UpdatePreview();
                    break;
            }
        };
        aiSearchPane.SettingsChanged += settings =>
        {
            aiSearchSettings = settings with
            {
                Visible = AiSearchVisible, Floating = aiSearchWindow is not null,
                Width = aiSearchWindow is null ? aiSearchPane.Width : aiSearchSettings.Width
            };
            if (aiSearchSession is not null) aiSearchSession.Settings = aiSearchSettings;
            SaveAiSearchSettings();
        };
        aiSearchPane.DetachRequested += (_, _) => SetAiSearchFloating(true);
        aiSearchPane.DragRequested += point =>
        {
            SetAiSearchFloating(true);
            aiSearchWindow?.BeginMove(point);
        };
        aiSearchSplitter.SplitterMoved += (_, _) => SaveAiSearchSettings();
        workspace.SizeChanged += (_, _) => FitAiSearchWidth();
        languageBindings.Add(aiSearchPane.RefreshLanguage);
        languageBindings.Add(() => aiSearchWindow?.RefreshLanguage());
        VisibleChanged += (_, _) =>
        {
            if (aiSearchWindow is null) return;
            if (Visible && AiSearchVisible) aiSearchWindow.Show(this);
            else aiSearchWindow.Hide();
        };
        Shown += (_, _) =>
        {
            try
            {
                if (aiSearchSettings.Floating) SetAiSearchFloating(true);
                SetAiSearchVisible(aiSearchSettings.Visible);
            }
            finally { restoringAiSearch = false; }
            if (aiSearchSettingsWarning is { } warning)
                Active.SetLocalizedStatus("AI settings could not be loaded: {0}", warning);
        };
        aiSearchButton.Checked = aiSearchSettings.Visible;
        aiSearchPane.Visible = aiSearchSplitter.Visible = aiSearchSettings.Visible;
    }

    private IReadOnlyList<string> AiSearchRoots() => new[] { Active.CurrentPath }
        .Concat(visiblePanes.Select(i => panes[i].CurrentPath))
        .Where(Path.IsPathRooted).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private bool IsAiSearchInput(nint handle) =>
        aiSearchPane.IsHandleCreated && (handle == aiSearchPane.Handle || ShellNative.IsChild(aiSearchPane.Handle, handle))
        || aiSearchWindow is { IsHandleCreated: true } window
            && (handle == window.Handle || ShellNative.IsChild(window.Handle, handle));

    internal void SetAiSearchVisible(bool visible)
    {
        aiSearchButton.Checked = visible;
        aiSearchPane.Visible = visible;
        aiSearchSplitter.Visible = visible && aiSearchWindow is null;
        if (aiSearchWindow is not null)
        {
            if (visible && Visible) aiSearchWindow.Show(this);
            else aiSearchWindow.Hide();
        }
        if (visible)
        {
            FitAiSearchWidth();
            aiSearchPane.FocusComposer();
        }
        else aiSearchSession?.Cancel();
        SaveAiSearchSettings();
    }

    internal void SetAiSearchFloating(bool floating)
    {
        if (floating == (aiSearchWindow is not null)) return;
        aiSearchPane.SetFloating(floating);
        if (floating)
        {
            aiSearchSettings = aiSearchSettings with { Width = aiSearchPane.Width };
            aiSearchSplitter.Visible = false;
            int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            var window = new PreviewWindow(text, "AI search", "Dock AI search")
            {
                Icon = Icon, Size = new Size(Math.Max(Scale(400), aiSearchPane.Width), Math.Max(Scale(480), aiSearchPane.Height)),
                Location = aiSearchPane.PointToScreen(Point.Empty), MinimumSize = new Size(Scale(360), Scale(420))
            };
            var area = Screen.FromControl(this).WorkingArea;
            window.Size = new Size(Math.Min(window.Width, area.Width), Math.Min(window.Height, area.Height));
            window.Location = new Point(Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.Width)),
                Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height)));
            aiSearchWindow = window;
            window.DockRequested += (_, _) => SetAiSearchFloating(false);
            window.MoveFinished += point =>
            {
                var workspace = paneHost.Parent!;
                var bounds = workspace.RectangleToScreen(workspace.ClientRectangle);
                if (new Rectangle(bounds.Right - Math.Min(420, bounds.Width / 3), bounds.Top,
                    Math.Min(420, bounds.Width / 3), bounds.Height).Contains(point)) SetAiSearchFloating(false);
            };
            window.FormClosing += (_, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                SetAiSearchVisible(false);
            };
            window.AddContent(aiSearchPane);
            RefreshAiSearchTheme();
            if (Visible && AiSearchVisible) window.Show(this);
        }
        else
        {
            var window = aiSearchWindow!;
            aiSearchWindow = null;
            var workspace = paneHost.Parent!;
            workspace.Controls.Add(aiSearchPane);
            aiSearchPane.Dock = DockStyle.Right;
            aiSearchPane.Width = aiSearchSettings.Width;
            workspace.Controls.SetChildIndex(aiSearchPane, workspace.Controls.GetChildIndex(aiSearchSplitter) + 1);
            aiSearchPane.Visible = AiSearchVisible;
            aiSearchSplitter.Visible = AiSearchVisible;
            window.Dispose();
            FitAiSearchWidth();
        }
        SaveAiSearchSettings();
    }

    private void FitAiSearchWidth()
    {
        if (aiSearchWindow is not null || !AiSearchVisible) return;
        int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        // At narrow widths, make the selected tool usable instead of covering the file view.
        if (showPreview && previewWindow is null
            && ClientSize.Width - (sidebar.Visible ? sidebar.Width : 0) - previewPane.Width < Scale(600))
            SetPreviewVisible(false);
        int available = Math.Max(Scale(320), ClientSize.Width - (sidebar.Visible ? sidebar.Width : 0)
            - (showPreview && previewWindow is null ? previewPane.Width : 0) - Scale(280));
        aiSearchPane.Width = Math.Clamp(aiSearchSettings.Width, Scale(320), Math.Min(Scale(640), available));
    }

    private void RefreshAiSearchTheme()
    {
        aiSearchSplitter.BackColor = Theme.Border;
        if (aiSearchWindow is not null) aiSearchWindow.RefreshTheme();
        aiSearchPane.ApplyColors();
    }

    private void SaveAiSearchSettings()
    {
        if (restoringAiSearch) return;
        aiSearchSettings = aiSearchSettings with
        {
            Visible = AiSearchVisible, Floating = aiSearchWindow is not null,
            Width = aiSearchWindow is null ? aiSearchPane.Width : aiSearchSettings.Width
        };
        try { AiSearchSettingsStore.Save(aiSearchSettingsPath, aiSearchSettings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Active.SetLocalizedStatus("AI settings could not be saved: {0}", error.Message);
        }
    }
}
