using System.ComponentModel;
using QuadExplorer.AiSearch;

namespace QuadExplorer;

internal sealed class AiSearchSettingsDialog : Form
{
    private readonly UiText text;
    private readonly AiSearchSettings original;
    private readonly ComboBox provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox endpoint = new() { Dock = DockStyle.Fill };
    private readonly ComboBox model = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly TextBox cliPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox filenamePrompt = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
        WordWrap = true, AcceptsReturn = true, MaxLength = 2000, Height = 96
    };
    private readonly Label endpointLabel, modelLabel, cliLabel, readableFiles;
    private Font? readableFilesFont;
    private readonly Label info = new() { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false };
    private readonly Button test, save, login;
    private readonly TableLayoutPanel fields;
    private CancellationTokenSource? probeCancellation;
    private int generation;
    private bool updatingModels;
    private readonly Dictionary<AiProviderKind, string> cliPaths = [];
    private readonly Dictionary<AiProviderKind, string> models = [];
    private AiProviderKind? shownProvider;

    internal AiSearchSettingsDialog(UiText text, AiSearchSettings settings)
    {
        this.text = text;
        original = Settings = settings;
        cliPaths[AiProviderKind.ClaudeCli] = settings.ClaudeCliPath
            ?? (settings.Provider == AiProviderKind.ClaudeCli ? settings.CliPath : null) ?? "";
        cliPaths[AiProviderKind.CodexCli] = settings.CodexCliPath
            ?? (settings.Provider == AiProviderKind.CodexCli ? settings.CliPath : null) ?? "";
        models[settings.Provider] = settings.Model;
        Text = text.T("AI search settings");
        AccessibleName = Text;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(360, 420);
        Size = new Size(520, 540);
        ShowInTaskbar = false;
        MinimizeBox = false;
        fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label Caption(string key) => new()
        {
            Text = text.T(key), AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false,
            Margin = new Padding(0, 8, 0, 4)
        };
        provider.Items.AddRange([text.T("Local endpoint"), "Claude", "ChatGPT / Codex"]);
        provider.SelectedIndex = settings.Provider switch
        {
            AiProviderKind.LocalEndpoint => 0,
            AiProviderKind.ClaudeCli => 1,
            AiProviderKind.CodexCli => 2,
            _ => -1
        };
        provider.AccessibleName = text.T("Provider");
        endpoint.Text = settings.Endpoint;
        endpoint.AccessibleName = text.T("Local endpoint URL");
        model.Text = settings.Model;
        model.AccessibleName = text.T("Model");
        cliPath.Text = cliPaths.GetValueOrDefault(settings.Provider, "");
        cliPath.AccessibleName = text.T("CLI path (optional)");
        filenamePrompt.Text = settings.FilenamePrompt;
        filenamePrompt.AccessibleName = text.T("Personal AI instructions (optional)");
        fields.Controls.Add(Caption("Provider"));
        fields.Controls.Add(provider);
        endpointLabel = Caption("Local endpoint URL");
        fields.Controls.Add(endpointLabel);
        fields.Controls.Add(endpoint);
        modelLabel = Caption("Local model ID");
        fields.Controls.Add(modelLabel);
        fields.Controls.Add(model);
        cliLabel = Caption("CLI path (optional)");
        fields.Controls.Add(cliLabel);
        fields.Controls.Add(cliPath);
        fields.Controls.Add(Caption("Personal AI instructions (optional)"));
        fields.Controls.Add(filenamePrompt);
        var promptInfo = Caption("Used in both modes. Filename search sends no file contents; document reading may send relevant excerpts.");
        promptInfo.Margin = new Padding(0, 4, 0, 8);
        fields.Controls.Add(promptInfo);
        readableFiles = Caption("Readable: PDF, HWP/HWPX/HML, DOCX, PPTX, XLS/XLSX, TXT/MD/CSV/LOG · text only, no OCR.");
        readableFiles.Margin = new Padding(0, 4, 0, 8);
        fields.Controls.Add(readableFiles);
        info.Margin = new Padding(0, 12, 0, 8);
        fields.Controls.Add(info);
        test = Theme.Button(text.T("Test / list models"), "Test AI provider", () => { PendingProbe = ProbeAsync(); });
        test.Dock = DockStyle.Fill;
        test.AutoSize = true;
        fields.Controls.Add(test);
        login = Theme.Button(text.T("Login"), "Login to AI provider", () => { PendingLogin = LoginAsync(); });
        login.Dock = DockStyle.Fill;
        login.AutoSize = true;
        login.Visible = false;
        fields.Controls.Add(login);
        status.Margin = new Padding(0, 8, 0, 0);
        status.AccessibleName = text.T("Provider connection status");
        fields.Controls.Add(status);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(fields);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12), WrapContents = true
        };
        var cancel = Theme.Button(text.T("Cancel"), "Cancel AI settings", () => { DialogResult = DialogResult.Cancel; Close(); });
        save = Theme.Button(text.T("Save"), "Save AI settings", Save);
        foreach (var button in new[] { cancel, save })
        {
            button.Dock = DockStyle.None;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(12, 0, 12, 0);
            actions.Controls.Add(button);
        }
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(scroll);
        Controls.Add(actions);
        provider.SelectedIndexChanged += (_, _) => { InvalidateProbe(); UpdateProviderFields(); };
        endpoint.TextChanged += (_, _) => InvalidateProbe();
        model.TextChanged += (_, _) => InvalidateProbe();
        cliPath.TextChanged += (_, _) => InvalidateProbe();
        provider.DrawMode = DrawMode.OwnerDrawFixed;
        model.DrawMode = DrawMode.OwnerDrawFixed;
        foreach (ComboBox box in new[] { provider, model })
        {
            Theme.ThemeComboBox(box);
            box.FontChanged += (_, _) => box.ItemHeight = box.Font.Height + (int)Math.Round(8 * box.DeviceDpi / 96d);
            box.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                using var back = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selected : Theme.Surface);
                e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, e.Bounds,
                    Theme.Text, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
        }
        Theme.Apply(this);
        status.ForeColor = Theme.Muted;
        readableFiles.ForeColor = Theme.Muted;
        FontChanged += (_, _) =>
        {
            var previous = readableFilesFont;
            readableFilesFont = Font.Size > 14f ? new Font(Font.FontFamily, 14f, Font.Style) : null;
            readableFiles.Font = readableFilesFont ?? Font;
            previous?.Dispose();
        };
        Disposed += (_, _) => readableFilesFont?.Dispose();
        UpdateProviderFields();
    }

    internal AiSearchSettings Settings { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, ProviderProbe, CancellationToken, Task<ProviderAuthentication>> AuthenticationStatus { get; set; } = ProviderAccess.GetStatusAsync;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, CancellationToken, Task> LoginProvider { get; set; } = ProviderAccess.LoginAsync;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, string> ResolveLogin { get; set; } = ProviderAccess.ResolveLoginExecutable;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, CancellationToken, Task<ProviderProbe>>? Probe { get; set; }
    internal ComboBox ProviderBox => provider;
    internal TextBox EndpointBox => endpoint;
    internal ComboBox ModelBox => model;
    internal TextBox CliPathBox => cliPath;
    internal TextBox FilenamePromptBox => filenamePrompt;
    internal Label ReadableFilesInfo => readableFiles;
    internal Button TestButton => test;
    internal Button LoginButton => login;
    internal Task PendingLogin { get; private set; } = Task.CompletedTask;
    internal bool LoginCompleted { get; private set; }
    internal Button SaveButton => save;
    internal string StatusText => status.Text;
    internal Task PendingProbe { get; private set; } = Task.CompletedTask;

    private void UpdateProviderFields()
    {
        if (provider.SelectedIndex >= 0)
        {
            var selected = (AiProviderKind)provider.SelectedIndex;
            if (shownProvider != selected)
            {
                if (shownProvider is { } previous)
                {
                    models[previous] = model.Text;
                    if (previous != AiProviderKind.LocalEndpoint) cliPaths[previous] = cliPath.Text;
                }
                model.Text = models.GetValueOrDefault(selected,
                    AiModelCatalog.GetModels(selected).FirstOrDefault()?.Id ?? "");
                cliPath.Text = cliPaths.GetValueOrDefault(selected, "");
                shownProvider = selected;
            }
        }
        bool local = provider.SelectedIndex == (int)AiProviderKind.LocalEndpoint;
        endpoint.Visible = endpointLabel.Visible = local;
        model.Visible = modelLabel.Visible = local;
        cliPath.Visible = cliLabel.Visible = !local && provider.SelectedIndex >= 0;
        if (provider.SelectedIndex < 0)
        {
            info.Text = text.T("Configured provider is unsupported. Choose Local endpoint or Claude CLI.");
            info.ForeColor = Theme.ErrorText;
            return;
        }
        info.ForeColor = Theme.Muted;
        info.Text = text.T("Choose the model in the conversation. Configure its connection here.")
            + Environment.NewLine + text.T(local
            ? "Use your local OpenAI-compatible endpoint. Saving works offline; test when it is running."
            : "Filename mode sends conversation only. Document mode sends candidate paths and relevant excerpts to your selected AI.");
        if (provider.SelectedIndex == (int)AiProviderKind.CodexCli)
            info.Text += Environment.NewLine + text.T("Uses your Codex ChatGPT login. No API key or Codex tools are used.");
    }

    private bool TryRead(out AiSearchSettings settings)
    {
        if (provider.SelectedIndex < 0)
        {
            settings = original;
            status.Text = text.T("Configured provider is unsupported. Choose Local endpoint or Claude CLI.");
            status.ForeColor = Theme.ErrorText;
            provider.Focus();
            return false;
        }
        var selected = (AiProviderKind)provider.SelectedIndex;
        if (selected != AiProviderKind.LocalEndpoint) cliPaths[selected] = cliPath.Text.Trim();
        settings = original with
        {
            Provider = (AiProviderKind)provider.SelectedIndex,
            Endpoint = endpoint.Text.Trim(),
            Model = model.Text.Trim(),
            FilenamePrompt = filenamePrompt.Text,
            CliPath = null,
            ClaudeCliPath = cliPaths.GetValueOrDefault(AiProviderKind.ClaudeCli),
            CodexCliPath = cliPaths.GetValueOrDefault(AiProviderKind.CodexCli)
        };
        if (!AiModelCatalog.GetReasoningEfforts(settings.Provider, settings.Model).Contains(settings.ReasoningEffort))
            settings = settings with { ReasoningEffort = "default" };
        if (settings.Provider == AiProviderKind.LocalEndpoint &&
            (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
        {
            status.Text = text.T("Enter an http:// or https:// endpoint without credentials.");
            status.ForeColor = Theme.ErrorText;
            endpoint.Focus();
            return false;
        }
        return true;
    }

    private void Save()
    {
        if (!TryRead(out var settings)) return;
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            status.Text = text.T("Choose or enter a model. You can save without connecting.");
            status.ForeColor = Theme.ErrorText;
            model.Focus();
            return;
        }
        Settings = settings;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void InvalidateProbe()
    {
        if (updatingModels) return;
        generation++;
        probeCancellation?.Cancel();
        status.Text = "";
        login.Visible = false;
        login.Enabled = false;
        test.Enabled = true;
    }

    private async Task ProbeAsync()
    {
        if (!TryRead(out var settings)) return;
        if (Probe is not { } probe)
        {
            status.Text = text.T("Provider connection testing is unavailable.");
            status.ForeColor = Theme.ErrorText;
            return;
        }
        probeCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        probeCancellation = cancellation;
        int request = ++generation;
        test.Enabled = false;
        status.Text = text.T("Connecting and listing models…");
        status.ForeColor = Theme.Muted;
        try
        {
            ProviderProbe result = await probe(settings, cancellation.Token);
            var authentication = await AuthenticationStatus(settings, result, cancellation.Token);
            if (IsDisposed || request != generation) return;
            login.Visible = authentication == ProviderAuthentication.Unauthenticated;
            login.Enabled = login.Visible;
            string? loginProblem = null;
            if (login.Visible)
            {
                try { ResolveLogin(settings); }
                catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    login.Enabled = false;
                    loginProblem = error.Message;
                }
            }
            if (IsDisposed || request != generation) return;
            string selected = model.Text;
            updatingModels = true;
            model.Items.Clear();
            model.Items.AddRange(result.Models.Cast<object>().ToArray());
            model.Text = selected;
            updatingModels = false;
            status.Text = text.T(result.Ok ? "Connected: {0}" : "Connection failed: {0}", result.Detail)
                + (loginProblem is null ? "" : Environment.NewLine + loginProblem);
            status.ForeColor = result.Ok ? Theme.Text : Theme.ErrorText;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsDisposed || request != generation) return;
            status.Text = text.T("Connection failed: {0}", error.Message);
            status.ForeColor = Theme.ErrorText;
        }
        finally
        {
            if (!IsDisposed && request == generation) test.Enabled = true;
        }
    }

    private async Task LoginAsync()
    {
        if (!login.Visible || !login.Enabled || !TryRead(out var current)) return;
        probeCancellation?.Cancel();
        probeCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        probeCancellation = cancellation;
        int request = ++generation;
        login.Enabled = false;
        test.Enabled = false;
        status.Text = text.T("Complete login in the provider window.");
        try
        {
            await LoginProvider(current, cancellation.Token);
            if (IsDisposed || request != generation || cancellation.IsCancellationRequested) return;
            LoginCompleted = true;
            PendingProbe = ProbeAsync();
            await PendingProbe;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsDisposed || request != generation) return;
            status.Text = text.T("Login failed: {0}", error.Message);
            status.ForeColor = Theme.ErrorText;
            login.Enabled = true;
        }
        finally
        {
            if (!IsDisposed && request == generation) test.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            generation++;
            probeCancellation?.Cancel();
            probeCancellation?.Dispose();
            probeCancellation = null;
        }
        base.Dispose(disposing);
    }
}
