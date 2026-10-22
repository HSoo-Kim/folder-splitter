using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PrivateDesktopQa;

internal static class Program
{
    private const int LauncherFailure = 125;
    private const uint TimeoutExitCode = 124;
    private const uint CleanupTimeoutMilliseconds = 5_000;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = uint.MaxValue;
    private const uint DesktopCreateWindow = 0x0002;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint StartfForceOffFeedback = 0x00000080;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint CreateNew = 1;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private static readonly nint InvalidHandle = new(-1);

    private static int Main(string[] args)
    {
        int separator = Array.IndexOf(args, "--");
        string? logArgument = null;
        uint timeoutMilliseconds = 0;
        for (int i = 0; i < separator; i += 2)
        {
            if (i + 1 >= separator)
                return Usage();
            if (args[i] == "--log" && logArgument is null)
                logArgument = args[i + 1];
            else if (args[i] == "--timeout" && timeoutMilliseconds == 0 &&
                uint.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out uint seconds) &&
                seconds > 0 && seconds <= (uint.MaxValue - 1) / 1000)
                timeoutMilliseconds = seconds * 1000;
            else
                return Usage();
        }

        if (separator < 0 || separator + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(logArgument) || timeoutMilliseconds == 0)
            return Usage();

        nint desktop = 0, job = 0, log = 0, input = 0;
        nint attributeList = 0, inheritedHandles = 0;
        bool attributesInitialized = false, assigned = false, exited = false;
        bool timedOut = false, cleanupOk = true;
        ProcessInformation process = default;
        int result = LauncherFailure;
        string desktopName = $"FolderSplitter-QA-{Guid.NewGuid():N}";

        try
        {
            string logPath = Path.GetFullPath(logArgument);
            string executable = Path.GetFullPath(args[separator + 1]);
            if (!File.Exists(executable))
                throw new FileNotFoundException("Executable must be an existing file.", executable);
            string commandLine = string.Join(" ", args.Skip(separator + 2).Prepend(executable).Select(QuoteArgument));
            if (commandLine.Length >= 32_767)
                throw new ArgumentException("Windows command line exceeds 32,766 UTF-16 characters.");

            // Read only: do not move this process to another window station.
            var stationName = new StringBuilder(256);
            if (!GetUserObjectInformationW(GetProcessWindowStation(), 2, stationName,
                    checked((uint)(stationName.Capacity * sizeof(char))), out _))
                throw LastError("GetUserObjectInformationW(window station)");
            if (!string.Equals(stationName.ToString(), "WinSta0", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runner must already belong to WinSta0.");

            // CreateDesktop can associate its calling thread with the desktop. End
            // that thread before cleanup instead of calling SetThreadDesktop.
            int desktopError = 0;
            var creator = new Thread(() =>
            {
                desktop = CreateDesktopW(desktopName, 0, 0, 0, DesktopCreateWindow, 0);
                desktopError = Marshal.GetLastPInvokeError();
            });
            creator.Start();
            creator.Join();
            if (desktop == 0)
                throw new Win32Exception(desktopError, "CreateDesktopW");
            Report(new { action = "desktop_created", desktop = desktopName, input_desktop_switched = false });

            job = CreateJobObjectW(0, null);
            if (job == 0)
                throw LastError("CreateJobObjectW");
            var limits = new JobExtendedLimitInformation
            {
                BasicLimitInformation = new JobBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose }
            };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JobExtendedLimitInformation>()))
                throw LastError("SetInformationJobObject(kill on close)");

            var security = new SecurityAttributes
            {
                Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
                InheritHandle = 1
            };
            // CREATE_NEW refuses to overwrite an existing log or user file.
            log = CreateFileW(logPath, GenericWrite, FileShareRead, ref security, CreateNew, FileAttributeNormal, 0);
            if (log == InvalidHandle)
                throw LastError("CreateFileW(new log)");
            Report(new { action = "log_created", log = logPath });
            input = CreateFileW("NUL", GenericRead, FileShareRead | FileShareWrite, ref security,
                OpenExisting, FileAttributeNormal, 0);
            if (input == InvalidHandle)
                throw LastError("CreateFileW(NUL)");

            nuint attributeBytes = 0;
            _ = InitializeProcThreadAttributeList(0, 1, 0, ref attributeBytes);
            if (attributeBytes == 0)
                throw LastError("InitializeProcThreadAttributeList(size)");
            attributeList = Marshal.AllocHGlobal(checked((nint)attributeBytes));
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeBytes))
                throw LastError("InitializeProcThreadAttributeList");
            attributesInitialized = true;
            inheritedHandles = Marshal.AllocHGlobal(2 * nint.Size);
            Marshal.WriteIntPtr(inheritedHandles, 0, input);
            Marshal.WriteIntPtr(inheritedHandles, nint.Size, log);
            if (!UpdateProcThreadAttribute(attributeList, 0, 0x00020002, inheritedHandles,
                    (nuint)(2 * nint.Size), 0, 0))
                throw LastError("UpdateProcThreadAttribute(handle list)");

            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Desktop = $@"WinSta0\{desktopName}",
                    Flags = StartfUseStdHandles | StartfForceOffFeedback,
                    StdInput = input,
                    StdOutput = log,
                    StdError = log
                },
                AttributeList = attributeList
            };
            // Change only this short-lived runner's environment; native inheritance
            // preserves every other variable, including hidden drive variables.
            Environment.SetEnvironmentVariable("FOLDERSPLITTER_QA_DESKTOP", desktopName);
            if (!CreateProcessW(executable, new StringBuilder(commandLine), 0, 0, true,
                    CreateNoWindow | CreateSuspended | ExtendedStartupInfoPresent, 0, null,
                    ref startup, out process))
                throw LastError("CreateProcessW(private desktop, suspended)");
            Report(new { action = "child_created", desktop = desktopName, pid = process.ProcessId, log = logPath, suspended = true });

            if (!AssignProcessToJobObject(job, process.Process))
                throw LastError("AssignProcessToJobObject");
            assigned = true;
            Report(new { action = "job_assigned", pid = process.ProcessId, kill_on_close = true });
            if (ResumeThread(process.Thread) == uint.MaxValue)
                throw LastError("ResumeThread");

            uint wait = WaitForSingleObject(process.Process, timeoutMilliseconds);
            if (wait == WaitTimeout)
            {
                timedOut = true;
                if (!TerminateJobObject(job, TimeoutExitCode))
                    throw LastError("TerminateJobObject(timeout)");
                Report(new { action = "timeout_job_terminated", pid = process.ProcessId });
                wait = WaitForSingleObject(process.Process, CleanupTimeoutMilliseconds);
            }
            if (wait == WaitFailed)
                throw LastError("WaitForSingleObject(child)");
            if (wait != WaitObject0)
                throw new InvalidOperationException($"Child exit was not confirmed: wait=0x{wait:X8}.");
            exited = true;
            if (!GetExitCodeProcess(process.Process, out uint exitCode))
                throw LastError("GetExitCodeProcess");
            result = unchecked((int)exitCode);
            Report(new { action = "child_exited", pid = process.ProcessId, exit_code = exitCode, timed_out = timedOut });
        }
        catch (Exception error)
        {
            Report(new { action = "launcher_error", error = error.Message });
            result = LauncherFailure;
        }
        finally
        {
            // Only the exact newly-created, still-suspended process can reach this
            // branch. Never discover processes by name or terminate an existing app.
            if (process.Process != 0 && !assigned && !exited)
            {
                bool terminated = TerminateProcess(process.Process, LauncherFailure);
                int error = terminated ? 0 : Marshal.GetLastPInvokeError();
                Report(new { action = "unassigned_child_terminated", pid = process.ProcessId, ok = terminated, error });
            }
            // The job is never inherited. Closing this sole handle also kills any
            // surviving descendants on normal exit or on an exceptional path.
            cleanupOk &= CloseOwnedHandle(ref job, "job");
            if (process.Process != 0 && !exited)
            {
                uint wait = WaitForSingleObject(process.Process, CleanupTimeoutMilliseconds);
                cleanupOk &= wait == WaitObject0;
                Report(new { action = "cleanup_child_exit", pid = process.ProcessId, confirmed = wait == WaitObject0, wait });
            }
            cleanupOk &= CloseOwnedHandle(ref process.Thread, "thread");
            cleanupOk &= CloseOwnedHandle(ref process.Process, "process");
            if (attributesInitialized)
                DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            Marshal.FreeHGlobal(inheritedHandles);
            Report(new { action = "cleanup_attribute_buffers", released = true });
            cleanupOk &= CloseOwnedHandle(ref input, "stdin");
            cleanupOk &= CloseOwnedHandle(ref log, "log");
            if (desktop != 0)
            {
                bool closed = CloseDesktop(desktop);
                int error = closed ? 0 : Marshal.GetLastPInvokeError();
                cleanupOk &= closed;
                Report(new { action = "cleanup_desktop_handle", desktop = desktopName, released = closed, error });
            }
        }

        if (!cleanupOk)
            result = LauncherFailure;
        Report(new { action = "result", exit_code = unchecked((uint)result), timed_out = timedOut, cleanup_ok = cleanupOk });
        return result;
    }

    private static int Usage()
    {
        Report(new { action = "usage", command = "PrivateDesktopQa --log <new-file-path> --timeout <positive-whole-seconds> -- <exe> <args...>" });
        return 2;
    }

    // Inverse of the Windows CRT argv parser: double backslashes before quotes
    // and before the closing quote. Always quoting also preserves empty arguments.
    private static string QuoteArgument(string argument)
    {
        if (argument.Contains('\0'))
            throw new ArgumentException("Arguments cannot contain NUL.");
        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(character);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static Win32Exception LastError(string operation) => new(Marshal.GetLastPInvokeError(), operation);

    private static bool CloseOwnedHandle(ref nint handle, string resource)
    {
        if (handle == 0 || handle == InvalidHandle)
            return true;
        bool closed = CloseHandle(handle);
        int error = closed ? 0 : Marshal.GetLastPInvokeError();
        handle = 0;
        Report(new { action = "cleanup_handle", resource, released = closed, error });
        return closed;
    }

    private static void Report(object receipt)
    {
        // A closed capture pipe must not prevent native handle cleanup.
        try { Console.WriteLine(JsonSerializer.Serialize(receipt)); }
        catch (IOException) { }
    }

    // Native field order and pointer-sized fields follow the Windows SDK:
    // STARTUPINFOW/STARTUPINFOEXW, PROCESS_INFORMATION, SECURITY_ATTRIBUTES,
    // JOBOBJECT_BASIC_LIMIT_INFORMATION, IO_COUNTERS and
    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION. No custom packing.
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Desktop;
        public nint Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Size;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimitInformation
    {
        public JobBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateDesktopW(string desktop, nint device, nint devmode, uint flags, uint access, nint security);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetProcessWindowStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder information, uint length, out uint needed);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint security, string? name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int informationClass, ref JobExtendedLimitInformation information, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateFileW(string fileName, uint access, uint share, ref SecurityAttributes security, uint disposition, uint attributes, nint template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint attributes, uint count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint attributes, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returnSize);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void DeleteProcThreadAttributeList(nint attributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processSecurity, nint threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(nint job, uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
