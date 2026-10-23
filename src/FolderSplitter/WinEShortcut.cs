using Microsoft.Win32;

namespace QuadExplorer;

// Win+E opens the shell's "Home" folder through this per-user CLSID verb. Registering a command
// with an empty DelegateExecute under HKCU makes Windows run that command instead; removing it
// restores File Explorer. Only keys written here are removed, never the rest of the CLSID entry.
internal sealed class WinEShortcut(RegistryKey root, string executable)
{
    internal const string ClassKey = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
    private const string VerbKey = ClassKey + @"\shell\opennewwindow";
    // --toggle lets Win+E hide the window again like Win+Alt+E; the bare form is what 0.7.4–0.7.13 wrote.
    private string Command => $"\"{executable}\" --toggle";
    private string LegacyCommand => $"\"{executable}\"";

    public static WinEShortcut ForCurrentUser() =>
        new(Registry.CurrentUser, Environment.ProcessPath ?? Application.ExecutablePath);

    public bool IsEnabled
    {
        get
        {
            using var command = root.OpenSubKey(VerbKey + @"\command");
            return command?.GetValue("") is string value && (value.Equals(Command, StringComparison.OrdinalIgnoreCase)
                || value.Equals(LegacyCommand, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void UpgradeLegacy()
    {
        using (var command = root.OpenSubKey(VerbKey + @"\command"))
            if (command?.GetValue("") is not string value || !value.Equals(LegacyCommand, StringComparison.OrdinalIgnoreCase)) return;
        Enable();
    }

    public void Enable()
    {
        using var command = root.CreateSubKey(VerbKey + @"\command");
        command.SetValue("", Command);
        command.SetValue("DelegateExecute", "");
    }

    public void Disable()
    {
        root.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
        DeleteIfEmpty(ClassKey + @"\shell");
        DeleteIfEmpty(ClassKey);
    }

    private void DeleteIfEmpty(string path)
    {
        using (var key = root.OpenSubKey(path))
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0) return;
        root.DeleteSubKey(path, throwOnMissingSubKey: false);
    }
}
