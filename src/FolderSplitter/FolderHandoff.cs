using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuadExplorer;

// A second launch with folders, such as a folder double-clicked while Folder Splitter runs, hands them to the
// running instance. WM_COPYDATA goes to its activation window, whose title names the session; a broadcast
// cannot carry the paths. Only folders that exist are opened.
internal static class FolderHandoff
{
    internal const int CopyDataMessage = 0x004A;
    private const nint Signature = 0x46534F46; // "FOSF"
    private const int MaxCharacters = 32 * 1024;

    internal static string ReceiverTitle(string sessionPath) => "Folder Splitter activation " + Program.InstanceKey(sessionPath);

    internal static bool TrySend(string sessionPath, IReadOnlyList<string> folders, TimeSpan wait)
    {
        string payload = string.Join('\n', folders);
        if (payload.Length == 0 || payload.Length > MaxCharacters) return false;
        // The first instance holds the session mutex a moment before its window exists.
        var clock = Stopwatch.StartNew();
        nint receiver;
        while ((receiver = FindWindowW(null, ReceiverTitle(sessionPath))) == 0)
        {
            if (clock.Elapsed >= wait) return false;
            Thread.Sleep(100);
        }
        nint text = Marshal.StringToHGlobalUni(payload);
        try
        {
            var data = new CopyData { Data = Signature, Size = payload.Length * 2, Pointer = text };
            return SendMessageTimeoutW(receiver, CopyDataMessage, 0, ref data, 0x0002, 10_000, out nint result) != 0 // SMTO_ABORTIFHUNG
                && result == 1;
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    internal static IReadOnlyList<string>? Read(nint copyData)
    {
        if (copyData == 0) return null;
        var data = Marshal.PtrToStructure<CopyData>(copyData);
        if (data.Data != Signature || data.Pointer == 0 || data.Size <= 0 || data.Size % 2 != 0 || data.Size / 2 > MaxCharacters)
            return null;
        return Marshal.PtrToStringUni(data.Pointer, data.Size / 2)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.IsPathFullyQualified(path) && Directory.Exists(path))
            .Select(Path.GetFullPath).ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public nint Data;
        public int Size;
        public nint Pointer;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string? className, string windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SendMessageTimeoutW(nint window, int message, nint wParam, ref CopyData data,
        uint flags, uint timeout, out nint result);
}
