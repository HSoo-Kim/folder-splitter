using System.ComponentModel;
using QuadExplorer.AiSearch;

namespace QuadExplorer;

internal sealed partial class AiSearchPane
{
    private readonly List<AiModelDescriptor> discoveredModels = [];
    private readonly Dictionary<AiProviderKind, string?> providerLogins = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, ProviderProbe, CancellationToken, Task<ProviderAuthentication>> AuthenticationStatus { get; set; } = ProviderAccess.GetStatusAsync;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, string> ResolveLogin { get; set; } = ProviderAccess.ResolveLoginExecutable;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AiSearchSettings, CancellationToken, Task> LoginProvider { get; set; } = ProviderAccess.LoginAsync;
    internal IReadOnlyList<AiModelDescriptor> DiscoveredModels => discoveredModels.AsReadOnly();
    internal IReadOnlyList<string> ReasoningChoices =>
        discoveredModels.FirstOrDefault(model => model.Provider == settings.Provider && model.Id == settings.Model
            && (model.Provider != AiProviderKind.LocalEndpoint || model.Endpoint == settings.Endpoint)
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
        if (settings.Model.Length > 0 && !choices.Any(model => model.Provider == settings.Provider && model.Id == settings.Model
            && (model.Provider != AiProviderKind.LocalEndpoint || model.Endpoint == settings.Endpoint)))
            choices.Add(new AiModelDescriptor(settings.Provider, settings.Model, settings.Model, ReasoningChoices)
                { Endpoint = settings.Provider == AiProviderKind.LocalEndpoint ? settings.Endpoint : null });
        modelMenu.LoginRequested -= StartProviderLogin;
        modelMenu.LoginRequested += StartProviderLogin;
        modelMenu.Populate(settings, choices, ReasoningChoices, listingModels, providerLogins);
        UpdateContextMeter();
    }

    internal void SelectModel(AiProviderKind provider, string id)
    {
        Cancel();
        if (provider != settings.Provider || provider == AiProviderKind.LocalEndpoint
            && modelMenu.SelectedEndpoint is { } endpoint && endpoint != settings.Endpoint)
        {
            modelListGeneration++;
            modelListCancellation?.Cancel();
            modelListCancellation = null;
            listingModels = false;
        }
        var next = settings with
        {
            Provider = provider, Model = id,
            Endpoint = provider == AiProviderKind.LocalEndpoint ? modelMenu.SelectedEndpoint ?? settings.Endpoint : settings.Endpoint,
            ClaudeCliPath = settings.ClaudeCliPath
                ?? (settings.Provider == AiProviderKind.ClaudeCli ? settings.CliPath : null),
            CodexCliPath = settings.CodexCliPath
                ?? (settings.Provider == AiProviderKind.CodexCli ? settings.CliPath : null),
            CliPath = null
        };
        var efforts = discoveredModels.FirstOrDefault(model => model.Provider == provider && model.Id == id
            && (provider != AiProviderKind.LocalEndpoint || model.Endpoint == next.Endpoint)
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
            ? new[] { AiProviderKind.ClaudeCli, AiProviderKind.CodexCli, AiProviderKind.LocalEndpoint }.Distinct().ToArray()
            : [settings.Provider];
        try
        {
            var results = await Task.WhenAll(providers.Select(async provider =>
            {
                var providerSettings = captured with
                {
                    Provider = provider, Model = provider == captured.Provider ? captured.Model : "",
                    CliPath = provider == captured.Provider ? captured.CliPath : null,
                    ReasoningEffort = "default"
                };
                var result = provider == AiProviderKind.LocalEndpoint
                    ? await LocalModelDiscovery.DiscoverAsync(providerSettings, listModels, cancellation.Token)
                    : await listModels(providerSettings, cancellation.Token);
                var auth = await AuthenticationStatus(providerSettings, result, cancellation.Token);
                string? problem = null;
                if (auth == ProviderAuthentication.Unauthenticated)
                {
                    try { ResolveLogin(providerSettings); }
                    catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
                    { problem = error.Message; }
                }
                return (Provider: provider, Result: result, Authentication: auth, LoginProblem: problem);
            }));
            if (IsDisposed || request != modelListGeneration || cancellation.IsCancellationRequested) return;
            foreach (var (provider, result, authentication, loginProblem) in results)
            {
                providerLogins.Remove(provider);
                if (authentication == ProviderAuthentication.Unauthenticated) providerLogins[provider] = loginProblem;
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

    private void StartProviderLogin(AiProviderKind provider) => PendingModelList = LoginFromMenuAsync(provider);

    private async Task LoginFromMenuAsync(AiProviderKind provider)
    {
        if (Busy || listingModels || !providerLogins.TryGetValue(provider, out var problem) || problem is not null) return;
        modelListCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        modelListCancellation = cancellation;
        int request = ++modelListGeneration;
        listingModels = true;
        RefreshModelButton();
        SetHint("Complete login in the provider window.");
        try
        {
            var current = settings with
            {
                Provider = provider,
                CliPath = provider == settings.Provider ? settings.CliPath : null
            };
            await LoginProvider(current, cancellation.Token);
            if (IsDisposed || request != modelListGeneration || cancellation.IsCancellationRequested) return;
            providerLogins.Remove(provider);
            listingModels = false;
            await RefreshModelsAsync(includeSubscriptions: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsDisposed || request != modelListGeneration) return;
            SetHint("Login failed. Check the provider CLI and try again.");
            tips.SetToolTip(modelButton, error.Message);
        }
        finally
        {
            if (!IsDisposed && request == modelListGeneration)
            {
                listingModels = false;
                modelListCancellation = null;
                RefreshModelButton();
            }
            cancellation.Dispose();
        }
    }
}
