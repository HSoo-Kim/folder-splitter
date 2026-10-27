using QuadExplorer;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static void RunAiModelMenuScenario(string? output)
    {
        if (output is not null) Directory.CreateDirectory(output);
        foreach (string theme in new[] { "light", "dark" })
        foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
        {
            Theme.Set(theme);
            var text = new UiText(language);
            using var form = new Form { ClientSize = new Size(520, 640), Font = new Font(Theme.UiFace, 10) };
            using var menu = new AiModelMenu(text) { Font = form.Font };
            AiModelDescriptor[] choices =
            [
                new(AiProviderKind.ClaudeCli, "claude-opus-5-5", "Opus 5.5", ["default", "high"], true),
                new(AiProviderKind.ClaudeCli, "claude-sonnet-5-5", "Sonnet 5.5", ["default", "high"], true),
                new(AiProviderKind.ClaudeCli, "claude-haiku-5-5", "Haiku 5.5", ["default", "high"], true),
                new(AiProviderKind.CodexCli, "gpt-6.1-sol", "GPT-6.1 Sol", ["default", "high"], true),
                new(AiProviderKind.CodexCli, "gpt-6-luna", "GPT-6 Luna", ["default", "high"], true),
                new(AiProviderKind.CodexCli, "gpt-6-astra", "GPT-6 Astra", ["default", "high"], true)
            ];
            var settings = new AiSearchSettings
            {
                Provider = AiProviderKind.ClaudeCli, Model = "claude-haiku-5-5", ReasoningEffort = "high"
            };
            Exception? failure = null;
            form.Shown += (_, _) =>
            {
                try
                {
                    menu.Populate(settings, choices, ["default", "low", "medium", "high"], false);
                    var models = menu.Items.OfType<ToolStripMenuItem>().Where(item => item.Tag is AiModelDescriptor).ToArray();
                    Check(models.Length == choices.Length && models.Count(item => item.Checked) == 1,
                        "model menu groups explicit choices and checks only the selected model");
                    (AiProviderKind Provider, string Id)? selected = null;
                    menu.ModelSelected += (provider, id) => selected = (provider, id);
                    models[3].PerformClick();
                    Check(selected == (AiProviderKind.CodexCli, "gpt-6.1-sol"),
                        "model row dispatches both provider and exact model id");
                    var efforts = menu.Items.OfType<ToolStripMenuItem>().SelectMany(item =>
                        item.DropDownItems.OfType<ToolStripMenuItem>()).Where(item => item.Tag is string).ToArray();
                    Check(efforts.Length == 4 && efforts.Single(item => item.Checked).Tag as string == "high",
                        "reasoning menu preserves the selected supported effort");
                    string? effort = null;
                    menu.EffortSelected += value => effort = value;
                    efforts.Single(item => item.Tag as string == "low").PerformClick();
                    Check(effort == "low", "reasoning selection dispatches its machine value");
                    menu.Show(form, new Point(20, 20));
                    menu.PerformLayout();
                    Console.WriteLine($"MODEL_MENU size={menu.Size} dpi={menu.DeviceDpi}");
                    foreach (ToolStripItem item in menu.Items)
                        Console.WriteLine($"MODEL_ITEM {item.AccessibleName} bounds={item.Bounds} available={item.Available}");
                    if (output is not null)
                        CaptureAiControl(menu, Path.Combine(output, $"model-menu-{theme}-{language}.png"));
                    Check(menu.Items.Cast<ToolStripItem>().Where(item => item.Available).All(item =>
                        item.Bounds.Left >= 0 && item.Bounds.Right <= menu.Width),
                        "native model menu stays within its popup width");
                    menu.Close();
                    menu.Populate(settings, choices, ["default"], true);
                    Check(menu.Items.OfType<ToolStripMenuItem>().Single(item =>
                        item.AccessibleName == "Refresh AI models").Enabled == false,
                        "loading state prevents duplicate model refresh");
                }
                catch (Exception error) { failure = error; }
                finally { form.Close(); }
            };
            Application.Run(form);
            if (failure is not null) throw new InvalidOperationException($"AI model menu {theme}/{language}", failure);
        }
    }
}
