using Microsoft.Win32;

namespace QuadExplorer;

// Starts Folder Splitter when the user signs in, through the per-user Run key. --hidden puts it in the
// notification area, or minimizes it when the app does not keep running there. Turning it off in Task
// Manager's Startup apps leaves our Run value in place and marks it off under StartupApproved; that choice is
// respected until the user turns the option on again here. Only values named after the app are touched.
internal sealed class StartupRegistration(RegistryKey root, string executable, string? session)
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "Folder Splitter";
    private readonly string command = $"\"{executable}\" --hidden"
        + (session is not null && !session.Equals(SessionStore.DefaultPath, StringComparison.OrdinalIgnoreCase)
            ? $" --session \"{session}\"" : "");

    public static StartupRegistration ForCurrentUser(string sessionPath) =>
        new(Registry.CurrentUser, Environment.ProcessPath ?? "", sessionPath);

    public bool IsSupported => executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled
    {
        get
        {
            using var run = root.OpenSubKey(RunKey);
            return run?.GetValue(ValueName) is string value && value.Equals(command, StringComparison.OrdinalIgnoreCase)
                && !IsDisabledByWindows();
        }
    }

    public void Enable()
    {
        using (var run = root.CreateSubKey(RunKey)) run.SetValue(ValueName, command);
        if (IsDisabledByWindows())
        {
            using var approval = root.OpenSubKey(ApprovalKey, writable: true);
            approval?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public void Disable()
    {
        using (var run = root.OpenSubKey(RunKey, writable: true))
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        using var approval = root.OpenSubKey(ApprovalKey, writable: true);
        approval?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // Launching does not move the registration to another copy that still exists, such as the temporary copy
    // Windows makes when the app is opened from inside a ZIP. It only fills a missing value, refreshes this
    // copy's own command, or replaces one whose executable is gone (the app was moved).
    public void EnsureRegistered()
    {
        using (var run = root.OpenSubKey(RunKey))
            if (run?.GetValue(ValueName) is string value && (value.Equals(command, StringComparison.OrdinalIgnoreCase)
                || RegisteredExecutable(value) is { } other && !other.Equals(executable, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(other)))
                return;
        using var registration = root.CreateSubKey(RunKey);
        registration.SetValue(ValueName, command);
    }

    private static string? RegisteredExecutable(string value) =>
        value.StartsWith('"') && value.IndexOf('"', 1) is > 1 and var end ? value[1..end] : null;

    private bool IsDisabledByWindows()
    {
        using var approval = root.OpenSubKey(ApprovalKey);
        // Windows marks Task Manager-disabled startup entries with an odd first byte.
        return approval?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) != 0;
    }
}
