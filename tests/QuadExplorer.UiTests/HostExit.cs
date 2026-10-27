using System.Runtime.InteropServices;

internal static partial class Program
{
    // The full suite creates and destroys hundreds of Shell-hosting windows. On this development PC the graphics
    // driver (amdxx64.dll) has crashed with 0xC0000409 while the process was shutting down, after every check had
    // already passed and printed, which fails the whole run. The unchanged 0.8.13 commit does the same, so the
    // host ends right after printing its verdict, with the verdict as its exit code, and skips that teardown.
    private static int QuitWithoutDriverTeardown(int exitCode)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(), (uint)exitCode);
        return exitCode;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);
}
