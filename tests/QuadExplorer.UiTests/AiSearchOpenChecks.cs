using System.IO.Pipes;
using System.Reflection;
using Microsoft.Win32;
using QuadExplorer;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    // ShellExecute launches this console-only receipt for one uniquely registered QA extension.
    // It never opens a GUI application or reads the fixture's contents.
    private static int WriteAiOpenReceipt(string pipe, string path)
    {
        if (!pipe.StartsWith("FolderSplitterAiQa-", StringComparison.Ordinal) || !File.Exists(path)) return 2;
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
        client.Connect(10000);
        using var writer = new StreamWriter(client);
        writer.WriteLine(path);
        return 0;
    }

    private static async Task CheckAiAssociatedFileOpen(AiSearchPane chat, string root)
    {
        string id = Guid.NewGuid().ToString("N");
        string extension = ".fsaiqa" + id, program = "FolderSplitter.AiQa." + id;
        string path = Path.Combine(root, "associated-file" + extension);
        File.WriteAllText(path, "owned file association fixture");
        string pipeName = "FolderSplitterAiQa-" + id;
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extension))
                key.SetValue("", program);
            using (var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + program + @"\shell\open\command"))
                command.SetValue("", $"\"{Environment.ProcessPath}\" \"{Assembly.GetExecutingAssembly().Location}\" --ai-open-receipt \"{pipeName}\" \"%1\"");
            var plan = new SearchPlan(["associated-file"], [], [], [], ItemKind.File,
                SearchScope.Everywhere, null, null, null, null, false, null);
            chat.ResetConversation();
            chat.Submit = _ => Task.FromResult<ChatTurn?>(new(ChatRole.Assistant, "",
                new AiSearchResult(plan, [new(path, false, 0, null)], 1, false, null)));
            chat.Composer.Text = "owned file association";
            await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var list = Descendants(chat).OfType<ListView>().Single();
            list.Items[0].Selected = true;
            var connected = pipe.WaitForConnectionAsync();
            Descendants(chat).OfType<Button>().Single(button => button.AccessibleName == "Open").PerformClick();
            await connected.WaitAsync(TimeSpan.FromSeconds(10));
            using var reader = new StreamReader(pipe);
            string? actual = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(actual == path, "AI Open invokes the real OS file association with the selected fixture path");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + extension, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + program, false);
        }
    }
}
