using System.Reflection;
using QuadExplorer;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static void RunAiSearchPaneScenario(string root)
    {
        string? expected = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_DESKTOP");
        var actual = new System.Text.StringBuilder(256);
        if (expected is null || !expected.StartsWith("FolderSplitter-QA-", StringComparison.Ordinal)
            || !GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, actual, 512, out _)
            || actual.ToString() != expected)
            throw new InvalidOperationException("AI pane checks require the private QA desktop.");

        string? output = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR");
        if (output is { Length: > 0 }) Directory.CreateDirectory(output);
        foreach (string mode in new[] { "light", "dark" })
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        foreach (float points in new[] { 9.5f, 18f })
        {
            Theme.Set(mode);
            var text = new UiText(language);
            using var font = new Font(Theme.UiFace, points);
            using var host = new Form { ClientSize = new Size(420, 640), Font = font };
            using var pane = new AiSearchPane(text, new AiSearchSettings()) { Dock = DockStyle.Fill };
            host.Controls.Add(pane);
            string name = $"AI {mode}/{language}/{points}";
            Exception? failure = null;
            host.Shown += async (_, _) =>
            {
                try
                {
                    Size AtDpi(int width, int height) => new(
                        (int)Math.Round(width * host.DeviceDpi / 96d),
                        (int)Math.Round(height * host.DeviceDpi / 96d));
                    Console.WriteLine($"AI_COMPOSER_DPI {host.DeviceDpi} {name}");
                    pane.ApplyColors();
                    int calls = 0, cancellations = 0, resets = 0;
                    pane.CancelRequested = () => cancellations++;
                    pane.ResetRequested = () => resets++;
                    Check(calls == 0 && pane.Turns.Count == 0 && !pane.Busy && !pane.SendButton.Enabled,
                        name + " starts empty without calling a provider");
                    Check(pane.CurrentSettings.Mode == AiSearchMode.FileNames && pane.ModePicker.SelectedIndex == 0
                        && pane.ModePicker.Items.Count == 2 && pane.ModePicker.DropDownStyle == ComboBoxStyle.DropDownList,
                        name + " explicitly defaults to filename-only mode");
                    if (pane.ExampleButton.Visible) pane.ExampleButton.PerformClick();
                    else pane.Composer.Text = text.T("Find proposal PDF files");
                    Check(pane.Composer.TextLength > 0 && calls == 0, name + " example fills draft without sending");
                    Check(Descendants(pane).OfType<Panel>().Count(panel => panel.AutoScroll) == 1,
                        name + " has one conversation scroll owner");
                    foreach (int width in new[] { 320, 420, 520, 960 })
                    {
                        host.ClientSize = AtDpi(width, 640);
                        host.PerformLayout();
                        if (output is { Length: > 0 })
                            CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-{width}-empty.png"));
                        CheckAiPaneGeometry(pane, name + "/" + width);
                    }
                    host.ClientSize = AtDpi(360, 420);
                    host.PerformLayout();
                    if (output is { Length: > 0 })
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-minimum.png"));
                    CheckAiPaneGeometry(pane, name + "/360x420");
                    host.ClientSize = AtDpi(520, 640);
                    AiSearchSettings? changed = null;
                    pane.SettingsChanged += value => changed = value;
                    var modelList = new TaskCompletionSource<ProviderProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.ListModels = (_, _) => modelList.Task;
                    Task loadingModels = pane.RefreshModelsAsync();
                    pane.SelectModel(AiProviderKind.LocalEndpoint, "gpt-oss:20b");
                    modelList.SetResult(new ProviderProbe(true, "", ["gpt-oss:20b", "local-model"], TimeSpan.Zero));
                    await loadingModels.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(pane.CurrentSettings.Model == "gpt-oss:20b"
                        && pane.DiscoveredModels.Any(model => model.Id == "local-model"),
                        name + " model discovery lists provider models without replacing the user's selection");
                    Check(changed?.Model == "gpt-oss:20b" && pane.ReasoningChoices.Count > 1,
                        name + " inline model selection updates the active provider settings");
                    pane.SelectEffort("high");
                    Check(changed?.ReasoningEffort == "high", name + " inline reasoning selection updates settings");
                    pane.SelectModel(AiProviderKind.LocalEndpoint, "local-model");
                    Check(changed?.Model == "local-model" && changed.ReasoningEffort == "default" && pane.ReasoningChoices.SequenceEqual(["default"]),
                        name + " models without reasoning support reset the override and disable the picker");
                    var reply = new TaskCompletionSource<ChatTurn?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    EventHandler settled = (_, _) => { if (!pane.Busy) completed.TrySetResult(); };
                    pane.StateChanged += settled;
                    pane.Submit = message => { calls++; return reply.Task; };
                    var delayedCatalog = new TaskCompletionSource<ProviderProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var catalogCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.ListModels = (_, token) =>
                    {
                        token.Register(() => catalogCancelled.TrySetResult());
                        return delayedCatalog.Task;
                    };
                    Task staleCatalog = pane.RefreshModelsAsync();
                    pane.SendButton.PerformClick();
                    Task pending = pane.PendingSubmission;
                    await catalogCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    delayedCatalog.SetResult(new ProviderProbe(true, "", ["stale-model"], TimeSpan.Zero));
                    await staleCatalog.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(pane.Busy && pane.ProgressIndicatorRunning && calls == 1
                        && pane.Turns.Count == 1 && pane.SendButton.Enabled,
                        name + " send appends user turn and displays active progress");
                    if (output is { Length: > 0 } && language == "ko" && points == 9.5f)
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-working.png"));
                    Check(!pane.DiscoveredModels.Any(model => model.Id == "stale-model"),
                        name + " cancelled late model catalog cannot overwrite the active request");
                    pane.Composer.Text = "retained draft";
                    pane.Visible = false;
                    pane.Visible = true;
                    pane.SetFloating(true);
                    pane.SetFloating(false);
                    Check(pane.Composer.Text == "retained draft" && pane.Turns.Count == 1,
                        name + " hide and docking preserve draft and conversation");
                    pane.SendButton.PerformClick();
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    pane.StateChanged -= settled;
                    Check(!pane.Busy && !pane.ProgressIndicatorRunning && cancellations == 1,
                        name + " stop delegates cancellation and stops animation");
                    reply.SetResult(new ChatTurn(ChatRole.Assistant, "late cancelled reply"));
                    await pending.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(pane.Turns.Count == 1, name + " cancelled stale reply cannot enter transcript");
                    var escapeReply = new TaskCompletionSource<ChatTurn?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.Submit = message => escapeReply.Task;
                    pane.Composer.Text = "escape cancellation";
                    Task escapePending = pane.SendAsync();
                    InvokeAiComposerKey(pane.Composer, Keys.Escape);
                    escapeReply.SetResult(null);
                    await escapePending.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(!pane.Busy && !pane.ProgressIndicatorRunning && cancellations == 2,
                        name + " Escape cancels from composer and clears progress");

                    SearchPlan plan = new([], [], [], ["pdf"], ItemKind.File, SearchScope.Everywhere,
                        null, null, null, null, false, null);
                    const string question = "Which file name or extension?";
                    int assistantRows = Descendants(pane).OfType<RichTextBox>().Count(label => label.AccessibleName == text.T("AI assistant"));
                    pane.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, question,
                        new AiSearchResult(plan with { Clarification = question }, [], 0, false, question),
                        ContextUsage: new AiContextUsage(42, null, "local-model")
                        { Provider = AiProviderKind.LocalEndpoint }));
                    pane.Composer.Text = "ambiguous request";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(Descendants(pane).OfType<RichTextBox>().Count(label => label.AccessibleName == text.T("AI assistant")) == assistantRows + 1,
                        name + " clarification is one concise answer without duplicate notes or a false no-results message");
                    Check(pane.HintText == text.T("AI chat is kept only while this window is open."),
                        name + " clarification does not report an unsuccessful file search in the footer");
                    Check(pane.ContextMeter.Visible && pane.ContextMeter.ForeColor == Theme.Muted
                        && !pane.ContextMeter.Text.Contains('%'),
                        name + " unknown context capacity shows measured input without a fabricated percentage");
                    if (output is { Length: > 0 })
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-clarification.png"));
                    string firstFolder = Path.Combine(root, "result-folder-alpha");
                    string secondFolder = Path.Combine(root, "result-folder-beta");
                    Directory.CreateDirectory(firstFolder);
                    Directory.CreateDirectory(secondFolder);
                    const string longName = "proposal-with-a-very-long-and-similar-filename-for-testing-selection-and-accessibility.pdf";
                    string[] paths =
                    [
                        Path.Combine(firstFolder, longName),
                        Path.Combine(secondFolder, longName),
                        Path.Combine(secondFolder, "proposal-with-a-very-long-and-similar-filename-for-testing-selection-and-accessibility-revised.pdf")
                    ];
                    foreach (string path in paths) File.WriteAllText(path, "actual result file");
                    AiHit[] hits = paths.Select(path => new AiHit(path, false, 42, null)).ToArray();
                    pane.Submit = message => Task.FromResult<ChatTurn?>(new ChatTurn(ChatRole.Assistant,
                        "A model-mentioned nonexistent.pdf is not a result.", new AiSearchResult(plan, hits, hits.Length, false, null),
                        ContextUsage: new AiContextUsage(231_200, 272_000, "local-model")
                        { Provider = AiProviderKind.LocalEndpoint }));
                    pane.Composer.Text = "proposal";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(pane.ContextMeter.Visible && pane.ContextMeter.ForeColor == Theme.ErrorText
                        && pane.ContextMeter.AccessibleDescription?.Length > 0,
                        name + " near-limit context shows a visible non-destructive warning");
                    ListView list = Descendants(pane).OfType<ListView>().Single();
                    Check(list.Items.Count == hits.Length && !list.Scrollable && list.ShowItemToolTips,
                        name + " renders only real result hits in a native list without nested scrolling");
                    foreach (AiHit expectedHit in hits)
                    {
                        ListViewItem item = list.Items.Cast<ListViewItem>().Single(value => Equals(value.Tag, expectedHit));
                        Check(File.Exists(expectedHit.FullPath) && item.Text == Path.GetFileName(expectedHit.FullPath)
                            && item.SubItems.Cast<ListViewItem.ListViewSubItem>().Any(part => part.Text == Path.GetDirectoryName(expectedHit.FullPath))
                            && item.SubItems.Cast<ListViewItem.ListViewSubItem>().Any(part => part.Text == Path.GetExtension(expectedHit.FullPath))
                            && item.ToolTipText == expectedHit.FullPath,
                            name + " each real result retains its exact filename, folder, hit and full-path tooltip");
                    }
                    Rectangle fileRow = list.Items[0].Bounds;
                    typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(list, [new MouseEventArgs(MouseButtons.None, 0, fileRow.Left + 2, fileRow.Top + 2, 0)]);
                    Check(list.Cursor == Cursors.Hand && list.AccessibleDescription?.Length > 0,
                        name + " file row advertises click selection with pointer and accessible hint");
                    if (output is { Length: > 0 })
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-hover.png"));
                    typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(list, [EventArgs.Empty]);
                    Check(list.Cursor == Cursors.Default,
                        name + " pointer affordance clears outside a file row");
                    list.Items[0].Selected = true;
                    foreach (string key in new[] { "Open", "Open in current pane", "Open in new tab", "Open in new pane", "Preview" })
                        Check(Descendants(pane).OfType<Button>().Single(value => value.AccessibleName == key).Enabled,
                            name + " selected result enables " + key);
                    foreach (AiHit expectedHit in hits)
                    {
                        ListViewItem selected = list.Items.Cast<ListViewItem>().Single(item => Equals(item.Tag, expectedHit));
                        foreach (ListViewItem item in list.Items) item.Selected = false;
                        selected.Selected = true;
                        Check(list.SelectedItems.Count == 1 && Equals(list.SelectedItems[0].Tag, expectedHit)
                            && list.SelectedItems[0].ToolTipText == expectedHit.FullPath,
                            name + " selection preserves the exact file and full path for similar long names");
                    }
                    string? resultCopy = null;
                    pane.CopyText = value => resultCopy = value;
                    Descendants(pane).OfType<Button>().Single(button => button.AccessibleName == text.T("Copy conversation")).PerformClick();
                    Check(resultCopy is not null && paths.All(path => resultCopy.Contains(path, StringComparison.Ordinal)),
                        name + " conversation copy retains every full result path, including duplicate filenames");
                    Check(Descendants(pane).OfType<FlowLayoutPanel>().SelectMany(actions => actions.Controls.Cast<Control>()
                        .Select(button => button.Left >= 0 && button.Right <= actions.Width && button.Bottom <= actions.Height)).All(fits => fits),
                        name + " all five result actions wrap inside the conversation width");
                    foreach (int width in new[] { 320, 520, 960 })
                    {
                        host.ClientSize = AtDpi(width, 640);
                        host.PerformLayout();
                        CheckAiResultGeometry(pane, name + "/results/" + width);
                        if (output is { Length: > 0 })
                            CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-{width}-results.png"));
                    }
                    if (output is { Length: > 0 })
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-results.png"));

                    var manyHits = new List<AiHit>();
                    for (int i = 0; i < 24; i++)
                    {
                        string path = Path.Combine(firstFolder, $"wheel-result-{i:D2}.pdf");
                        File.WriteAllText(path, "actual result file");
                        manyHits.Add(new AiHit(path, false, 42, null));
                    }
                    pane.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, "",
                        new AiSearchResult(plan, manyHits, manyHits.Count, false, null)));
                    pane.Composer.Text = "many results";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    ListView longList = Descendants(pane.Transcript).OfType<ListView>()
                        .Single(value => value.Items.Count == manyHits.Count);
                    int listTop = pane.Transcript.PointToClient(longList.PointToScreen(Point.Empty)).Y
                        - pane.Transcript.AutoScrollPosition.Y;
                    pane.Transcript.AutoScrollPosition = new Point(0, Math.Max(0, listTop));
                    int beforeWheel = pane.Transcript.AutoScrollPosition.Y;
                    Point wheelPoint = longList.PointToScreen(new Point(Math.Min(10, longList.ClientSize.Width - 1), 10));
                    if (SystemInformation.MouseWheelScrollLines != 0)
                    {
                        AiPaneSendMessage(longList.Handle, 0x20A, (nint)(-120 << 16),
                            (nint)((wheelPoint.X & 0xFFFF) | ((wheelPoint.Y & 0xFFFF) << 16)));
                        Check(!longList.Scrollable && pane.Transcript.AutoScrollPosition.Y < beforeWheel,
                            name + " wheel over overflowing file results scrolls the outer conversation, not a nested list");
                    }

                    pane.Submit = message => Task.FromResult<ChatTurn?>(new ChatTurn(ChatRole.Assistant, "", Error: "endpoint offline / model missing"));
                    pane.Composer.Text = "error";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(!pane.Busy && pane.Turns[^1].Error is not null, name + " provider errors settle without fabricated hits");
                    pane.Submit = message => Task.FromResult<ChatTurn?>(new ChatTurn(ChatRole.Assistant, "refinement answer"));
                    for (int i = 0; i < 6; i++)
                    {
                        pane.Composer.Text = "refine " + i;
                        await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    Check(Enumerable.Range(0, 6).All(i => Descendants(pane.Transcript).OfType<RichTextBox>()
                        .Any(body => body.Text == "refine " + i))
                        && Descendants(pane.Transcript).OfType<ListView>().Any(value => value.Items.Count == hits.Length),
                        name + " more than ten completed turns remain visible alongside earlier file results");
                    pane.Composer.Text = "keyboard";
                    int keyboardCalls = 0;
                    pane.Submit = message => { keyboardCalls++; return Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, "done")); };
                    InvokeAiComposerKey(pane.Composer, Keys.Shift | Keys.Enter);
                    Check(keyboardCalls == 0, name + " Shift+Enter does not submit or navigate");
                    InvokeAiComposerKey(pane.Composer, Keys.Enter);
                    await pane.PendingSubmission.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(keyboardCalls == 1, name + " Enter submits through composer without global input");
                    await CheckAiMessageHistory(pane, name, mode == "light" && language == "ko" && points == 9.5f);
                    string? switchedCopy = null;
                    void CheckCopied(params string[] expected)
                    {
                        pane.CopyText = value => switchedCopy = value;
                        switchedCopy = null;
                        Descendants(pane).OfType<Button>().Single(button => button.AccessibleName == text.T("Copy conversation")).PerformClick();
                        Check(switchedCopy is not null && expected.All(value => switchedCopy.Contains(value, StringComparison.Ordinal))
                            && !switchedCopy.Contains("stale previous mode", StringComparison.Ordinal),
                            name + " copy conversation preserves completed messages and full result paths, without cancelled replies");
                    }
                    CheckCopied("ambiguous request", question, "proposal", paths[0], paths[1], "refine 0", "refine 5");
                    var modeReply = new TaskCompletionSource<ChatTurn?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.Submit = _ => { calls++; return modeReply.Task; };
                    pane.Composer.Text = "pending before mode switch";
                    Task modePending = pane.SendAsync();
                    pane.Composer.Text = "draft survives mode switch";
                    int callsBeforeMode = calls;
                    pane.ModePicker.SelectedIndex = 1;
                    typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(pane.ModePicker, [EventArgs.Empty]);
                    Check(changed?.Mode == AiSearchMode.ReadDocuments && pane.CurrentSettings.Mode == AiSearchMode.ReadDocuments
                        && pane.ModePicker.SelectedIndex == 1 && !pane.Busy && cancellations == 3
                        && pane.Composer.Text == "draft survives mode switch" && calls == callsBeforeMode,
                        name + " document mode cancels old work without sending or losing the draft");
                    modeReply.SetResult(new ChatTurn(ChatRole.Assistant, "stale previous mode"));
                    await modePending.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(!Descendants(pane.Transcript).OfType<RichTextBox>().Any(body => body.Text.Contains("stale previous mode", StringComparison.Ordinal))
                        && Enumerable.Range(0, 6).All(i => Descendants(pane.Transcript).OfType<RichTextBox>()
                            .Any(body => body.Text == "refine " + i))
                        && Descendants(pane.Transcript).OfType<ListView>().Any(value => value.Items.Count == hits.Length),
                        name + " switching retains more than ten completed turns and results but rejects the late reply");
                    CheckCopied("ambiguous request", question, "proposal", paths[0], paths[1], "refine 0", "refine 5");
                    pane.Composer.SelectionStart = 0;
                    Check(InvokeAiComposerKey(pane.Composer, Keys.Up) && pane.Composer.Text == "pending before mode switch",
                        name + " sent-message recall survives the switch to document mode");
                    Check(InvokeAiComposerKey(pane.Composer, Keys.Down) && pane.Composer.Text == "draft survives mode switch",
                        name + " recall restores the unsent draft after switching modes");
                    await CheckAiSelectableTranscript(pane, host, text, root, name);
                    foreach (int width in new[] { 320, 420, 520, 960 })
                    {
                        host.ClientSize = AtDpi(width, 640);
                        host.PerformLayout();
                        if (output is { Length: > 0 })
                        {
                            CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-{width}-documents.png"));
                            CaptureAiConversation(host, pane, Path.Combine(output, $"ai-{mode}-{language}-{points}-{width}-conversation"));
                        }
                        CheckAiPaneGeometry(pane, name + "/documents/" + width);
                        CheckAiResultGeometry(pane, name + "/documents/" + width);
                    }
                    host.ClientSize = AtDpi(360, 420);
                    host.PerformLayout();
                    if (output is { Length: > 0 })
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-{language}-{points}-minimum-documents.png"));
                    CheckAiPaneGeometry(pane, name + "/documents/360x420");
                    var modeCatalog = new TaskCompletionSource<ProviderProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var modeCatalogCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.ListModels = (_, token) =>
                    {
                        token.Register(() => modeCatalogCancelled.TrySetResult());
                        return modeCatalog.Task;
                    };
                    Task previousModeCatalog = pane.RefreshModelsAsync();
                    pane.Composer.Text = "return draft survives mode switch";
                    pane.Composer.SelectionStart = 0;
                    pane.ModePicker.SelectedIndex = 0;
                    typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(pane.ModePicker, [EventArgs.Empty]);
                    await modeCatalogCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    modeCatalog.SetResult(new ProviderProbe(true, "", ["previous-mode-model"], TimeSpan.Zero));
                    await previousModeCatalog.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(changed?.Mode == AiSearchMode.FileNames && pane.CurrentSettings.Mode == AiSearchMode.FileNames
                        && calls == callsBeforeMode && pane.Composer.Text == "return draft survives mode switch"
                        && !pane.DiscoveredModels.Any(model => model.Id == "previous-mode-model"),
                        name + " return to filename mode cancels stale catalog without sending or losing draft");
                    string documentFile = Path.Combine(root, "chat-source.txt");
                    string secondDocumentFile = Path.Combine(root, "second-source-folder", "chat-source.txt");
                    Check(Enumerable.Range(0, 6).All(i => Descendants(pane.Transcript).OfType<RichTextBox>()
                            .Any(body => body.Text == "refine " + i))
                        && Descendants(pane.Transcript).OfType<RichTextBox>()
                            .Any(body => body.Text.Contains("The delivery date is October 30.", StringComparison.Ordinal))
                        && Descendants(pane.Transcript).OfType<ListView>()
                            .Any(value => value.Items.Cast<ListViewItem>().Any(item => item.Tag is AiHit source && source.FullPath == documentFile)),
                        name + " returning to filename mode visibly retains earlier turns, document answer and source");
                    CheckCopied("ambiguous request", question, "proposal", paths[0], paths[1], "refine 0", "refine 5",
                        "Summarize this proposal.", "staged delivery", documentFile, secondDocumentFile,
                        "The delivery date is October 30.", "A separate source confirms the delivery date.");
                    Check(InvokeAiComposerKey(pane.Composer, Keys.Up)
                        && pane.Composer.Text == "Summarize this proposal.\r\n\r\nKeep the delivery date.",
                        name + " document-mode recall survives return to filename mode");
                    pane.Composer.SelectionStart = pane.Composer.TextLength;
                    Check(InvokeAiComposerKey(pane.Composer, Keys.Down) && pane.Composer.Text == "return draft survives mode switch",
                        name + " return-mode recall restores the unsent draft");
                    foreach (ListViewItem item in list.Items) item.Selected = false;
                    list.Items[1].Selected = true;
                    var contentAction = Descendants(list.Parent!).OfType<Button>()
                        .Single(button => button.AccessibleName == "Search contents of this file");
                    int turnsBeforeTarget = pane.Turns.Count;
                    contentAction.PerformClick();
                    var targetChip = Descendants(pane).OfType<Button>()
                        .Single(button => button.AccessibleName == text.T("Clear selected file"));
                    Check(pane.CurrentSettings.Mode == AiSearchMode.ReadDocuments
                        && pane.Composer.Text == "return draft survives mode switch" && pane.Turns.Count == turnsBeforeTarget
                        && targetChip.Visible && targetChip.AccessibleDescription == paths[1],
                        name + " selecting a result stages the exact file for content search without sending or losing the draft");
                    CheckAiPaneGeometry(pane, name + "/selected-document");
                    if (output is { Length: > 0 } && language == "ko" && points == 9.5f)
                        CaptureAiControl(host, Path.Combine(output, $"ai-{mode}-selected-document.png"));
                    targetChip.PerformClick();
                    Check(!targetChip.Visible && pane.Turns.Count == turnsBeforeTarget,
                        name + " selected file can be cleared without sending");
                    contentAction.PerformClick();
                    Check(targetChip.Visible, name + " a file can be selected again");
                    pane.Composer.Text = "new chat preserves this draft";
                    pane.Composer.SelectionStart = 4;
                    InvokeAiComposerKey(pane.Composer, Keys.Up);
                    int resetsBeforeNewChat = resets;
                    pane.NewChatButton.PerformClick();
                    Check(resets == resetsBeforeNewChat + 1 && pane.Composer.Text == "new chat preserves this draft"
                        && pane.Composer.SelectionStart == 4 && !InvokeAiComposerKey(pane.Composer, Keys.Up)
                        && !Descendants(pane.Transcript).OfType<RichTextBox>().Any(body =>
                            body.Text == "refine 0" || body.Text.Contains("staged delivery", StringComparison.Ordinal))
                        && !Descendants(pane.Transcript).OfType<ListView>().Any()
                        && !targetChip.Visible
                        && !Descendants(pane).OfType<Button>().Any(button => button.AccessibleName == text.T("Copy conversation")),
                        name + " New chat explicitly clears visible conversation, results and recall while preserving the draft");
                    text.SetLanguage(language == "en" ? "ko" : "en");
                    pane.RefreshLanguage();
                    Check(pane.SendButton.Text == text.T("Send"), name + " live language refresh localizes controls");
                }
                catch (Exception error) { failure = error; }
                finally { host.Close(); }
            };
            Application.Run(host);
            if (failure is not null) throw new InvalidOperationException(name, failure);
        }
        RunAiSettingsChecks(output);
    }

    private static async Task CheckAiMessageHistory(AiSearchPane pane, string name, bool checkLimit)
    {
        int calls = 0;
        pane.Submit = _ => { calls++; return Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, "History check reply.")); };
        string first = "history first\r\nhistory last";
        foreach (string value in new[] { first, "history second", "history second" })
        {
            pane.Composer.Text = value;
            await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        string draft = "draft first\r\ndraft middle\r\ndraft last";
        pane.Composer.Text = string.Join(" ", Enumerable.Repeat("wrapped", 100));
        pane.Composer.SelectionStart = pane.Composer.TextLength / 2;
        Check(pane.Composer.GetLineFromCharIndex(pane.Composer.SelectionStart) > 0
            && !InvokeAiComposerKey(pane.Composer, Keys.Up) && !InvokeAiComposerKey(pane.Composer, Keys.Down),
            name + " wrapped logical lines keep native arrows until the visible text boundary");
        pane.Composer.Text = draft;
        pane.Composer.SelectionStart = draft.IndexOf("middle", StringComparison.Ordinal);
        Check(!InvokeAiComposerKey(pane.Composer, Keys.Up) && !InvokeAiComposerKey(pane.Composer, Keys.Down)
            && pane.Composer.Text == draft, name + " middle logical line keeps ordinary multiline arrow editing");
        pane.Composer.Select(0, 3);
        Check(!InvokeAiComposerKey(pane.Composer, Keys.Up) && pane.Composer.SelectionLength == 3,
            name + " selecting composer text does not recall history");
        pane.Composer.Select(3, 0);
        Check(!InvokeAiComposerKey(pane.Composer, Keys.Control | Keys.Up)
            && !InvokeAiComposerKey(pane.Composer, Keys.Shift | Keys.Down),
            name + " modified arrow shortcuts retain native editing");
        AiPaneSendMessage(pane.Composer.Handle, 0x10D, 0, 0);
        Check(!InvokeAiComposerKey(pane.Composer, Keys.Up) && pane.Composer.Text == draft,
            name + " active IME composition cannot trigger recall");
        InvokeAiComposerKey(pane.Composer, Keys.Enter);
        Check(calls == 3, name + " active IME Enter cannot submit");
        AiPaneSendMessage(pane.Composer.Handle, 0x10E, 0, 0);
        Check(InvokeAiComposerKey(pane.Composer, Keys.Up) && pane.Composer.Text == "history second",
            name + " first-line Up recalls the last sent message without sending");
        InvokeAiComposerKey(pane.Composer, Keys.Up);
        Check(pane.Composer.Text == first, name + " consecutive duplicate sends share one recall entry");
        Check(!InvokeAiComposerKey(pane.Composer, Keys.Down) && pane.Composer.Text == first,
            name + " recalled multiline message keeps Down inside its first line");
        pane.Composer.SelectionStart = pane.Composer.TextLength;
        Check(InvokeAiComposerKey(pane.Composer, Keys.Down) && pane.Composer.Text == "history second",
            name + " last-line Down moves toward the draft");
        InvokeAiComposerKey(pane.Composer, Keys.Down);
        Check(pane.Composer.Text == draft && pane.Composer.SelectionStart == 3 && calls == 3,
            name + " Down restores exact multiline draft and cursor without submitting");
        pane.Composer.SelectionStart = pane.Composer.TextLength;
        Check(!InvokeAiComposerKey(pane.Composer, Keys.Down) && pane.Composer.Text == draft,
            name + " Down cannot move beyond the unsent draft");
        if (!checkLimit) return;
        for (int i = 0; i < 52; i++)
        {
            pane.Composer.Text = "bounded " + i;
            await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        pane.Composer.Text = "bounded draft";
        pane.Composer.SelectionStart = 3;
        for (int i = 0; i < 50; i++) InvokeAiComposerKey(pane.Composer, Keys.Up);
        Check(pane.Composer.Text == "bounded 2", name + " recall retains only the latest 50 sent messages");
        InvokeAiComposerKey(pane.Composer, Keys.Up);
        Check(pane.Composer.Text == "bounded 2", name + " Up stays at the oldest retained entry");
        for (int i = 0; i < 50; i++) InvokeAiComposerKey(pane.Composer, Keys.Down);
        Check(pane.Composer.Text == "bounded draft" && pane.Composer.SelectionStart == 3 && calls == 55,
            name + " bounded history returns to draft without sending recalled messages");
    }

    private static async Task CheckAiSelectableTranscript(AiSearchPane pane, Form host, UiText text, string root, string name)
    {
        string file = Path.Combine(root, "chat-source.txt");
        const string quote = "The delivery date is October 30.";
        string secondFolder = Path.Combine(root, "second-source-folder");
        Directory.CreateDirectory(secondFolder);
        string secondFile = Path.Combine(secondFolder, "chat-source.txt");
        const string secondQuote = "A separate source confirms the delivery date.";
        File.WriteAllText(file, quote);
        File.WriteAllText(secondFile, secondQuote);
        const string answer = "• The proposal describes a staged delivery.\n• The delivery date is October 30. Keep this second point in the copied answer.";
        const string question = "Summarize this proposal.\r\n\r\nKeep the delivery date.";
        var documents = new DocumentSearchResult(answer,
            [new("S1", file, "line 1", quote), new("S2", secondFile, "line 2", secondQuote)],
            new(1, 2, 2, 2, 0, false, []));
        pane.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, answer, DocumentResult: documents));
        pane.Composer.Text = question;
        await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var bodies = Descendants(pane).OfType<RichTextBox>().ToArray();
        RichTextBox user = bodies.Single(body => body.AccessibleName == text.T("You")
            && body.Text == question.Replace("\r\n", "\n", StringComparison.Ordinal));
        RichTextBox assistant = bodies.Single(body => body.Text == answer);
        Check(user.Text == question.Replace("\r\n", "\n", StringComparison.Ordinal) && assistant.ReadOnly
            && user.ReadOnly && assistant.ShortcutsEnabled && user.ShortcutsEnabled
            && bodies.All(body => body.ScrollBars == RichTextBoxScrollBars.None && body.WordWrap && !body.HideSelection),
            name + " user and multiparagraph answers are native selectable readonly text without nested scrollbars");
        foreach (var body in new[] { user, assistant })
        {
            using var receipt = new AiCopyReceipt(body);
            body.Select(1, body.TextLength - 2);
            string selected = body.SelectedText;
            body.Copy();
            Check(receipt.Copies == 1 && receipt.Value == selected && selected.Contains('\n'),
                name + " native Copy dispatches the exact multiparagraph selection without touching the user clipboard");
            Check(InvokeAiComposerKey(body, Keys.Control | Keys.C) && receipt.Copies == 2 && receipt.Value == selected,
                name + " Ctrl+C copies the selected multiparagraph text without global keyboard input");
            body.ContextMenuStrip!.Items[0].PerformClick();
            Check(receipt.Copies == 3 && receipt.Value == selected, name + " selection context-menu copy uses native selection");
            Check(InvokeAiComposerKey(body, Keys.Control | Keys.A) && body.SelectedText == body.Text,
                name + " native selection spans the entire user or assistant message");
            body.Select(0, 0);
        }
        ListView sources = Descendants(pane.Transcript).OfType<ListView>().Single(list =>
            list.Items.Cast<ListViewItem>().Any(item => item.Tag is AiHit hit && hit.FullPath == file));
        foreach (string sourcePath in new[] { file, secondFile })
        {
            ListViewItem item = sources.Items.Cast<ListViewItem>().Single(value => value.Tag is AiHit hit && hit.FullPath == sourcePath);
            foreach (ListViewItem other in sources.Items) other.Selected = false;
            item.Selected = true;
            Check(item.Text == Path.GetFileName(sourcePath) && item.ToolTipText == sourcePath
                && sources.SelectedItems.Count == 1 && Equals(sources.SelectedItems[0].Tag, item.Tag),
                name + " source selection distinguishes identical filenames by full path");
        }
        foreach (string sourceQuote in new[] { quote, secondQuote })
        {
            RichTextBox citation = bodies.Single(body => body.AccessibleName == text.T("Document sources")
                && body.Text.Contains(sourceQuote, StringComparison.Ordinal));
            using var receipt = new AiCopyReceipt(citation);
            Check(citation.ReadOnly && citation.ShortcutsEnabled, name + " source evidence remains selectable readonly text");
            citation.Select(citation.Text.IndexOf(sourceQuote, StringComparison.Ordinal), sourceQuote.Length);
            Check(citation.SelectedText == sourceQuote && InvokeAiComposerKey(citation, Keys.Control | Keys.C)
                && receipt.Copies == 1 && receipt.Value == sourceQuote,
                name + " source citation selection copies the exact quoted evidence");
            citation.ContextMenuStrip!.Items[0].PerformClick();
            Check(receipt.Copies == 2 && receipt.Value == sourceQuote,
                name + " source citation context menu copies the same selected evidence");
            citation.Select(0, 0);
        }
        string? copied = null;
        pane.CopyText = value => copied = value;
        Descendants(pane).OfType<Button>().Single(button => button.AccessibleName == text.T("Copy conversation")).PerformClick();
        Check(copied is not null && copied.Contains(user.Text, StringComparison.Ordinal)
            && copied.Contains(answer, StringComparison.Ordinal) && copied.Contains(file, StringComparison.Ordinal)
            && copied.Contains(secondFile, StringComparison.Ordinal) && copied.Contains(quote, StringComparison.Ordinal)
            && copied.Contains(secondQuote, StringComparison.Ordinal),
            name + " full-conversation copy includes both roles, actual file paths and quoted evidence");
        CheckAiResultGeometry(pane, name + "/sources");
        host.PerformLayout();
    }

    private static void CheckAiResultGeometry(AiSearchPane pane, string name)
    {
        var transcriptControls = Descendants(pane.Transcript).ToArray();
        foreach (var list in transcriptControls.OfType<ListView>())
        {
            Control group = list.Parent!;
            Check(transcriptControls.Contains(group) && !list.Scrollable && list.ShowItemToolTips
                && list.AccessibleName?.Length > 0 && list.Left >= 0 && list.Top >= 0
                && list.Right <= group.ClientSize.Width && list.Bottom <= group.ClientSize.Height,
                name + " accessible native result list fits inside the conversation without its own scroll");
            foreach (ListViewItem item in list.Items)
            {
                Rectangle row = item.Bounds;
                Check(row.Top >= 0 && row.Bottom <= list.ClientSize.Height && row.Height >= list.Font.Height * 2
                    && row.Width > 0 && item.Tag is AiHit && item.ToolTipText.Length > 0,
                    name + " each file row fits and has space for filename and folder while retaining its full path");
            }
            foreach (Button button in Descendants(group).OfType<Button>())
                Check(button.Left >= 0 && button.Top >= 0 && button.Right <= button.Parent!.ClientSize.Width
                    && button.Bottom <= button.Parent.ClientSize.Height,
                    name + " result actions fit their container");
            foreach (Label label in Descendants(group).OfType<Label>().Where(label => label.Visible && label.Text.Length > 0))
                Check(label.Left >= 0 && label.Top >= 0 && label.Right <= label.Parent!.ClientSize.Width
                    && label.Bottom <= label.Parent.ClientSize.Height
                    && label.GetPreferredSize(new Size(label.ClientSize.Width, 0)).Height <= label.ClientSize.Height,
                    name + " visible result labels fit without vertical clipping");
            Rectangle listScreen = list.RectangleToScreen(list.ClientRectangle);
            Check(!transcriptControls.OfType<RichTextBox>()
                .Any(body => listScreen.IntersectsWith(body.RectangleToScreen(body.ClientRectangle))),
                name + " file results remain separate from selectable prose and citations");
        }
        foreach (var body in Descendants(pane).OfType<RichTextBox>())
        {
            Point end = body.GetPositionFromCharIndex(body.TextLength);
            Check(body.Left >= 0 && body.Right <= body.Parent!.Width && body.Bottom <= body.Parent.Height
                && end.Y >= 0 && end.Y + body.Font.Height <= body.ClientSize.Height,
                name + " selectable text fits its native wrapped content without vertical clipping");
        }
        Check(!pane.Transcript.HorizontalScroll.Visible
            && Descendants(pane).OfType<Panel>().Count(panel => panel.AutoScroll) == 1,
            name + " prose and result groups retain one conversation scroll owner");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint AiPaneSendMessage(nint window, int message, nint wParam, nint lParam);

    private sealed class AiCopyReceipt : NativeWindow, IDisposable
    {
        private readonly RichTextBox body;
        internal int Copies { get; private set; }
        internal string? Value { get; private set; }
        internal AiCopyReceipt(RichTextBox body)
        {
            this.body = body;
            AssignHandle(body.Handle);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x301) // WM_COPY: inspect the real native selection, never write the shared clipboard.
            {
                Copies++;
                Value = body.SelectedText;
                return;
            }
            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }

    private static void CheckAiPaneGeometry(AiSearchPane pane, string name)
    {
        Rectangle composer = pane.RectangleToClient(pane.Composer.RectangleToScreen(pane.Composer.ClientRectangle));
        Rectangle transcript = pane.RectangleToClient(pane.Transcript.RectangleToScreen(pane.Transcript.ClientRectangle));
        Check(composer.Width >= 80 && composer.Height >= pane.Font.Height * 2 && composer.Left >= 0
            && composer.Right <= pane.ClientSize.Width && composer.Bottom <= pane.ClientSize.Height,
            name + " multiline composer fits without clipping " + composer);
        Check(transcript.Height >= pane.Font.Height * 2 && transcript.Bottom <= composer.Top
            && !pane.Transcript.HorizontalScroll.Visible,
            name + " pinned help and composer leave usable transcript " + transcript
            + $" sheet={pane.Transcript.Controls[0].Bounds} display={pane.Transcript.DisplayRectangle} range={pane.Transcript.AutoScrollMinSize}");
        var buttons = new[] { pane.NewChatButton, pane.SettingsButton, pane.DetachButton };
        Check(buttons.All(button => button.Right <= button.Parent!.ClientSize.Width && button.Left >= 0)
            && !pane.Composer.Bounds.IntersectsWith(pane.SendButton.Bounds),
            name + " compact header and send button do not overlap");
        Control[] toolbar = [pane.ModePicker, pane.ModelButton, pane.SendButton];
        Check(toolbar.All(control => control.Parent == pane.Composer.Parent && control.Left >= 0
            && control.Right <= control.Parent!.ClientSize.Width && control.Top >= pane.Composer.Bottom
            && control.Bottom <= control.Parent.ClientSize.Height)
            && toolbar.SelectMany((control, index) => toolbar.Skip(index + 1)
                .Select(other => !control.Bounds.IntersectsWith(other.Bounds))).All(fits => fits),
            name + " actual mode model effort and send controls fit below broad input without overlap");
        Check(pane.Composer.Width >= pane.Composer.Parent!.ClientSize.Width - pane.Composer.Left * 2
            && pane.Composer.TabIndex < pane.ModePicker.TabIndex && pane.ModePicker.TabIndex < pane.ModelButton.TabIndex
            && pane.ModelButton.TabIndex < pane.SendButton.TabIndex,
            name + " broad input and toolbar preserve reading-order keyboard navigation");
    }

    private static void RunAiSettingsChecks(string? output)
    {
        foreach (string mode in new[] { "light", "dark" })
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            Theme.Set(mode);
            var text = new UiText(language);
            using var font = new Font(Theme.UiFace, 18f);
            using var dialog = new AiSearchSettingsDialog(text, new AiSearchSettings { Model = "local-model" }) { Font = font };
            Exception? failure = null;
            dialog.Shown += async (_, _) =>
            {
                try
                {
                    int probes = 0;
                    AiProviderKind? probedProvider = null;
                    dialog.Probe = (settings, token) =>
                    {
                        probes++;
                        probedProvider = settings.Provider;
                        return Task.FromResult(new ProviderProbe(false, "offline", [], TimeSpan.Zero));
                    };
                    Check(probes == 0 && dialog.ProviderBox.SelectedIndex == 0 && dialog.ProviderBox.Items.Count == 3,
                        $"AI settings {mode}/{language} starts local without cloud or fake providers");
                    Check(dialog.ModelBox.Visible, "local endpoint keeps its manual model ID field");
                    const string customPrompt = "한국어와 영어 이름을 함께 고려해\n최신 문서를 우선해";
                    dialog.FilenamePromptBox.Text = customPrompt;
                    Check(dialog.FilenamePromptBox.Multiline && dialog.FilenamePromptBox.MaxLength == 2000
                        && dialog.FilenamePromptBox.AccessibleName?.Length > 0,
                        "filename prompt input is a bounded accessible multiline field");
                    dialog.EndpointBox.Text = "not-a-url";
                    dialog.SaveButton.PerformClick();
                    Check(dialog.DialogResult != DialogResult.OK && dialog.StatusText.Length > 0, "invalid endpoint cannot save");
                    dialog.EndpointBox.Text = "http://127.0.0.1:11434/v1";
                    dialog.ProviderBox.SelectedIndex = (int)AiProviderKind.ClaudeCli;
                    Check(!dialog.ModelBox.Visible, "cloud model choice stays in the conversation selector");
                    dialog.TestButton.PerformClick();
                    await dialog.PendingProbe.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(probes == 1 && probedProvider == AiProviderKind.ClaudeCli,
                        "test probes only explicitly selected provider without fallback");
                    dialog.ProviderBox.SelectedIndex = (int)AiProviderKind.LocalEndpoint;
                    Check(dialog.ModelBox.Visible, "local model ID returns with local connection settings");
                    Check(dialog.FilenamePromptBox.Text.Replace("\r\n", "\n", StringComparison.Ordinal) == customPrompt,
                        "provider switching preserves the user's filename preferences");
                    dialog.Size = new Size(360, 420);
                    dialog.PerformLayout();
                    Check(dialog.SaveButton.Visible && dialog.SaveButton.Parent!.Bottom <= dialog.ClientSize.Height,
                        "large-text narrow settings keep save reachable while fields scroll");
                    if (output is { Length: > 0 } && language == "ko")
                    {
                        CaptureAiControl(dialog, Path.Combine(output, $"ai-settings-{mode}.png"));
                        dialog.FilenamePromptBox.Focus();
                        CaptureAiControl(dialog, Path.Combine(output, $"ai-settings-{mode}-prompt.png"));
                    }
                    var settingsScroll = dialog.Controls.OfType<Panel>().Single(panel => panel.AutoScroll);
                    settingsScroll.AutoScrollPosition = new Point(0, dialog.ReadableFilesInfo.Top);
                    var visibleArea = settingsScroll.RectangleToScreen(settingsScroll.ClientRectangle);
                    var formatsArea = dialog.ReadableFilesInfo.RectangleToScreen(dialog.ReadableFilesInfo.ClientRectangle);
                    if (output is { Length: > 0 })
                        CaptureAiControl(dialog, Path.Combine(output, $"ai-settings-{mode}-{language}-formats.png"));
                    Check(formatsArea.Top >= visibleArea.Top && formatsArea.Bottom <= visibleArea.Bottom,
                        $"all readable file types fit in the scrolled settings view {formatsArea} inside {visibleArea}");
                    dialog.SaveButton.PerformClick();
                    Check(dialog.DialogResult == DialogResult.OK && dialog.Settings.Provider == AiProviderKind.LocalEndpoint
                        && dialog.Settings.Model == "local-model"
                        && AiSearchSettingsStore.Normalize(dialog.Settings).FilenamePrompt == customPrompt && probes == 1,
                        "filename preferences and syntax-valid settings save offline without probing");
                }
                catch (Exception error) { failure = error; }
                finally { dialog.Close(); }
            };
            Application.Run(dialog);
            if (failure is not null) throw new InvalidOperationException("AI settings", failure);
        }
        using var legacy = new AiSearchSettingsDialog(new UiText("en"), new AiSearchSettings
        {
            Provider = (AiProviderKind)99, Model = "legacy-model"
        });
        legacy.Shown += (_, _) =>
        {
            try
            {
                int probes = 0;
                legacy.Probe = (_, _) =>
                {
                    probes++;
                    return Task.FromResult(new ProviderProbe(false, "unsupported", [], TimeSpan.Zero));
                };
                Check(legacy.ProviderBox.SelectedIndex == -1 && legacy.ProviderBox.Items.Count == 3,
                    "unknown provider configuration has no selectable unsupported provider and no silent fallback");
                legacy.TestButton.PerformClick();
                legacy.SaveButton.PerformClick();
                Check(probes == 0 && legacy.DialogResult != DialogResult.OK && legacy.StatusText.Length > 0,
                    "unsupported legacy provider cannot probe or save until supported provider is explicitly chosen");
                legacy.ProviderBox.SelectedIndex = (int)AiProviderKind.ClaudeCli;
                legacy.SaveButton.PerformClick();
                Check(legacy.DialogResult == DialogResult.OK && legacy.Settings.Provider == AiProviderKind.ClaudeCli,
                    "legacy provider can be replaced only by explicit supported selection");
            }
            finally { legacy.Close(); }
        };
        Application.Run(legacy);
    }

    private static bool InvokeAiComposerKey(TextBoxBase composer, Keys keys)
    {
        Message message = Message.Create(composer.Handle, 0x100, (nint)(keys & Keys.KeyCode), 0);
        return (bool)typeof(Control).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, [message, keys])!;
    }

    private static void CaptureAiControl(Control control, string path)
    {
        control.Refresh();
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        image.Save(path);
    }

    private static void CaptureAiConversation(Form host, AiSearchPane pane, string path)
    {
        int step = Math.Max(pane.Font.Height, pane.Transcript.ClientSize.Height - pane.Font.Height);
        Point position = pane.Transcript.AutoScrollPosition;
        int extent = pane.Transcript.Controls[0].Height;
        for (int top = 0, page = 0; top < extent; top += step, page++)
        {
            pane.Transcript.AutoScrollPosition = new Point(0, top);
            CaptureAiControl(host, $"{path}-{page}.png");
        }
        pane.Transcript.AutoScrollPosition = new Point(0, -position.Y);
    }
}
