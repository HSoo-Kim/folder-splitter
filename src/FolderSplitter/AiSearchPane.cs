using System.ComponentModel;
using System.Globalization;
using QuadExplorer.AiSearch;

namespace QuadExplorer;

internal enum AiHitAction { ShowInPane, Open, NewTab, NewPane, Preview, SearchContents }

internal sealed partial class AiSearchPane : Panel
{
    private readonly UiText text;
    private AiSearchSettings settings;
    private readonly List<ChatTurn> turns = [];
    private readonly Panel layout = new() { Dock = DockStyle.Fill };
    private readonly Panel header = new();
    private readonly Label title = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    private readonly Panel help = new();
    private readonly Label instruction = new() { UseMnemonic = false };
    private readonly Label mechanism = new() { UseMnemonic = false };
    private readonly ComboBox modePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button modelButton;
    private readonly AiModelMenu modelMenu;
    private readonly Panel transcript = new() { AutoScroll = true, AccessibleName = "AI conversation" };
    private readonly Panel sheet = new() { AccessibleName = "AI conversation sheet" };
    private readonly Panel footer = new();
    private readonly ComposerField field = new();
    private readonly ComposerTextBox composer = new() { Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
    private readonly Label hint = new() { UseMnemonic = false, AutoEllipsis = true, AccessibleName = "AI search status" };
    private readonly Label contextMeter = new()
    {
        UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight,
        AccessibleName = "Last request context usage"
    };
    private readonly System.Windows.Forms.Timer progressTimer = new() { Interval = 100 };
    private readonly Button send, newChat, configure, detach, example, selectedDocument;
    private readonly ContextMenuStrip conversationMenu = new();
    private readonly ToolTip tips = new();
    private readonly List<(Control Control, bool Right)> transcriptRows = [];
    private string? selectedDocumentPath;
    private Button? copyConversationButton;
    private int renderedTurns;
    private Font? iconFont;
    private Point? dragStart;
    private int generation;
    private bool arranging;
    private bool arrangingChrome;
    private bool floating;
    private CancellationTokenSource? modelListCancellation;
    private int modelListGeneration;
    private bool listingModels;
    private int progressFrame;
    private string hintKey = "AI chat is kept only while this window is open.";

    internal AiSearchPane(UiText text, AiSearchSettings settings)
    {
        this.text = text;
        this.settings = settings;
        AccessibleName = "AI search";
        MinimumSize = new Size(320, 0);
        newChat = Theme.Button("\uE710", "New AI chat", ResetConversation, quiet: true);
        configure = Theme.Button("\uE713", "AI search settings", ShowSettings, quiet: true);
        detach = Theme.Button("\uE8A7", "Detach AI search", () => DetachRequested?.Invoke(this, EventArgs.Empty), quiet: true);
        send = Theme.Button("", "Send AI search", () => { if (Busy) Cancel(); else _ = SendAsync(); });
        modelMenu = new AiModelMenu(text);
        modelButton = Theme.Button("", "Choose AI model", ShowModelMenu, quiet: true);
        modelButton.AutoEllipsis = true;
        modelButton.TextAlign = ContentAlignment.MiddleLeft;
        modelButton.Padding = new Padding(8, 0, 8, 0);
        modelMenu.ModelSelected += SelectModel;
        modelMenu.EffortSelected += SelectEffort;
        modelMenu.SettingsRequested += (_, _) => ShowSettings();
        modelMenu.RefreshRequested += (_, _) => PendingModelList = RefreshModelsAsync(includeSubscriptions: true);
        example = Theme.Button("", "AI search example", () =>
        {
            composer.Text = text.T(settings.Mode == AiSearchMode.ReadDocuments
                ? "Summarize the proposal documents" : "Find proposal PDF files");
            composer.SelectionStart = composer.TextLength;
            FocusComposer();
        }, quiet: true);
        selectedDocument = Theme.Button("", "Clear selected file", () => SetDocumentTarget(null), quiet: true);
        selectedDocument.Dock = DockStyle.None;
        selectedDocument.AutoEllipsis = true;
        selectedDocument.TextAlign = ContentAlignment.MiddleLeft;
        selectedDocument.Visible = false;
        foreach (var button in new[] { newChat, configure, detach, send, example, modelButton }) button.Dock = DockStyle.None;
        example.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.AddRange([title, newChat, configure, detach]);
        help.Controls.AddRange([instruction, mechanism, example]);
        modePicker.SelectionChangeCommitted += (_, _) => SaveMode();
        foreach (var picker in new[] { modePicker })
        {
            picker.DrawMode = DrawMode.OwnerDrawFixed;
            Theme.ThemeComboBox(picker);
            picker.FontChanged += (_, _) => picker.ItemHeight = picker.Font.Height + Space(2);
            picker.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selected : Theme.Surface);
                e.Graphics.FillRectangle(background, e.Bounds);
                TextRenderer.DrawText(e.Graphics, picker.GetItemText(picker.Items[e.Index]), picker.Font, e.Bounds,
                    !picker.Enabled ? Theme.Muted : Theme.Text,
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
        }
        transcript.Controls.Add(sheet);
        conversationMenu.Items.Add("", null, (_, _) => CopyConversation());
        conversationMenu.Opening += (_, _) => conversationMenu.Items[0].Enabled = turns.Count > 0;
        transcript.ContextMenuStrip = sheet.ContextMenuStrip = conversationMenu;
        field.Controls.Add(composer);
        field.Controls.AddRange([modePicker, modelButton, send]);
        composer.TabIndex = 0;
        modePicker.TabIndex = 1;
        modelButton.TabIndex = 2;
        send.TabIndex = 3;
        footer.Controls.AddRange([field, selectedDocument, hint, contextMeter]);
        layout.Controls.AddRange([header, help, transcript, footer]);
        Controls.Add(layout);
        composer.SendRequested = () => { if (!Busy) _ = SendAsync(); };
        composer.CancelRequested = Cancel;
        composer.TextChanged += (_, _) => UpdateSendButton();
        composer.GotFocus += (_, _) => field.Invalidate();
        composer.LostFocus += (_, _) => field.Invalidate();
        progressTimer.Tick += (_, _) => { progressFrame = (progressFrame + 1) % 12; hint.Invalidate(); };
        hint.Paint += (_, e) =>
        {
            if (!Busy) return;
            int diameter = Math.Min(Font.Height - 2, hint.Height - 4);
            if (diameter < 4) return;
            var smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Accent, Math.Max(1, DeviceDpi / 96f * 2));
            e.Graphics.DrawArc(pen, new Rectangle(2, (hint.Height - diameter) / 2, diameter, diameter),
                progressFrame * 30, 255);
            e.Graphics.SmoothingMode = smoothing;
        };
        Disposed += (_, _) => progressTimer.Dispose();
        field.Resize += (_, _) => ArrangeComposer();
        layout.ClientSizeChanged += (_, _) => ArrangeChrome();
        transcript.ClientSizeChanged += (_, _) => ArrangeTranscript();
        transcript.Layout += (_, _) => ArrangeTranscript();
        title.Cursor = Cursors.SizeAll;
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) dragStart = e.Location; };
        title.MouseUp += (_, _) => dragStart = null;
        title.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || dragStart is not { } start) return;
            Size threshold = SystemInformation.DragSize;
            if (new Rectangle(start.X - threshold.Width / 2, start.Y - threshold.Height / 2,
                threshold.Width, threshold.Height).Contains(e.Location)) return;
            dragStart = null;
            DragRequested?.Invoke(title.PointToScreen(e.Location));
        };
        RefreshLanguage();
        ApplyColors();
        ApplyMetrics();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, Task<ChatTurn?>>? Submit { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action? CancelRequested { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action? ResetRequested { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, CancellationToken, Task<ProviderProbe>>? Probe { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, CancellationToken, Task<ProviderProbe>>? ListModels { get; set; }
    internal event Action<AiHit, AiHitAction>? OpenRequested;
    internal event Action<string?>? DocumentTargetChanged;
    internal event Action<Point>? DragRequested;
    internal event EventHandler? DetachRequested;
    internal event Action<AiSearchSettings>? SettingsChanged;
    internal event EventHandler? StateChanged;
    internal IReadOnlyList<ChatTurn> Turns => turns.AsReadOnly();
    internal bool Busy { get; private set; }
    internal bool ProgressIndicatorRunning => progressTimer.Enabled;
    internal string HintText => hint.Text;
    internal Label ContextMeter => contextMeter;
    internal TextBox Composer => composer;
    internal Button SendButton => send;
    internal Button NewChatButton => newChat;
    internal Button SettingsButton => configure;
    internal Button DetachButton => detach;
    internal Button ExampleButton => example;
    internal Panel Transcript => transcript;
    internal Panel HelpContent => help;
    internal Button ModelButton => modelButton;
    internal AiModelMenu ModelMenu => modelMenu;
    internal ComboBox ModePicker => modePicker;
    internal AiSearchSettings CurrentSettings => settings;
    internal Task PendingModelList { get; private set; } = Task.CompletedTask;
    internal Task PendingSubmission { get; private set; } = Task.CompletedTask;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string> CopyText { get; set; } = value => Clipboard.SetText(value);

    internal void SetFloating(bool floating)
    {
        this.floating = floating;
        detach.Visible = !floating;
        ArrangeChrome();
    }
    internal void FocusComposer() => composer.Focus();

    // MainForm can report actual planner/index stages without a fabricated timer.
    internal void SetHint(string key)
    {
        hintKey = key;
        hint.Text = text.T(key);
        ArrangeChrome();
    }

    internal void SetDocumentProgress(DocumentSearchProgress progress)
    {
        if (!Busy || settings.Mode != AiSearchMode.ReadDocuments) return;
        hint.Text = text.T("Searching {0} · folders {1} · candidates {2} · read {3}",
            progress.Searches, progress.FoldersListed, progress.Candidates, progress.FilesRead);
        ArrangeChrome();
    }

    internal void RefreshLanguage() => RefreshLanguage(rebuildTranscript: true);

    private void RefreshLanguage(bool rebuildTranscript)
    {
        title.Text = text.T("AI search · Beta");
        instruction.Text = text.T("Describe it naturally");
        example.Text = text.T(settings.Mode == AiSearchMode.ReadDocuments
            ? "Example: Summarize the proposal documents" : "Example: Find proposal PDF files");
        composer.PlaceholderText = text.T("Describe a file, or refine your last search…");
        composer.AccessibleName = text.T("AI search message");
        SetButtonLabel(newChat, "New chat");
        SetButtonLabel(configure, "AI search settings");
        SetButtonLabel(detach, "Detach AI search");
        conversationMenu.Items[0].Text = text.T("Copy conversation");
        modePicker.AccessibleName = text.T("AI search mode");
        modelButton.AccessibleName = text.T("Model");
        modePicker.Items.Clear();
        modePicker.Items.AddRange([text.T("Find by name"), text.T("Search contents")]);
        modePicker.SelectedIndex = settings.Mode == AiSearchMode.ReadDocuments ? 1 : 0;
        RefreshModelButton();
        RefreshDocumentTarget();
        hint.Text = text.T(hintKey);
        UpdateContextMeter();
        if (rebuildTranscript) RebuildTranscript();
        UpdateSendButton();
        ArrangeChrome();
    }

    private void SetButtonLabel(Button button, string key)
    {
        button.AccessibleName = text.T(key);
        tips.SetToolTip(button, text.T(key));
    }

    internal void ApplyColors()
    {
        Theme.Apply(this);
        BackColor = layout.BackColor = header.BackColor = help.BackColor = footer.BackColor = Theme.Canvas;
        title.BackColor = instruction.BackColor = mechanism.BackColor = hint.BackColor = contextMeter.BackColor = Theme.Canvas;
        title.ForeColor = instruction.ForeColor = Theme.Text;
        mechanism.ForeColor = hint.ForeColor = Theme.Muted;
        UpdateContextMeter();
        transcript.BackColor = sheet.BackColor = Theme.Surface;
        composer.BackColor = Theme.Surface;
        composer.ForeColor = Theme.Text;
        field.BackColor = Theme.Canvas;
        foreach (var row in transcriptRows)
        {
            if (row.Control is TranscriptMessage message) ApplyMessageColors(message);
            if (row.Control is FileResultGroup group) group.ApplyColors();
        }
        field.Invalidate();
        sheet.Invalidate(true);
    }

    private static void ApplyMessageColors(TranscriptMessage message)
    {
        message.BackColor = Theme.Surface;
        message.Body.BackColor = message.IsUser ? Theme.Selected : Theme.Surface;
        message.Body.ForeColor = (message.Body.Tag as string) switch { "error" => Theme.ErrorText, "muted" => Theme.Muted, _ => Theme.Text };
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (send is not null) ApplyMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
    }

    private int Space(int units) => (int)Math.Round(units * 4 * DeviceDpi / 96d);

    private void ApplyMetrics()
    {
        iconFont?.Dispose();
        iconFont = new Font(Theme.IconFont, Font.SizeInPoints);
        foreach (Button button in new[] { newChat, configure, detach })
        {
            button.Font = iconFont;
            button.Size = new Size(Font.Height + Space(2), Font.Height + Space(2));
            button.Margin = new Padding(Space(1), 0, 0, 0);
        }
        ArrangeChrome();
        ArrangeTranscript();
    }

    private void ArrangeChrome()
    {
        if (arrangingChrome || layout.ClientSize.Width <= 0) return;
        arrangingChrome = true;
        try
        {
            int inset = Space(3), gap = Space(2), width = layout.ClientSize.Width;
            int contentWidth = Math.Max(1, width - inset * 2);
            bool compact = layout.ClientSize.Height < Font.Height * 26;
            int buttonSize = Font.Height + Space(2);
            int right = width - inset;
            foreach (var button in new[] { detach, configure, newChat })
            {
                if (button == detach && floating) continue;
                button.SetBounds(right - buttonSize, Space(1), buttonSize, buttonSize);
                right -= buttonSize + Space(1);
            }
            title.SetBounds(inset, Space(1), Math.Max(1, right - inset), buttonSize);
            title.Text = text.T(TextRenderer.MeasureText(text.T("AI search · Beta"), Font).Width > title.Width
                ? "AI Beta" : "AI search · Beta");
            tips.SetToolTip(title, text.T("AI search · Beta"));
            header.SetBounds(0, 0, width, buttonSize + gap);
            int y = 0;
            int Measure(Label label) => label.GetPreferredSize(new Size(contentWidth, 0)).Height;
            bool documents = settings.Mode == AiSearchMode.ReadDocuments;
            instruction.Text = text.T(documents ? "Search contents" : compact ? "Describe a file" : "Describe it naturally");
            bool showInstruction = !documents || !compact;
            instruction.Visible = showInstruction;
            instruction.SetBounds(inset, y, contentWidth, Measure(instruction));
            if (showInstruction) y = instruction.Bottom + Space(1);
            bool showMechanism = documents || !compact;
            mechanism.Visible = showMechanism;
            if (showMechanism)
            {
                mechanism.Text = text.T(documents
                    ? compact ? "Your AI reads excerpts." : "Find answers in your documents. Relevant excerpts go to your AI."
                    : "Find files by name, folder or file type.");
                mechanism.SetBounds(inset, y, contentWidth, Measure(mechanism));
                y = mechanism.Bottom + Space(1);
            }
            string provider = text.T("Provider") + ": " + (settings.Provider == AiProviderKind.LocalEndpoint
                ? text.T("Local endpoint") : settings.Provider == AiProviderKind.ClaudeCli ? "Claude CLI" : "Codex CLI");
            tips.SetToolTip(modelButton, text.T("Model") + " · " + provider + " · " + settings.Model);
            string modeDescription = text.T(documents
                ? "Selected AI receives relevant body excerpts. No permanent body index." : "AI: name/folder/ext → real index")
                + Environment.NewLine + text.T("Chat kept · context separated by mode.");
            tips.SetToolTip(modePicker, modeDescription);
            modePicker.AccessibleDescription = modeDescription;
            tips.SetToolTip(composer, text.T("Up / Down recalls sent messages at the first / last line. New chat clears recall, not your draft."));
            example.Visible = !compact;
            if (!compact)
            {
                int exampleHeight = TextRenderer.MeasureText(example.Text, Font, new Size(contentWidth - gap * 2, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + gap;
                example.SetBounds(inset, y, contentWidth, exampleHeight);
                y = example.Bottom + gap;
            }
            composer.PlaceholderText = text.T(documents ? "Ask about document contents…"
                : compact ? "Find proposal PDF files" : "Describe a file, or refine your last search…");
            help.SetBounds(0, header.Bottom, width, y);
            bool showHint = !compact || Busy || hintKey != "AI chat is kept only while this window is open.";
            int hintHeight = showHint ? (compact ? Math.Min(Measure(hint), Font.Height) : Measure(hint)) + Space(1) : 0;
            int targetHeight = selectedDocumentPath is not null ? buttonSize + Space(1) : 0;
            int meterHeight = contextMeter.Visible ? Font.Height + Space(1) : 0;
            int composerHeight = Font.Height * (compact ? 2 : 3) + inset * 2 + ComposerToolbar(contentWidth).Height + gap;
            int footerHeight = composerHeight + targetHeight + hintHeight + meterHeight + gap * 2;
            // Preserve room to read the conversation at large text sizes; the selected-file chip
            // already conveys this nonessential hint, while progress and error hints remain visible.
            if (showHint && selectedDocumentPath is not null && !Busy
                && hintKey == "Selected file is ready. Enter a question; nothing has been sent."
                && layout.ClientSize.Height - help.Bottom - footerHeight < Font.Height * 2)
            {
                showHint = false;
                footerHeight -= hintHeight;
                hintHeight = 0;
            }
            hint.Visible = showHint;
            footer.SetBounds(0, Math.Max(help.Bottom, layout.ClientSize.Height - footerHeight), width, footerHeight);
            field.SetBounds(inset, gap, contentWidth, composerHeight);
            selectedDocument.SetBounds(inset, field.Bottom + Space(1), contentWidth, Math.Max(0, targetHeight - Space(1)));
            int hintTop = selectedDocumentPath is not null ? selectedDocument.Bottom + Space(1) : field.Bottom + Space(1);
            hint.SetBounds(inset, hintTop, contentWidth, showHint ? Math.Max(hint.PreferredHeight, hintHeight - Space(1)) : 0);
            contextMeter.SetBounds(inset, hint.Bottom, contentWidth, meterHeight);
            transcript.SetBounds(0, help.Bottom, width, Math.Max(1, footer.Top - help.Bottom));
            ArrangeComposer();
        }
        finally { arrangingChrome = false; }
    }

    private void ArrangeComposer()
    {
        int inset = Space(3), gap = Space(2);
        var toolbar = ComposerToolbar(field.ClientSize.Width);
        int y = Math.Max(inset + Font.Height * 2 + gap, field.Height - inset - toolbar.Height);
        int available = Math.Max(1, field.ClientSize.Width - inset * 2);
        int row = toolbar.RowHeight + gap;
        composer.SetBounds(inset, inset, available, Math.Max(Font.Height * 2, y - gap - inset));
        if (toolbar.Rows == 1)
        {
            modePicker.SetBounds(inset, y, toolbar.ModeWidth, modePicker.PreferredHeight);
            send.SetBounds(field.Width - inset - toolbar.SendWidth, y, toolbar.SendWidth, toolbar.RowHeight);
            modelButton.SetBounds(send.Left - gap - toolbar.ModelWidth, y, toolbar.ModelWidth, toolbar.RowHeight);
        }
        else
        {
            modePicker.SetBounds(inset, y, Math.Min(available, toolbar.ModeWidth), modePicker.PreferredHeight);
            y += row;
            send.SetBounds(field.Width - inset - toolbar.SendWidth, y, toolbar.SendWidth, toolbar.RowHeight);
            modelButton.SetBounds(inset, y, Math.Max(1, send.Left - gap - inset), toolbar.RowHeight);
        }
    }

    private (int Rows, int RowHeight, int Height, int ModeWidth, int ModelWidth, int SendWidth) ComposerToolbar(int width)
    {
        int gap = Space(2), available = Math.Max(1, width - Space(3) * 2);
        int PickerWidth(ComboBox picker) => picker.Items.Cast<string>()
            .Select(value => TextRenderer.MeasureText(value, Font).Width).DefaultIfEmpty(Font.Height * 3).Max()
            + SystemInformation.VerticalScrollBarWidth + Space(2);
        int modeWidth = Math.Min(available, PickerWidth(modePicker));
        int sendWidth = Math.Min(available / 2, Math.Max(Font.Height + Space(4), send.GetPreferredSize(Size.Empty).Width));
        int modelWidth = Math.Min(available, TextRenderer.MeasureText(modelButton.Text, Font).Width + Space(6));
        int rowHeight = Math.Max(Font.Height + Space(2), modePicker.PreferredHeight);
        int rows = modeWidth + modelWidth + sendWidth + gap * 3 <= available ? 1 : 2;
        return (rows, rowHeight, rows * rowHeight + (rows - 1) * gap, modeWidth, modelWidth, sendWidth);
    }

    private void UpdateSendButton()
    {
        modelButton.Enabled = !Busy;
        send.Text = text.T(Busy ? "Stop" : "Send");
        send.AccessibleName = send.Text;
        send.Enabled = Busy || !string.IsNullOrWhiteSpace(composer.Text);
        tips.SetToolTip(send, text.T(Busy ? "Stop search (Esc)" : "Enter to send · Shift+Enter for a new line"));
        hint.Padding = new Padding(Busy ? Font.Height + Space(1) : 0, 0, 0, 0);
        if (Busy)
        {
            if (!progressTimer.Enabled) progressTimer.Start();
        }
        else
        {
            progressTimer.Stop();
            progressFrame = 0;
        }
        hint.Invalidate();
        ArrangeComposer();
    }

    internal Task SendAsync()
    {
        if (Busy || string.IsNullOrWhiteSpace(composer.Text)) return Task.CompletedTask;
        PendingSubmission = SubmitCoreAsync();
        return PendingSubmission;
    }

    private async Task SubmitCoreAsync()
    {
        if (Submit is not { } submit)
        {
            SetHint("Configure a provider and model in AI search settings.");
            return;
        }
        string message = composer.Text.Trim();
        composer.RememberSentMessage(message);
        modelListGeneration++;
        modelListCancellation?.Cancel();
        modelListCancellation = null;
        listingModels = false;
        composer.Clear();
        int request = ++generation;
        Busy = true;
        turns.Add(new ChatTurn(ChatRole.User, message));
        SetHint(settings.Mode == AiSearchMode.ReadDocuments
            ? "Finding relevant documents → reading excerpts…" : "Understanding your request → searching the file index…");
        AppendNewTurns();
        UpdateSendButton();
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            ChatTurn? reply = await submit(message);
            if (IsDisposed || request != generation) return;
            if (reply is not null) turns.Add(reply);
            hintKey = reply?.Error is not null ? "Search failed. Check provider, endpoint and model in settings."
                : reply?.Result is { Hits.Count: 0, Plan.Clarification: null } ? "No matching indexed files. Try fewer terms or another folder."
                : "AI chat is kept only while this window is open.";
        }
        catch (OperationCanceledException)
        {
            if (IsDisposed || request != generation) return;
            hintKey = "Search stopped. You can send another message.";
        }
        catch (Exception error)
        {
            if (IsDisposed || request != generation) return;
            turns.Add(new ChatTurn(ChatRole.Assistant, "", Error: error.Message));
            hintKey = "Search failed. Check provider, endpoint and model in settings.";
        }
        finally
        {
            if (!IsDisposed && request == generation)
            {
                Busy = false;
                hint.Text = text.T(hintKey);
                UpdateContextMeter();
                ArrangeChrome();
                AppendNewTurns();
                UpdateSendButton();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    internal void Cancel()
    {
        if (!Busy) return;
        generation++;
        Busy = false;
        CancelRequested?.Invoke();
        SetHint("Search stopped. You can send another message.");
        UpdateSendButton();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ResetConversation()
    {
        Cancel();
        generation++;
        turns.Clear();
        selectedDocumentPath = null;
        RefreshDocumentTarget();
        UpdateContextMeter();
        composer.ResetHistory();
        ResetRequested?.Invoke();
        SetHint("AI chat is kept only while this window is open.");
        RebuildTranscript();
        FocusComposer();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateContextMeter()
    {
        var usage = turns.LastOrDefault(turn => turn.Role == ChatRole.Assistant)?.ContextUsage;
        contextMeter.Visible = usage is not null;
        if (usage is null)
        {
            contextMeter.Text = "";
            return;
        }
        long? limit = usage.ContextWindow;
        if (limit is null)
            limit = discoveredModels.FirstOrDefault(model => model.Provider == usage.Provider
                && model.Id == usage.ModelId)?.ContextWindow;
        double fraction = limit is > 0 ? (double)usage.InputTokens / limit.Value : 0;
        contextMeter.Text = limit is > 0
            ? text.T("Context · last request: {0:N0} / {1:N0} ({2:P1})",
                usage.InputTokens, limit.Value, fraction)
            : text.T("Context · last request: {0:N0} input tokens", usage.InputTokens);
        contextMeter.ForeColor = fraction >= 0.85 ? Theme.ErrorText : Theme.Muted;
        string detail = text.T("Measured input tokens for the last request, not cumulative chat usage.");
        if (fraction >= 0.85)
            detail += " " + text.T("Near the context limit. Narrow your request or start a new chat.");
        tips.SetToolTip(contextMeter, detail);
        contextMeter.AccessibleDescription = detail;
    }


    private void RebuildTranscript()
    {
        sheet.SuspendLayout();
        foreach (Control control in sheet.Controls.Cast<Control>().ToArray()) control.Dispose();
        transcriptRows.Clear();
        copyConversationButton = null;
        renderedTurns = 0;
        if (turns.Count == 0)
            AddText(text.T(settings.Mode == AiSearchMode.ReadDocuments
                ? "Ask a question about your documents. Only relevant excerpts are read for this request."
                : "Results come from your file index. Ask a follow-up to narrow them down."), false, muted: true);
        else
            AppendNewTurns();
        sheet.ResumeLayout(false);
        ArrangeTranscript();
        ApplyColors();
        ScrollTranscriptToEnd();
    }

    private void AppendNewTurns()
    {
        if (renderedTurns == turns.Count) return;
        sheet.SuspendLayout();
        if (renderedTurns == 0)
        {
            foreach (Control control in sheet.Controls.Cast<Control>().ToArray()) control.Dispose();
            transcriptRows.Clear();
        }
        else
            transcriptRows.RemoveAt(transcriptRows.Count - 1);
        for (int i = renderedTurns; i < turns.Count; i++) AddTurn(turns[i]);
        renderedTurns = turns.Count;
        if (copyConversationButton is null)
        {
            copyConversationButton = Theme.Button(text.T("Copy conversation"), "Copy conversation", CopyConversation, quiet: true);
            copyConversationButton.Dock = DockStyle.None;
            copyConversationButton.AccessibleName = text.T("Copy conversation");
            sheet.Controls.Add(copyConversationButton);
        }
        transcriptRows.Add((copyConversationButton, false));
        sheet.ResumeLayout(false);
        ArrangeTranscript();
        ScrollTranscriptToEnd();
    }

    private void ScrollTranscriptToEnd() =>
        transcript.AutoScrollPosition = new Point(0, Math.Max(0, sheet.Height - transcript.ClientSize.Height));

    private void AddTurn(ChatTurn turn)
    {
        bool unverified = turn.DocumentResult?.Question == DocumentSearch.UnverifiedEvidenceMessage;
        if (!unverified && turn.Text.Length > 0 && (turn.Role == ChatRole.User
            || turn.Result?.Plan.Clarification is not null || turn.Result is null && turn.Error is null))
            AddText(turn.DocumentResult?.Question is { } question
                && question.StartsWith(DocumentSearch.OutsideContentScopeMessage, StringComparison.Ordinal)
                ? text.T(DocumentSearch.OutsideContentScopeMessage) + question[DocumentSearch.OutsideContentScopeMessage.Length..]
                : turn.Text, turn.Role == ChatRole.User);
        if (turn.Error is { Length: > 0 } error) AddText(text.T("Provider error: {0}", error), false, error: true);
        if (turn.DocumentResult is { } documents)
        {
            var coverage = documents.Coverage;
            AddText(text.T("Checked {0} folders · read {1} files · failed {2}",
                coverage.FoldersListed, coverage.FilesRead, coverage.FailedReads), false, muted: true);
            if (documents.Sources.Count > 0)
                AddHits(documents.Sources.DistinctBy(source => source.FullPath, StringComparer.OrdinalIgnoreCase)
                    .Select(source => new AiHit(source.FullPath, false, null, null)).ToArray(), indexed: false, documents.Sources,
                    unverified: unverified);
            if (coverage.Limited)
                AddText(text.T("Only part of the search scope was checked. Provide a candidate folder to continue."), false, muted: true);
            foreach (var note in coverage.Notes)
                AddText(note == "Bounded investigation only; unread files and folders may contain additional evidence."
                    ? text.T("Only the listed files and folders were checked.") : note, false, muted: true);
            return;
        }
        if (turn.Result is not { } result || result.Plan.Clarification is not null) return;
        var conditions = result.Plan.AllTerms.Concat(result.Plan.Extensions)
            .Concat(result.Plan.AnyOf.Select(group => string.Join(" | ", group))).ToArray();
        if (conditions.Length > 0)
            AddText(text.T("Search conditions: {0}", string.Join(" · ", conditions)), false, muted: true);
        if (result.Hits.Count == 0)
            AddText(text.T("No matching indexed files. Try fewer terms or another folder."), false, muted: true);
        else
            AddHits(result.Hits);
        if (result.Truncated) AddText(text.T("More results exist. Refine your search."), false, muted: true);
        if (result.Note is { Length: > 0 } resultNote) AddText(resultNote, false, muted: true);
    }

    private void AddText(string value, bool right, bool error = false, bool muted = false)
    {
        var message = new TranscriptMessage(right);
        message.Body.Text = value;
        message.Body.AccessibleName = text.T(right ? "You" : "AI assistant");
        message.Body.Tag = error ? "error" : muted ? "muted" : null;
        AddTextMenu(message.Body);
        sheet.Controls.Add(message);
        transcriptRows.Add((message, right));
        ApplyMessageColors(message);
    }

    private void AddTextMenu(TranscriptTextBox body)
    {
        var menu = new ContextMenuStrip();
        var copy = menu.Items.Add(text.T("Copy"), null, (_, _) => body.Copy());
        menu.Items.Add(text.T("Select all"), null, (_, _) => body.SelectAll());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(text.T("Copy conversation"), null, (_, _) => CopyConversation());
        menu.Opening += (_, _) => copy.Enabled = body.SelectionLength > 0;
        body.ContextMenuStrip = menu;
        body.Disposed += (_, _) => menu.Dispose();
    }

    private void CopyConversation()
    {
        var parts = new List<string>();
        foreach (var row in transcriptRows)
        {
            if (row.Control is TranscriptMessage message)
                parts.Add(message.Body.AccessibleName + Environment.NewLine + message.Body.Text);
            else if (row.Control is FileResultGroup group)
            {
                parts.Add(group.Heading.Text + Environment.NewLine
                    + string.Join(Environment.NewLine, group.List.Items.Cast<ListViewItem>().Select(item => ((AiHit)item.Tag!).FullPath)));
                parts.AddRange(group.Evidence.Select(body => body.Text));
            }
        }
        if (parts.Count > 0) CopyText(string.Join(Environment.NewLine + Environment.NewLine, parts));
    }

    private void AddHits(IReadOnlyList<AiHit> hits, bool indexed = true, IReadOnlyList<DocumentSourceReference>? sources = null,
        bool unverified = false)
    {
        var list = new ResultListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None, Scrollable = false, ShowItemToolTips = true,
            AccessibleName = text.T(indexed ? "Indexed file results" : "Document sources"), UseCompatibleStateImageBehavior = false, OwnerDraw = true
        };
        list.Columns.Add(text.T("Name"));
        list.Columns.Add(text.T("Folder"));
        list.Columns.Add(text.T("Type"));
        var rowImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        var abbreviatedNames = new Dictionary<int, (int Width, string Text)>();
        Font nameFont = new(list.Font, FontStyle.Bold);
        void ResizeRows()
        {
            int height = Math.Clamp(nameFont.Height + list.Font.Height + Space(3), 1, 256);
            if (rowImages.ImageSize != new Size(1, height))
            {
                list.SmallImageList = null;
                rowImages.ImageSize = new Size(1, height);
                list.SmallImageList = rowImages;
            }
            if (list.Parent?.Parent is not null) ArrangeTranscript();
        }
        list.FontChanged += (_, _) =>
        {
            abbreviatedNames.Clear();
            Font previous = nameFont;
            nameFont = new Font(list.Font, FontStyle.Bold);
            if (list.Parent is FileResultGroup group) group.Heading.Font = nameFont;
            previous.Dispose();
            ResizeRows();
        };
        list.DpiChangedAfterParent += (_, _) => { abbreviatedNames.Clear(); ResizeRows(); };
        ResizeRows();
        int hoveredIndex = -1;
        void SetHover(int index)
        {
            if (hoveredIndex == index) return;
            int previous = hoveredIndex;
            hoveredIndex = index;
            list.Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            if (previous >= 0) list.Invalidate(list.Items[previous].Bounds);
            if (index >= 0) list.Invalidate(list.Items[index].Bounds);
        }
        list.MouseMove += (_, e) => SetHover(list.GetItemAt(e.X, e.Y)?.Index ?? -1);
        list.MouseLeave += (_, _) => SetHover(-1);
        list.DrawSubItem += (_, e) =>
        {
            if (e.ColumnIndex != 0 || e.Item is null) return;
            using var back = new SolidBrush(e.Item.Selected ? Theme.Selected
                : e.Item.Index == hoveredIndex ? Theme.Hover : Theme.Surface);
            e.Graphics.FillRectangle(back, e.Bounds);
            int inset = Space(2), iconWidth = Math.Max(list.Font.Height, Space(4));
            var icon = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top, iconWidth, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Item.Tag is AiHit { IsDirectory: true } ? "\uE8B7" : "\uE8A5",
                iconFont, icon, Theme.Muted, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int x = icon.Right + inset;
            int available = Math.Max(1, e.Bounds.Right - x - inset);
            int textHeight = nameFont.Height + list.Font.Height;
            int top = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - textHeight) / 2);
            if (!abbreviatedNames.TryGetValue(e.Item.Index, out var abbreviated) || abbreviated.Width != available)
            {
                abbreviated = (available, AbbreviateFileName(e.Graphics, e.Item.Text, nameFont, available));
                abbreviatedNames[e.Item.Index] = abbreviated;
            }
            TextRenderer.DrawText(e.Graphics, abbreviated.Text, nameFont, new Rectangle(x, top, available, nameFont.Height), Theme.Text,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, e.Item.SubItems[1].Text, list.Font,
                new Rectangle(x, top + nameFont.Height, available, list.Font.Height), Theme.Muted,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
            if (e.Item.Index < list.Items.Count - 1)
            {
                using var separator = new Pen(Theme.Border);
                e.Graphics.DrawLine(separator, x, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            if (e.Item.Focused && list.Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1), Theme.Text, Theme.Selected);
        };
        foreach (AiHit hit in hits)
        {
            string name = Path.GetFileName(hit.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string folder = Path.GetDirectoryName(hit.FullPath) ?? hit.FullPath;
            list.Items.Add(new ListViewItem([name.Length > 0 ? name : hit.FullPath, folder,
                hit.IsDirectory ? text.T("Folder") : Path.GetExtension(hit.FullPath)])
            { Tag = hit, ToolTipText = hit.FullPath });
        }
        void Open(AiHitAction action)
        {
            if (list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag is not AiHit hit) return;
            if (action == AiHitAction.SearchContents)
            {
                if (hit.IsDirectory) return;
                if (!DocumentSearch.IsEligibleFile(hit.FullPath)) { SetHint("Selected file is no longer available."); return; }
                if (settings.Mode != AiSearchMode.ReadDocuments)
                {
                    modePicker.SelectedIndex = 1;
                    SaveMode();
                }
                SetDocumentTarget(hit.FullPath);
                SetHint("Selected file is ready. Enter a question; nothing has been sent.");
                FocusComposer();
            }
            else OpenRequested?.Invoke(hit, action);
        }
        list.DoubleClick += (_, _) => Open(AiHitAction.ShowInPane);
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Open(e.Control ? AiHitAction.NewTab : AiHitAction.ShowInPane);
        };
        var menu = new ContextMenuStrip();
        var actions = new FlowLayoutPanel { WrapContents = true, Margin = Padding.Empty };
        foreach (var (key, action) in new[]
        {
            ("Open", AiHitAction.Open), ("Open in current pane", AiHitAction.ShowInPane),
            ("Open in new tab", AiHitAction.NewTab), ("Open in new pane", AiHitAction.NewPane),
            ("Preview", AiHitAction.Preview), ("Search contents of this file", AiHitAction.SearchContents)
        })
        {
            string caption = action switch
            {
                AiHitAction.ShowInPane => "Current pane",
                AiHitAction.NewTab => "New tab",
                AiHitAction.NewPane => "New pane",
                _ => key
            };
            var button = Theme.Button(text.T(caption), key, () => Open(action), quiet: true);
            button.Dock = DockStyle.None;
            button.Tag = action;
            button.Margin = new Padding(0, 0, Space(2), Space(1));
            button.Enabled = false;
            tips.SetToolTip(button, text.T(key));
            actions.Controls.Add(button);
            var menuItem = menu.Items.Add(text.T(key), null, (_, _) => Open(action));
            menuItem.Tag = action;
        }
        list.SelectedIndexChanged += (_, _) =>
        {
            bool selectedFile = list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is AiHit { IsDirectory: false };
            foreach (Control button in actions.Controls)
                button.Enabled = list.SelectedItems.Count == 1 && (button.Tag is not AiHitAction.SearchContents || selectedFile);
            if (list.SelectedItems.Count != 1) return;
            Rectangle row = list.RectangleToScreen(list.SelectedItems[0].Bounds);
            Rectangle viewport = transcript.RectangleToScreen(transcript.ClientRectangle);
            int delta = row.Top < viewport.Top ? row.Top - viewport.Top : row.Bottom > viewport.Bottom ? row.Bottom - viewport.Bottom : 0;
            if (delta != 0) transcript.AutoScrollPosition = new Point(0, Math.Max(0, -transcript.AutoScrollPosition.Y + delta));
        };
        menu.Opening += (_, _) =>
        {
            bool selectedFile = list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is AiHit { IsDirectory: false };
            foreach (ToolStripItem item in menu.Items)
                item.Enabled = list.SelectedItems.Count == 1 && (item.Tag is not AiHitAction.SearchContents || selectedFile);
        };
        list.ContextMenuStrip = menu;
        list.Disposed += (_, _) => { menu.Dispose(); rowImages.Dispose(); nameFont.Dispose(); };
        var group = new FileResultGroup(list, actions)
        {
            AccessibleName = text.T(indexed ? "Indexed file results" : "Document sources")
        };
        group.Heading.Font = nameFont;
        group.Heading.Text = text.T(indexed ? "{0} files found" : unverified ? "Unverified answer · {0} files" : "Sources · {0} files", hits.Count);
        group.Hint.Text = text.T(unverified ? "Check the excerpts below." : "Click to select · double-click to open");
        list.AccessibleDescription = text.T("Click a file to select it. Double-click to open it here.");
        if (sources is not null)
        {
            foreach (var source in sources)
            {
                var body = new TranscriptTextBox
                {
                    Text = $"[{source.Id}] {Path.GetFileName(source.FullPath)} · {source.Locator}{Environment.NewLine}{source.Quote}",
                    AccessibleName = text.T("Document sources")
                };
                AddTextMenu(body);
                group.Evidence.Add(body);
                group.Controls.Add(body);
            }
        }
        sheet.Controls.Add(group);
        transcriptRows.Add((group, false));
        group.ApplyColors();
    }

    private static string AbbreviateFileName(IDeviceContext context, string name, Font font, int width)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int Measure(string value) => TextRenderer.MeasureText(context, value, font, new Size(int.MaxValue, int.MaxValue), flags).Width;
        if (Measure(name) <= width) return name;
        int[] elements = StringInfo.ParseCombiningCharacters(name);
        string best = "…";
        int low = 0, high = elements.Length - 1;
        while (low <= high)
        {
            int count = low + (high - low) / 2;
            int left = (count + 1) / 2, right = count / 2;
            int leftEnd = left == elements.Length ? name.Length : elements[left];
            int rightStart = right == 0 ? name.Length : elements[elements.Length - right];
            string candidate = string.Concat(name.AsSpan(0, leftEnd), "…", name.AsSpan(rightStart));
            if (Measure(candidate) <= width) { best = candidate; low = count + 1; }
            else high = count - 1;
        }
        return best;
    }

    private void ArrangeTranscript()
    {
        if (arranging || transcript.ClientSize.Width <= 0) return;
        arranging = true;
        transcript.SuspendLayout();
        Point scroll = transcript.AutoScrollPosition;
        transcript.AutoScrollPosition = Point.Empty;
        try
        {
            int inset = Space(3);
            sheet.Width = Math.Max(1, transcript.ClientSize.Width
                - (transcript.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth));
            int width = Math.Max(1, sheet.Width - inset * 2);
            int y = inset;
            foreach (var row in transcriptRows)
            {
                int rowWidth = row.Right ? width * 9 / 10 : width;
                int height;
                if (row.Control is FileResultGroup group) height = group.MeasureHeight(rowWidth);
                else if (row.Control is TranscriptMessage message) height = message.MeasureHeight(rowWidth);
                else height = row.Control.GetPreferredSize(new Size(rowWidth, 0)).Height;
                row.Control.SetBounds(inset + (row.Right ? width - rowWidth : 0), y, rowWidth, height);
                y += height + Space(3);
            }
            sheet.Height = y;
            transcript.AutoScrollMinSize = new Size(0, y);
        }
        finally
        {
            transcript.ResumeLayout(performLayout: true);
            transcript.AutoScrollPosition = new Point(0, -scroll.Y);
            arranging = false;
        }
    }

    private void ShowSettings()
    {
        using var dialog = new AiSearchSettingsDialog(text, settings)
        {
            Font = Font, Probe = ListModels ?? Probe, AuthenticationStatus = AuthenticationStatus,
            LoginProvider = LoginProvider, ResolveLogin = ResolveLogin
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Cancel();
            modelListGeneration++;
            modelListCancellation?.Cancel();
            modelListCancellation = null;
            listingModels = false;
            discoveredModels.Clear();
            providerLogins.Clear();
            settings = dialog.Settings;
            SettingsChanged?.Invoke(settings);
            RefreshLanguage();
        }
        if (dialog.LoginCompleted && !IsDisposed) PendingModelList = RefreshModelsAsync(includeSubscriptions: true);
    }


    private void SaveMode()
    {
        var mode = modePicker.SelectedIndex == 1 ? AiSearchMode.ReadDocuments : AiSearchMode.FileNames;
        if (mode == settings.Mode) return;
        Cancel();
        generation++;
        modelListGeneration++;
        modelListCancellation?.Cancel();
        modelListCancellation = null;
        listingModels = false;
        settings = settings with { Mode = mode };
        SettingsChanged?.Invoke(settings);
        hintKey = "Chat kept · context separated by mode.";
        RefreshLanguage(rebuildTranscript: turns.Count == 0);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetDocumentTarget(string? path)
    {
        Cancel();
        selectedDocumentPath = path;
        DocumentTargetChanged?.Invoke(path);
        RefreshDocumentTarget();
        if (path is null) SetHint("AI chat is kept only while this window is open.");
        else ArrangeChrome();
    }

    private void RefreshDocumentTarget()
    {
        selectedDocument.Visible = selectedDocumentPath is not null;
        selectedDocument.AccessibleName = text.T("Clear selected file");
        selectedDocument.AccessibleDescription = selectedDocumentPath;
        if (selectedDocumentPath is null) return;
        selectedDocument.Text = $"{text.T("Clear selected file")} · {Path.GetFileName(selectedDocumentPath)}";
        tips.SetToolTip(selectedDocument, selectedDocumentPath);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            generation++;
            modelListGeneration++;
            modelListCancellation?.Cancel();
            if (Busy) CancelRequested?.Invoke();
            tips.Dispose();
            conversationMenu.Dispose();
            modelMenu.Dispose();
            iconFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class ResultListView : ListView
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x20A)
            {
                Control? owner = Parent;
                while (owner is not null && owner is not ScrollableControl { AutoScroll: true }) owner = owner.Parent;
                if (owner is ScrollableControl { AutoScroll: true })
                {
                    NativeSendMessage(owner.Handle, m.Msg, m.WParam, m.LParam);
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    private sealed class TranscriptTextBox : RichTextBox
    {
        private int contentHeight;
        internal TranscriptTextBox()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            ScrollBars = RichTextBoxScrollBars.None;
            WordWrap = true;
            DetectUrls = false;
            HideSelection = false;
            AutoWordSelection = false;
        }

        internal int MeasureHeight(int width)
        {
            Width = Math.Max(1, width);
            // Ask RichEdit itself: GDI Label measurement wraps CJK and long words differently.
            _ = Handle;
            NativeSendMessage(Handle, 0x441, 0, 0); // EM_REQUESTRESIZE
            return Math.Max(Font.Height, contentHeight) + (int)Math.Ceiling(4 * DeviceDpi / 96d);
        }

        protected override void OnContentsResized(ContentsResizedEventArgs e)
        {
            contentHeight = e.NewRectangle.Height;
            base.OnContentsResized(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) && SelectionLength > 0) { Copy(); return true; }
            if (keyData == (Keys.Control | Keys.A)) { SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Keep the conversation panel as the sole wheel-scroll owner.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x20A && Parent?.Parent is { } owner)
            {
                while (owner.Parent is { } parent && owner is not ScrollableControl { AutoScroll: true }) owner = parent;
                if (owner is ScrollableControl { AutoScroll: true })
                {
                    NativeSendMessage(owner.Handle, m.Msg, m.WParam, m.LParam);
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint NativeSendMessage(nint window, int message, nint wParam, nint lParam);

    private sealed class TranscriptMessage(bool isUser) : Panel
    {
        internal readonly TranscriptTextBox Body = new();
        internal bool IsUser => isUser;

        internal int MeasureHeight(int width)
        {
            if (Body.Parent is null) Controls.Add(Body);
            int horizontal = isUser ? (int)Math.Round(12 * DeviceDpi / 96d) : 0;
            int vertical = isUser ? (int)Math.Round(8 * DeviceDpi / 96d) : 0;
            int height = Body.MeasureHeight(width - horizontal * 2);
            Body.SetBounds(horizontal, vertical, Math.Max(1, width - horizontal * 2), height);
            return height + vertical * 2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (isUser) Theme.FillRounded(e.Graphics, ClientRectangle, Theme.Selected, DeviceDpi);
        }
    }

    private sealed class FileResultGroup : Panel
    {
        internal readonly Label Heading = new() { UseMnemonic = false };
        internal readonly Label Hint = new() { UseMnemonic = false };
        internal readonly ListView List;
        internal readonly FlowLayoutPanel Actions;
        internal readonly List<TranscriptTextBox> Evidence = [];

        internal FileResultGroup(ListView list, FlowLayoutPanel actions)
        {
            DoubleBuffered = true;
            List = list;
            Actions = actions;
            Controls.AddRange([Heading, Hint, List, Actions]);
        }

        internal void ApplyColors()
        {
            BackColor = Theme.Surface;
            Heading.BackColor = Hint.BackColor = Actions.BackColor = Theme.Canvas;
            List.BackColor = Theme.Surface;
            Heading.ForeColor = Theme.Text;
            Hint.ForeColor = Theme.Muted;
            foreach (var body in Evidence)
            {
                body.BackColor = Theme.Canvas;
                body.ForeColor = Theme.Muted;
            }
            Invalidate(true);
        }

        internal int MeasureHeight(int width)
        {
            int inset = (int)Math.Round(12 * DeviceDpi / 96d), gap = (int)Math.Round(8 * DeviceDpi / 96d);
            int inner = Math.Max(1, width - inset * 2);
            Heading.SetBounds(inset, inset, inner, Heading.GetPreferredSize(new Size(inner, 0)).Height);
            Hint.SetBounds(inset, Heading.Bottom + gap / 2, inner, Hint.GetPreferredSize(new Size(inner, 0)).Height);
            int y = Hint.Bottom + gap;
            List.Columns[0].Width = inner;
            List.Columns[1].Width = 0;
            List.Columns[2].Width = 0;
            _ = List.Handle;
            Rectangle first = List.Items.Count > 0 ? List.Items[0].Bounds : Rectangle.Empty;
            int rowHeight = Math.Max(first.Height, Font.Height * 2);
            List.SetBounds(inset, y, inner, Math.Max(1, first.Top + rowHeight * List.Items.Count + 1));
            Actions.SetBounds(inset, List.Bottom + gap, inner, 0);
            foreach (Control button in Actions.Controls)
            {
                var preferred = button.GetPreferredSize(Size.Empty);
                button.Size = new Size(Math.Min(inner, preferred.Width), preferred.Height);
            }
            Actions.PerformLayout();
            Actions.Height = Actions.Controls.Cast<Control>().Select(button => button.Bottom + button.Margin.Bottom).DefaultIfEmpty(0).Max();
            y = Actions.Bottom;
            foreach (var body in Evidence)
            {
                y += gap;
                body.SetBounds(inset, y, inner, body.MeasureHeight(inner));
                y = body.Bottom;
            }
            return y + inset;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.FillRounded(e.Graphics, ClientRectangle, Theme.Border, DeviceDpi);
            int edge = Math.Max(1, (int)Math.Round(DeviceDpi / 96d));
            var inside = ClientRectangle;
            inside.Inflate(-edge, -edge);
            Theme.FillRounded(e.Graphics, inside, Theme.Canvas, DeviceDpi);
        }
    }

    private sealed class ComposerField : Panel
    {
        internal ComposerField() => DoubleBuffered = true;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width > 1 && Height > 1)
                Theme.PaintField(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), ContainsFocus, DeviceDpi);
        }
    }

    private sealed class ComposerTextBox : TextBox
    {
        private bool composing;
        private readonly List<string> history = [];
        private int historyIndex = -1;
        private string draft = "";
        private int draftCaret;

        internal void RememberSentMessage(string message)
        {
            if (history.Count == 0 || history[^1] != message) history.Add(message);
            if (history.Count > 50) history.RemoveAt(0);
            historyIndex = -1;
            draft = "";
        }

        internal void ResetHistory()
        {
            // Reset conversation context and recall together, but never discard a pre-recall draft.
            if (historyIndex >= 0 && Text == history[historyIndex])
            {
                Text = draft;
                SelectionStart = draftCaret;
            }
            history.Clear();
            historyIndex = -1;
            draft = "";
        }

        private bool Recall(Keys keyData)
        {
            if (composing || SelectionLength != 0 || history.Count == 0) return false;
            bool up = keyData == Keys.Up;
            if (!up && keyData != Keys.Down) return false;
            int caret = SelectionStart;
            if (up ? Text.LastIndexOf('\n', Math.Max(0, caret - 1), caret) >= 0
                : Text.IndexOf('\n', caret) >= 0) return false;
            // A long logical line may wrap; arrows still edit until the visible text boundary.
            if (up ? GetLineFromCharIndex(caret) > 0
                : GetLineFromCharIndex(caret) < GetLineFromCharIndex(TextLength)) return false;
            if (up && historyIndex == 0) return true;
            if (!up && historyIndex < 0) return false;
            if (historyIndex < 0)
            {
                draft = Text;
                draftCaret = caret;
                historyIndex = history.Count;
            }
            historyIndex += up ? -1 : 1;
            if (historyIndex == history.Count)
            {
                Text = draft;
                SelectionStart = draftCaret;
                historyIndex = -1;
            }
            else
            {
                Text = history[historyIndex];
                SelectionStart = up ? 0 : TextLength;
            }
            return true;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Action? SendRequested { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Action? CancelRequested { get; set; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Recall(keyData)) return true;
            if (keyData == Keys.Enter && !composing) { SendRequested?.Invoke(); return true; }
            if (keyData == Keys.Escape) { CancelRequested?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x10D) composing = true; // WM_IME_STARTCOMPOSITION
            if (m.Msg == 0x10E) composing = false; // WM_IME_ENDCOMPOSITION
            base.WndProc(ref m);
        }
    }
}
