using QuadExplorer.AiSearch;

namespace QuadExplorer;

/// <summary>A single anchored picker, with provider groups and native keyboard navigation.</summary>
internal sealed class AiModelMenu : ContextMenuStrip
{
    private readonly UiText text;
    internal event Action<AiProviderKind, string>? ModelSelected;
    internal event Action<string>? EffortSelected;
    internal event EventHandler? SettingsRequested;
    internal event EventHandler? RefreshRequested;
    internal event Action<AiProviderKind>? LoginRequested;
    internal string? SelectedEndpoint { get; private set; }

    internal AiModelMenu(UiText text)
    {
        this.text = text;
        ShowImageMargin = false;
        ShowCheckMargin = true;
        AccessibleName = text.T("Model");
        Padding = new Padding(6);
    }

    internal void Populate(AiSearchSettings settings, IReadOnlyList<AiModelDescriptor> choices,
        IReadOnlyList<string> efforts, bool loading, IReadOnlyDictionary<AiProviderKind, string?>? logins = null)
    {
        foreach (ToolStripItem item in Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        Items.Clear();
        int pad = Math.Max(4, (int)Math.Round(6 * DeviceDpi / 96d));
        foreach (var provider in choices.GroupBy(choice => choice.Provider))
        {
            if (Items.Count > 0) Items.Add(new ToolStripSeparator());
            string label = provider.Key switch
            {
                AiProviderKind.ClaudeCli => "Claude",
                AiProviderKind.CodexCli => "ChatGPT",
                _ => text.T("Local endpoint")
            };
            Items.Add(new ToolStripLabel(label)
            {
                ForeColor = Theme.Muted, Padding = new Padding(pad, pad, pad, pad / 2),
                AccessibleName = label
            });
            if (logins is not null && logins.TryGetValue(provider.Key, out var loginProblem))
            {
                var login = new ToolStripMenuItem($"{text.T("Login")} · {label}"
                    + (loginProblem is null ? "" : $" · {text.T("CLI not found; open settings")}"))
                {
                    Enabled = !loading && loginProblem is null,
                    AccessibleName = $"{text.T("Login")} {label}",
                    AccessibleDescription = loginProblem ?? text.T("Open the provider's interactive login window."),
                    ToolTipText = loginProblem ?? text.T("Open the provider's interactive login window."),
                    Padding = new Padding(0, pad, 0, pad)
                };
                login.Click += (_, _) => { Close(); LoginRequested?.Invoke(provider.Key); };
                Items.Add(login);
            }
            foreach (var choice in provider)
            {
                var item = new ToolStripMenuItem(choice.DisplayName)
                {
                    Checked = choice.Provider == settings.Provider && choice.Id == settings.Model
                        && (choice.Provider != AiProviderKind.LocalEndpoint
                            || string.Equals(choice.Endpoint, settings.Endpoint, StringComparison.OrdinalIgnoreCase)),
                    Enabled = choice.Available,
                    Padding = new Padding(0, pad, 0, pad),
                    ToolTipText = choice.Available ? choice.Endpoint ?? choice.Id : choice.UnavailableReason ?? choice.Id,
                    AccessibleName = $"Model {choice.Provider}:{choice.Id} {choice.Endpoint}",
                    Tag = choice
                };
                item.Click += (_, _) =>
                {
                    Close();
                    SelectedEndpoint = choice.Endpoint;
                    try { ModelSelected?.Invoke(choice.Provider, choice.Id); }
                    finally { SelectedEndpoint = null; }
                };
                Items.Add(item);
            }
        }
        if (Items.Count > 0) Items.Add(new ToolStripSeparator());
        var reasoning = new ToolStripMenuItem(text.T("Reasoning effort"))
        {
            Enabled = efforts.Count > 1, Padding = new Padding(0, pad, 0, pad),
            AccessibleName = text.T("Reasoning effort")
        };
        foreach (string effort in efforts)
        {
            string caption = effort switch
            {
                "low" => text.T("Low"), "medium" => text.T("Medium"),
                "high" => text.T("High"), "xhigh" => text.T("Very high"),
                "max" => text.T("Maximum"), _ => text.T("Default")
            };
            var item = new ToolStripMenuItem(caption)
            {
                Checked = settings.ReasoningEffort == effort, Tag = effort,
                AccessibleName = $"Reasoning {effort}", Padding = new Padding(0, pad, 0, pad)
            };
            item.Click += (_, _) => { Close(); EffortSelected?.Invoke(effort); };
            reasoning.DropDownItems.Add(item);
        }
        Items.Add(reasoning);
        Items.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem(text.T(loading ? "Loading available models…" : "Test / list models"))
        {
            Enabled = !loading, Padding = new Padding(0, pad, 0, pad), AccessibleName = "Refresh AI models"
        };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        Items.Add(refresh);
        var configure = new ToolStripMenuItem(text.T("AI search settings"))
        {
            Padding = new Padding(0, pad, 0, pad), AccessibleName = "Configure AI models"
        };
        configure.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Items.Add(configure);
        Theme.Apply(this);
    }
}
