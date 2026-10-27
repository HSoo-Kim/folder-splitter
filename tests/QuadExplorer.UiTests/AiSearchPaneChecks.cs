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
                    int modelLookups = 0;
                    AiSearchSettings? changed = null;
                    pane.SettingsChanged += value => changed = value;
                    var modelList = new TaskCompletionSource<ProviderProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.ListModels = (_, _) => { modelLookups++; return modelList.Task; };
                    Task loadingModels = pane.RefreshModelsAsync();
                    pane.SelectModel(AiProviderKind.LocalEndpoint, "gpt-oss:20b");
                    modelList.SetResult(new ProviderProbe(true, "", ["gpt-oss:20b", "local-model"], TimeSpan.Zero));
                    await loadingModels.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(modelLookups == 1 && pane.CurrentSettings.Model == "gpt-oss:20b"
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
                    var hit = new AiHit(Path.Combine(root, "proposal.pdf"), false, 42, null);
                    pane.Submit = message => Task.FromResult<ChatTurn?>(new ChatTurn(ChatRole.Assistant,
                        "A model-mentioned nonexistent.pdf is not a result.", new AiSearchResult(plan, [hit], 1, false, null),
                        ContextUsage: new AiContextUsage(231_200, 272_000, "local-model")
                        { Provider = AiProviderKind.LocalEndpoint }));
                    pane.Composer.Text = "proposal";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(pane.ContextMeter.Visible && pane.ContextMeter.ForeColor == Theme.ErrorText
                        && pane.ContextMeter.AccessibleDescription?.Length > 0,
                        name + " near-limit context shows a visible non-destructive warning");
                    ListView list = Descendants(pane).OfType<ListView>().Single();
                    Check(list.Items.Count == 1 && Equals(list.Items[0].Tag, hit) && list.Columns.Count == 3 && !list.Scrollable,
                        name + " renders only real result hits with name folder type and no nested scroll");
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
                    var opened = new TaskCompletionSource<(AiHit Hit, AiHitAction Action)>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pane.OpenRequested += (item, action) => opened.TrySetResult((item, action));
                    list.Items[0].Selected = true;
                    typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(list, [new KeyEventArgs(Keys.Control | Keys.Enter)]);
                    var open = await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(open.Hit == hit && open.Action == AiHitAction.NewTab, name + " Ctrl+Enter opens actual hit in a new tab");
                    foreach (var (key, action) in new[]
                    {
                        ("Open", AiHitAction.Open), ("Open in current pane", AiHitAction.ShowInPane),
                        ("Open in new tab", AiHitAction.NewTab), ("Open in new pane", AiHitAction.NewPane),
                        ("Preview", AiHitAction.Preview)
                    })
                    {
                        AiHitAction? invoked = null;
                        void Observe(AiHit item, AiHitAction value) { if (item == hit) invoked = value; }
                        pane.OpenRequested += Observe;
                        try
                        {
                            var button = Descendants(pane).OfType<Button>().Single(value => value.AccessibleName == key);
                            Check(button.Enabled, name + " selected result enables " + key);
                            button.PerformClick();
                            Check(invoked == action, name + " result action dispatches " + key);
                        }
                        finally { pane.OpenRequested -= Observe; }
                    }
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

                    pane.Submit = message => Task.FromResult<ChatTurn?>(new ChatTurn(ChatRole.Assistant, "", Error: "endpoint offline / model missing"));
                    pane.Composer.Text = "error";
                    await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(!pane.Busy && pane.Turns[^1].Error is not null, name + " provider errors settle without fabricated hits");
                    pane.Submit = message => Task.FromResult<ChatTurn?>(new ChatTurn(ChatRole.Assistant, "",
                        new AiSearchResult(plan, [], 0, false, null)));
                    for (int i = 0; i < 6; i++)
                    {
                        pane.Composer.Text = "refine " + i;
                        await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    Check(pane.Turns.Count == 10 && Descendants(pane).OfType<ListView>().Count() == 0,
                        name + " bounds history at ten turns and handles no-results without fake rows");
                    pane.Composer.Text = "keyboard";
                    int keyboardCalls = 0;
                    pane.Submit = message => { keyboardCalls++; return Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, "done")); };
                    InvokeAiComposerKey(pane.Composer, Keys.Shift | Keys.Enter);
                    Check(keyboardCalls == 0, name + " Shift+Enter does not submit or navigate");
                    InvokeAiComposerKey(pane.Composer, Keys.Enter);
                    await pane.PendingSubmission.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(keyboardCalls == 1, name + " Enter submits through composer without global input");
                    await CheckAiMessageHistory(pane, name, mode == "light" && language == "ko" && points == 9.5f);
                    pane.Composer.Text = "new chat preserves this draft";
                    pane.Composer.SelectionStart = 4;
                    InvokeAiComposerKey(pane.Composer, Keys.Up);
                    pane.NewChatButton.PerformClick();
                    Check(pane.Turns.Count == 0 && resets == 1 && pane.Composer.Text == "new chat preserves this draft"
                        && pane.Composer.SelectionStart == 4 && !InvokeAiComposerKey(pane.Composer, Keys.Up),
                        name + " new chat clears conversation and recall, restoring the unsent draft and cursor");
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
                        && pane.ModePicker.SelectedIndex == 1 && !pane.Busy && cancellations == 3 && resets == 2
                        && pane.Turns.Count == 0 && pane.Composer.Text == "draft survives mode switch" && calls == callsBeforeMode,
                        name + " explicit document mode persists and cancels old work without sending or losing draft");
                    modeReply.SetResult(new ChatTurn(ChatRole.Assistant, "stale previous mode"));
                    await modePending.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(pane.Turns.Count == 0, name + " stale previous-mode response cannot enter new conversation");
                    Check(!InvokeAiComposerKey(pane.Composer, Keys.Up) && pane.Composer.Text == "draft survives mode switch",
                        name + " mode switch clears sent recall without losing the current draft");
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
                    pane.ModePicker.SelectedIndex = 0;
                    typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(pane.ModePicker, [EventArgs.Empty]);
                    await modeCatalogCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    modeCatalog.SetResult(new ProviderProbe(true, "", ["previous-mode-model"], TimeSpan.Zero));
                    await previousModeCatalog.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(changed?.Mode == AiSearchMode.FileNames && resets == 3 && calls == callsBeforeMode
                        && !pane.DiscoveredModels.Any(model => model.Id == "previous-mode-model"),
                        name + " return to filename mode cancels stale catalog and never reads or sends automatically");
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
        File.WriteAllText(file, quote);
        const string answer = "• The proposal describes a staged delivery.\n• The delivery date is October 30. Keep this second point in the copied answer.";
        const string question = "Summarize this proposal.\r\n\r\nKeep the delivery date.";
        var documents = new DocumentSearchResult(answer, [new("S1", file, "line 1", quote)], new(1, 1, 1, 1, 0, false, []));
        pane.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, answer, DocumentResult: documents));
        pane.Composer.Text = question;
        await pane.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var bodies = Descendants(pane).OfType<RichTextBox>().ToArray();
        RichTextBox user = bodies.Single(body => body.AccessibleName == text.T("You"));
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
        string? copied = null;
        pane.CopyText = value => copied = value;
        Descendants(pane).OfType<Button>().Single(button => button.AccessibleName == text.T("Copy conversation")).PerformClick();
        Check(copied is not null && copied.Contains(user.Text, StringComparison.Ordinal)
            && copied.Contains(answer, StringComparison.Ordinal) && copied.Contains(file, StringComparison.Ordinal)
            && copied.Contains(quote, StringComparison.Ordinal),
            name + " full-conversation copy includes both roles, actual file paths and quoted evidence");
        CheckAiResultGeometry(pane, name + "/sources");
        host.PerformLayout();
    }

    private static void CheckAiResultGeometry(AiSearchPane pane, string name)
    {
        var sheet = pane.Transcript.Controls[0];
        foreach (var list in Descendants(pane).OfType<ListView>())
        {
            Control group = list.Parent!;
            var labels = group.Controls.OfType<Label>().ToArray();
            var heading = labels[0];
            var hint = labels[1];
            var actions = group.Controls.OfType<FlowLayoutPanel>().Single();
            Check(labels.Length == 2 && group.Parent == sheet && group.AccessibleName == list.AccessibleName
                && group.BackColor == Theme.Surface && list.BackColor == Theme.Canvas
                && heading.Text.Length > 0 && heading.Bottom <= hint.Top && hint.Text.Length > 0
                && hint.Bottom <= list.Top && list.Bottom <= actions.Top
                && list.Left > 0 && list.Right < group.Width && actions.Bottom < group.Height,
                name + " selectable file rows and usage hint occupy a distinct inset result group");
            Check(actions.Controls.Count == 5 && actions.Controls.Cast<Control>().All(button =>
                button.Left >= 0 && button.Right <= actions.Width && button.Bottom <= actions.Height),
                name + " result group's five actionable buttons fit its width");
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
