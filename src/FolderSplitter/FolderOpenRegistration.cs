using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuadExplorer;

// Windows' Default apps settings have no entry for folders. File managers instead add a verb to the
// per-user Directory and Drive classes and, to take over double-clicks, make it those classes' default.
// Only what is written here is removed; a default another program had set is put back when ours goes.
internal sealed class FolderOpenRegistration(RegistryKey root, string executable, bool notifyShell = false)
{
    internal const string Verb = "FolderSplitter";
    private const string PreviousDefault = "FolderSplitterPreviousDefault";
    private static readonly string[] Kinds = ["Directory", "Drive"];

    public static FolderOpenRegistration ForCurrentUser() =>
        new(Registry.CurrentUser, Environment.ProcessPath ?? Application.ExecutablePath, notifyShell: true);

    internal static string ShellKey(string kind) => $@"Software\Classes\{kind}\shell";
    internal static string VerbKey(string kind) => ShellKey(kind) + @"\" + Verb;

    // A drive root ends in a backslash, and a quoted "C:\" reads as an escaped quote; drive roots have no spaces.
    internal string Command(string kind) => kind == "Drive" ? $"\"{executable}\" %1" : $"\"{executable}\" \"%1\"";

    public bool MenuEnabled => Kinds.All(kind =>
    {
        using var command = root.OpenSubKey(VerbKey(kind) + @"\command");
        return command?.GetValue("") is string value && value.Equals(Command(kind), StringComparison.OrdinalIgnoreCase);
    });

    public bool DefaultEnabled => MenuEnabled && Kinds.All(kind =>
    {
        using var shell = root.OpenSubKey(ShellKey(kind));
        return shell?.GetValue("") is string verb && verb.Equals(Verb, StringComparison.OrdinalIgnoreCase);
    });

    // Opening folders by default runs the menu verb, so the verb stays while the default is on.
    public void Apply(bool menu, bool openByDefault, string label)
    {
        foreach (string kind in Kinds)
        {
            if (!menu)
            {
                RemoveDefault(kind);
                root.DeleteSubKeyTree(VerbKey(kind), throwOnMissingSubKey: false);
                DeleteIfEmpty(ShellKey(kind));
                DeleteIfEmpty($@"Software\Classes\{kind}");
                continue;
            }
            using (var verb = root.CreateSubKey(VerbKey(kind)))
            {
                verb.SetValue("MUIVerb", label);
                verb.SetValue("Icon", $"\"{executable}\",0");
            }
            using (var command = root.CreateSubKey(VerbKey(kind) + @"\command")) command.SetValue("", Command(kind));
            if (openByDefault) SetDefault(kind);
            else RemoveDefault(kind);
        }
        if (notifyShell) SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED
    }

    public void UpdateLabel(string label)
    {
        if (!MenuEnabled) return;
        foreach (string kind in Kinds)
        {
            using var verb = root.OpenSubKey(VerbKey(kind), writable: true);
            verb?.SetValue("MUIVerb", label);
        }
    }

    private void SetDefault(string kind)
    {
        using var shell = root.CreateSubKey(ShellKey(kind));
        using var verb = root.CreateSubKey(VerbKey(kind));
        if (shell.GetValue("") is not string current || current.Length == 0) verb.DeleteValue(PreviousDefault, throwOnMissingValue: false);
        else if (!current.Equals(Verb, StringComparison.OrdinalIgnoreCase)) verb.SetValue(PreviousDefault, current);
        shell.SetValue("", Verb);
    }

    private void RemoveDefault(string kind)
    {
        using var verb = root.OpenSubKey(VerbKey(kind), writable: true);
        using (var shell = root.OpenSubKey(ShellKey(kind), writable: true))
        {
            if (shell?.GetValue("") is string current && current.Equals(Verb, StringComparison.OrdinalIgnoreCase))
            {
                if (verb?.GetValue(PreviousDefault) is string previous) shell.SetValue("", previous);
                else shell.DeleteValue("", throwOnMissingValue: false);
            }
        }
        verb?.DeleteValue(PreviousDefault, throwOnMissingValue: false);
    }

    private void DeleteIfEmpty(string path)
    {
        using (var key = root.OpenSubKey(path))
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0) return;
        root.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
