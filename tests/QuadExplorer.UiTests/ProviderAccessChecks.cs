using QuadExplorer;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static void RunProviderAccessScenario()
    {
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Set(theme);
            var text = new UiText("en");
            using var form = new Form();
            Exception? failure = null;
            form.Shown += async (_, _) =>
            {
                try
                {
                    const string endpoint1 = "http://127.0.0.1:11434/v1";
                    const string endpoint2 = "http://127.0.0.1:1234/v1";
                    using var menu = new AiModelMenu(text);
                    AiModelDescriptor[] choices =
                    [
                        new(AiProviderKind.LocalEndpoint, "shared", "shared one", ["default"], true) { Endpoint = endpoint1 },
                        new(AiProviderKind.LocalEndpoint, "shared", "shared two", ["default"], true) { Endpoint = endpoint2 },
                        new(AiProviderKind.CodexCli, "model", "model", ["default"], false)
                    ];
                    var current = new AiSearchSettings { Endpoint = endpoint2, Model = "shared" };
                    menu.Populate(current, choices, ["default"], false, new Dictionary<AiProviderKind, string?> { [AiProviderKind.CodexCli] = null });
                    var rows = menu.Items.OfType<ToolStripMenuItem>().Where(i => i.Tag is AiModelDescriptor).ToArray();
                    Check(rows.Count(i => i.Checked) == 1 && ((AiModelDescriptor)rows.Single(i => i.Checked).Tag!).Endpoint == endpoint2,
                        "only matching server and model is checked");
                    string? selectedEndpoint = null;
                    menu.ModelSelected += (_, _) => selectedEndpoint = menu.SelectedEndpoint;
                    rows[0].PerformClick();
                    Check(selectedEndpoint == endpoint1, "model selection retains its server identity");
                    var login = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.AccessibleName == "Login ChatGPT");
                    Check(login.Enabled && !string.IsNullOrEmpty(login.AccessibleName), "unauthenticated provider has accessible Login");
                    menu.Populate(current, choices, ["default"], false, new Dictionary<AiProviderKind, string?> { [AiProviderKind.CodexCli] = "CLI missing; set its full path in settings." });
                    login = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.AccessibleName == "Login ChatGPT");
                    Check(!login.Enabled && login.ToolTipText?.Contains("CLI missing") == true, "missing CLI disables login with actionable explanation");
                    menu.Populate(current, choices, ["default"], true, new Dictionary<AiProviderKind, string?> { [AiProviderKind.CodexCli] = null });
                    Check(!menu.Items.OfType<ToolStripMenuItem>().Single(i => i.AccessibleName == "Login ChatGPT").Enabled,
                        "login is disabled during refresh");
                    menu.Populate(current, choices, ["default"], false);
                    Check(!menu.Items.OfType<ToolStripMenuItem>().Any(i => i.AccessibleName == "Login ChatGPT"),
                        "unknown or network failure does not offer login");

                    int launched = 0, probes = 0;
                    using var dialog = new AiSearchSettingsDialog(text, new AiSearchSettings { Provider = AiProviderKind.CodexCli, Model = "model" })
                    {
                        Probe = (_, _) => { probes++; return Task.FromResult(new ProviderProbe(false, "fixture", [], TimeSpan.Zero)); },
                        AuthenticationStatus = (_, _, _) => Task.FromResult(ProviderAuthentication.Unauthenticated),
                        ResolveLogin = _ => "fixture",
                        LoginProvider = (_, _) => { launched++; return Task.CompletedTask; }
                    };
                    dialog.Show(form);
                    Check(launched == 0 && !dialog.LoginButton.Visible, "login is never automatic and initially hidden");
                    dialog.TestButton.PerformClick();
                    await dialog.PendingProbe;
                    Check(dialog.LoginButton.Visible && dialog.LoginButton.Enabled && !string.IsNullOrEmpty(dialog.LoginButton.AccessibleName),
                        "unauthenticated provider exposes accessible native Login");
                    dialog.LoginButton.PerformClick();
                    await dialog.PendingLogin;
                    Check(launched == 1 && probes == 2 && dialog.LoginCompleted, "explicit login refreshes provider catalog");
                    dialog.AuthenticationStatus = (_, _, _) => Task.FromResult(ProviderAuthentication.Unknown);
                    dialog.TestButton.PerformClick();
                    await dialog.PendingProbe;
                    Check(!dialog.LoginButton.Visible, "unknown authentication does not offer login");
                    dialog.AuthenticationStatus = (_, _, _) => Task.FromResult(ProviderAuthentication.Unauthenticated);
                    dialog.TestButton.PerformClick();
                    await dialog.PendingProbe;
                    dialog.LoginProvider = (_, token) => Task.Delay(Timeout.Infinite, token);
                    dialog.LoginButton.PerformClick();
                    int beforeCancellation = probes;
                    dialog.ProviderBox.SelectedIndex = 0;
                    await dialog.PendingLogin;
                    Check(!dialog.LoginButton.Visible && probes == beforeCancellation,
                        "changing provider cancels login without a stale refresh");
                    dialog.Close();
                }
                catch (Exception error) { failure = error; }
                finally { form.Close(); }
            };
            Application.Run(form);
            if (failure is not null) throw new InvalidOperationException($"Provider access {theme}", failure);
        }
        string executable = Path.Combine(AppContext.BaseDirectory, "QuadExplorer.UiTests.exe");
        string receipt = Path.Combine(Path.GetTempPath(), "FolderSplitterAiLogin-" + Guid.NewGuid().ToString("N"));
        string? previousReceipt = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOGIN_RECEIPT");
        try
        {
            Environment.SetEnvironmentVariable("FOLDERSPLITTER_QA_LOGIN_RECEIPT", receipt);
            foreach (var (provider, command) in new[] { (AiProviderKind.CodexCli, "login"), (AiProviderKind.ClaudeCli, "auth login") })
            {
                ProviderAccess.LoginAsync(new AiSearchSettings
                {
                    Provider = provider, CodexCliPath = executable, ClaudeCliPath = executable
                }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                Check(File.Exists(receipt) && File.ReadAllText(receipt) == command,
                    $"explicit {provider} login launches and waits for its interactive provider process");
                File.Delete(receipt);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOLDERSPLITTER_QA_LOGIN_RECEIPT", previousReceipt);
            File.Delete(receipt);
        }
    }
}
