using QuadExplorer.AiSearch;

namespace QuadExplorer;

internal sealed partial class AiSearchPane
{
    private readonly List<AiModelDescriptor> discoveredModels = [];
    internal IReadOnlyList<AiModelDescriptor> DiscoveredModels => discoveredModels.AsReadOnly();
    internal IReadOnlyList<string> ReasoningChoices =>
        discoveredModels.FirstOrDefault(model => model.Provider == settings.Provider && model.Id == settings.Model
            && model.Available)?.ReasoningEfforts ?? AiModelCatalog.GetReasoningEfforts(settings.Provider, settings.Model);

    private void ShowModelMenu()
    {
        RefreshModelButton();
        modelMenu.Font = Font;
        modelMenu.Show(modelButton, new Point(modelButton.Width, 0), ToolStripDropDownDirection.AboveLeft);
        if (!listingModels) PendingModelList = RefreshModelsAsync(includeSubscriptions: true);
    }

    private void RefreshModelButton()
    {
        string name = AiModelCatalog.DisplayName(settings.Provider, settings.Model);
        if (name.StartsWith("Claude ", StringComparison.Ordinal)) name = name[7..];
        if (name.Length == 0) name = text.T("Model");
        string effort = settings.ReasoningEffort switch
        {
            "low" => "Low", "medium" => "Medium", "high" => "High",
            "xhigh" => "Very high", "max" => "Maximum", _ => "Default"
        };
        modelButton.Text = $"{name} · {text.T(effort)}  \u2304";
        modelButton.Enabled = !Busy;
        var choices = AiModelCatalog.RequestedModels.Select(preferred =>
            discoveredModels.FirstOrDefault(actual => actual.Provider == preferred.Provider && actual.Id == preferred.Id)
                ?? preferred).ToList();
        choices.AddRange(discoveredModels.Where(model => model.Provider == AiProviderKind.LocalEndpoint));
        if (settings.Model.Length > 0 && !choices.Any(model => model.Provider == settings.Provider && model.Id == settings.Model))
            choices.Add(discoveredModels.FirstOrDefault(model => model.Provider == settings.Provider && model.Id == settings.Model)
                ?? new(settings.Provider, settings.Model, settings.Model, ReasoningChoices));
        modelMenu.Populate(settings, choices, ReasoningChoices, listingModels);
        UpdateContextMeter();
    }

    internal void SelectModel(AiProviderKind provider, string id)
    {
        Cancel();
        if (provider != settings.Provider)
        {
            modelListGeneration++;
            modelListCancellation?.Cancel();
            modelListCancellation = null;
            listingModels = false;
        }
        var next = settings with
        {
            Provider = provider, Model = id,
            ClaudeCliPath = settings.ClaudeCliPath
                ?? (settings.Provider == AiProviderKind.ClaudeCli ? settings.CliPath : null),
            CodexCliPath = settings.CodexCliPath
                ?? (settings.Provider == AiProviderKind.CodexCli ? settings.CliPath : null),
            CliPath = null
        };
        var efforts = discoveredModels.FirstOrDefault(model => model.Provider == provider && model.Id == id
            && model.Available)?.ReasoningEfforts ?? AiModelCatalog.GetReasoningEfforts(provider, id);
        if (!efforts.Contains(next.ReasoningEffort)) next = next with { ReasoningEffort = "default" };
        settings = AiSearchSettingsStore.Normalize(next);
        SettingsChanged?.Invoke(settings);
        RefreshModelButton();
        ArrangeChrome();
    }

    internal void SelectEffort(string effort)
    {
        if (!ReasoningChoices.Contains(effort)) return;
        settings = AiSearchSettingsStore.Normalize(settings with { ReasoningEffort = effort });
        SettingsChanged?.Invoke(settings);
        RefreshModelButton();
        ArrangeChrome();
    }

    internal async Task RefreshModelsAsync(bool includeSubscriptions = false)
    {
        if (ListModels is not { } listModels || Busy) return;
        modelListCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        modelListCancellation = cancellation;
        int request = ++modelListGeneration;
        listingModels = true;
        SetHint("Loading available models…");
        RefreshModelButton();
        var captured = settings;
        var providers = includeSubscriptions
            ? new[] { AiProviderKind.ClaudeCli, AiProviderKind.CodexCli, settings.Provider }.Distinct().ToArray()
            : [settings.Provider];
        try
        {
            var results = await Task.WhenAll(providers.Select(async provider =>
                (Provider: provider, Result: await listModels(captured with
                {
                    Provider = provider, Model = provider == captured.Provider ? captured.Model : "",
                    CliPath = provider == captured.Provider ? captured.CliPath : null,
                    ReasoningEffort = "default"
                }, cancellation.Token))));
            if (IsDisposed || request != modelListGeneration || cancellation.IsCancellationRequested) return;
            foreach (var (provider, result) in results)
            {
                discoveredModels.RemoveAll(model => model.Provider == provider);
                var descriptors = result.ModelDescriptors.Count > 0 ? result.ModelDescriptors
                    : result.Models.Select(id => new AiModelDescriptor(provider, id,
                        AiModelCatalog.DisplayName(provider, id), AiModelCatalog.GetReasoningEfforts(provider, id), result.Ok)).ToArray();
                discoveredModels.AddRange(descriptors);
            }
            bool any = results.Any(result => result.Result.Ok && result.Result.Models.Count > 0);
            SetHint(any ? "Models loaded." : "Could not list models. Enter a model ID or open settings.");
            if (!any) tips.SetToolTip(modelButton, string.Join("\n", results.Select(result => result.Result.Detail)));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsDisposed || request != modelListGeneration) return;
            SetHint("Could not list models. Enter a model ID or open settings.");
            tips.SetToolTip(modelButton, error.Message);
        }
        finally
        {
            if (!IsDisposed && request == modelListGeneration)
            {
                listingModels = false;
                modelListCancellation = null;
                RefreshModelButton();
                ArrangeChrome();
            }
            cancellation.Dispose();
        }
    }
}
