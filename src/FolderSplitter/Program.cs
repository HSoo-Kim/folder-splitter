using System.Security.Cryptography;
using System.Text;

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
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--session" && i + 1 < args.Length)
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

        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionPath.ToUpperInvariant())))[..16];
        using var mutex = new Mutex(true, @"Local\QuadExplorer-" + key, out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show(text.T("Folder Splitter is already running. Use its taskbar button to switch to it."),
                "Folder Splitter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.Run(new MainForm(sessionPath, folders));
    }
}
