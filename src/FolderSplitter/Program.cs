using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using QuadExplorer.Search;

namespace QuadExplorer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string sessionPath = SessionStore.DefaultPath;
        var text = new UiText(SessionStore.Load(sessionPath).Language);
        var folders = new List<string>();
        bool toggle = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--toggle")
                toggle = true;
            else if (args[i] == "--wait-pid" && i + 1 < args.Length)
                WaitForExit(args[++i]);
            else if (args[i] == "--session" && i + 1 < args.Length)
            {
                sessionPath = Path.GetFullPath(args[++i]);
                text.SetLanguage(SessionStore.Load(sessionPath).Language);
            }
            else if (Directory.Exists(args[i]))
                folders.Add(Path.GetFullPath(args[i]));
            else
            {
                MessageBox.Show(text.T("Not a folder or recognized argument:\n{0}", args[i]),
                    "Folder Splitter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        string key = InstanceKey(sessionPath);
        using var mutex = new Mutex(true, @"Local\QuadExplorer-" + key, out bool firstInstance);
        if (!firstInstance)
        {
            // A second launch (for example Win+E) hands the foreground to the running window instead.
            AllowSetForegroundWindow(-1);
            PostMessage(0xFFFF, ActivationMessage(sessionPath), toggle ? 1 : 0, 0);
            return;
        }
        UpdateInstaller.RemovePrevious(Environment.ProcessPath);
        var roots = DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName).ToArray();
        string storePath = sessionPath.Equals(SessionStore.DefaultPath, StringComparison.OrdinalIgnoreCase)
            ? IndependentSearchBackend.DefaultStorePath : Path.ChangeExtension(sessionPath, ".search.index");
        var search = new IndependentSearchBackend(roots, storePath);
        _ = search.BuildOrLoadAsync().ContinueWith(task => System.Diagnostics.Debug.WriteLine(task.Exception),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        Application.Run(new MainForm(sessionPath, folders, search, latestRelease: UpdateChecker.FetchLatestAsync));
    }

    // After an update the new executable starts before the old one has exited.
    private static void WaitForExit(string processId)
    {
        if (!int.TryParse(processId, out int id)) return;
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(id);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException) { }
    }

    internal static string InstanceKey(string sessionPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionPath.ToUpperInvariant())))[..16];

    internal static uint ActivationMessage(string sessionPath) =>
        RegisterWindowMessage("FolderSplitter.Activate." + InstanceKey(sessionPath));

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int process);
}
