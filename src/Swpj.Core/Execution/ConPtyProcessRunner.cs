using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Swpj.Core.Execution;

/// <summary>
/// 用伪控制台（ConPTY）跑子进程。
///
/// 解决的问题：**只在真控制台里画进度的程序**（7z 就是）一旦被重定向就什么进度都不输出。
/// 实测证据：40 MB 输入的完整输出只有 354 字节，一个 % 都没有；接上伪控制台后才有百分比。
///
/// 为什么自己调 CreateProcess 而不用 .NET 的 Process：.NET 不支持把子进程挂到伪控制台上。
/// 代价是命令行要自己拼成字符串（Win32 只接受一条命令行），所以引号规则必须正确
/// —— 见 <see cref="WindowsCommandLine"/>，那里有专门的单测。
///
/// 顺带补上了一个缺口：这里用 **Job Object**（KILL_ON_JOB_CLOSE）来保证"取消时杀掉整棵进程树"，
/// 连"被杀之后又新建的子进程"也覆盖得到，比 <c>Kill(entireProcessTree: true)</c> 更彻底。
/// </summary>
public sealed class ConPtyProcessRunner : IProcessRunner
{
    private const short ConsoleWidth = 120;
    private const short ConsoleHeight = 30;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    /// <summary>
    /// 挂起方式创建进程。**这是保证"取消能杀掉整棵树"的关键**：
    /// 先把进程放进 Job Object 再恢复运行，否则它可能在入 job 之前就已经派生了子进程，
    /// 那些孙进程不在 job 里，取消时就杀不掉（实测：取消杀整树的用例曾经偶发失败）。
    /// </summary>
    private const uint CreateSuspended = 0x00000004;

    private const uint WaitObject0 = 0x00000000;
    private const uint Infinite = 0xFFFFFFFF;

    /// <summary>
    /// 显式声明子进程的标准句柄（这里三个都置空）。
    /// 不加这个标志时，子进程会继承父进程的标准句柄值，从而绕过伪控制台——见下面使用处的长注释。
    /// </summary>
    private const int StartfUseStdHandles = 0x00000100;

    /// <summary>ConPTY 输出里可能出现光标位置询问（ESC[6n），程序会等一个回复；不回它会卡住。</summary>
    private const string CursorPositionQuery = "\u001b[6n";
    private const string CursorPositionReply = "\u001b[1;1R";

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("伪控制台（ConPTY）只在 Windows 上可用。");
        }

        var stopwatch = Stopwatch.StartNew();
        var lines = new List<ProcessOutputLine>();
        var gate = new object();
        var totalLines = 0;

        // 用局部变量而不是字段，这样同一个 runner 实例可以被并发复用
        StreamWriter? stdin = null;

        void OnLine(string text)
        {
            if (text.Contains(CursorPositionQuery, StringComparison.Ordinal) && stdin is not null)
            {
                try
                {
                    stdin.Write(CursorPositionReply);
                    stdin.Flush();
                }
                catch (Exception)
                {
                    // 写不进去说明进程已经走了，忽略即可。
                }
            }

            var line = new ProcessOutputLine(OutputStream.StandardOutput, text);
            lock (gate)
            {
                totalLines++;
                if (lines.Count < request.MaxCapturedLines)
                {
                    lines.Add(line);
                }
            }

            progress?.Report(line);
        }

        // 与管道版保持一致：目录不存在就先建（"解压到还不存在的目录"是常见意图）
        if (!string.IsNullOrEmpty(request.WorkingDirectory) && !Directory.Exists(request.WorkingDirectory))
        {
            Directory.CreateDirectory(request.WorkingDirectory);
        }

        // 关键：给管道设 SECURITY_ATTRIBUTES.bInheritHandle = TRUE。
        // 伪控制台的宿主进程是另一个进程，它需要继承这两个管道句柄才能与子进程通信；
        // 传 NULL（不可继承）会让会话变成"哑"的——伪控制台会渲染初始画面，
        // 但子进程的标准句柄接不到控制台上，输出全部丢失（这就是本文件一段时间的 bug）。
        var security = new SecurityAttributes
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = true,
        };
        var securityPointer = Marshal.AllocHGlobal(security.nLength);
        Marshal.StructureToPtr(security, securityPointer, false);

        Microsoft.Win32.SafeHandles.SafeFileHandle ptyInput;
        Microsoft.Win32.SafeHandles.SafeFileHandle ourInput;
        Microsoft.Win32.SafeHandles.SafeFileHandle ourOutput;
        Microsoft.Win32.SafeHandles.SafeFileHandle ptyOutput;

        try
        {
            if (!CreatePipe(out ptyInput, out ourInput, securityPointer, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe（stdin 方向）失败");
            }

            if (!CreatePipe(out ourOutput, out ptyOutput, securityPointer, 0))
            {
                ptyInput.Dispose();
                ourInput.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe（stdout 方向）失败");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(securityPointer);
        }

        var consoleSize = new Coord { X = ConsoleWidth, Y = ConsoleHeight };
        var createResult = CreatePseudoConsole(
            consoleSize, ptyInput.DangerousGetHandle(), ptyOutput.DangerousGetHandle(), 0, out var pseudoConsole);

        // 注意：**不要**在 CreatePseudoConsole 之后立刻关掉 ptyInput / ptyOutput。
        // "立刻关闭导致会话变哑"这个假设已被实测排除（留着不关同样拿不到输出），
        // 但让它们与伪控制台同寿命更稳妥，所以统一留到 finally 关闭。
        if (createResult != 0)
        {
            ptyInput.Dispose();
            ptyOutput.Dispose();
            ourInput.Dispose();
            ourOutput.Dispose();
            throw new Win32Exception(createResult, $"CreatePseudoConsole 失败（HRESULT 0x{createResult:X8}）");
        }

        var attributeList = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;
        var job = IntPtr.Zero;
        ProcessInformation processInfo = default;
        FileStream? inputStream = null;
        FileStream? outputStream = null;

        try
        {
            // ---- 属性列表：把伪控制台挂到子进程上 ----
            var attributeListSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
            attributeList = Marshal.AllocHGlobal(attributeListSize);

            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList 失败");
            }

            if (!UpdateProcThreadAttribute(
                    attributeList, 0, (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute 失败");
            }

            // ---- 环境块（只有调用方指定了环境变量才构造） ----
            var flags = ExtendedStartupInfoPresent | CreateSuspended;
            if (request.Environment is { Count: > 0 })
            {
                environmentBlock = BuildEnvironmentBlock(request.Environment);
                flags |= CreateUnicodeEnvironment;
            }

            var startupInfo = new StartupInfoEx
            {
                // 关键：STARTF_USESTDHANDLES 加上三个 NULL 句柄。
                //
                // 实测结论（这条是花了很久才查清的）：**子进程的标准句柄是从父进程复制过去的**
                // ——即使 bInheritHandles = FALSE，句柄"值"照样被填进子进程的标准句柄槽，
                // 而 ConPTY 只负责提供控制台，并**不会**覆盖这些继承来的句柄。后果：
                //   * 父进程是控制台程序 → 子进程直接写到父进程的控制台，绕过 ConPTY
                //   * 父进程的 stdout 被重定向（如 dotnet test）→ 子进程写到那个看不见的管道
                //   * 父进程是无控制台的 GUI 程序（真实的 WinUI 宿主）→ 没有可继承的句柄，ConPTY 正常工作
                // 显式置空后，三种情形都走控制台，行为一致。
                StartupInfo = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfoEx>(),
                    dwFlags = StartfUseStdHandles,
                },
                lpAttributeList = attributeList,
            };

            var commandLine = WindowsCommandLine.Build(request.Executable, request.Arguments);

            if (!CreateProcess(
                    request.Executable, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                    environmentBlock, request.WorkingDirectory, ref startupInfo, out processInfo))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), $"CreateProcess 失败：{commandLine}");
            }

            // ---- Job Object：保证取消/结束时整棵树都死掉 ----
            job = CreateJobObject(IntPtr.Zero, null);
            if (job != IntPtr.Zero)
            {
                var limit = new JobObjectExtendedLimitInformation();
                limit.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

                var limitSize = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
                var limitPointer = Marshal.AllocHGlobal(limitSize);
                try
                {
                    Marshal.StructureToPtr(limit, limitPointer, false);
                    SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, limitPointer, (uint)limitSize);
                }
                finally
                {
                    Marshal.FreeHGlobal(limitPointer);
                }

                // 分配失败不致命：只是失去"保证不留下孤儿"这层保险。
                AssignProcessToJobObject(job, processInfo.hProcess);
            }

            // 进程是挂起创建的：入 job 之后再恢复运行，这样它派生的一切都在 job 里。
            if (ResumeThread(processInfo.hThread) == unchecked((uint)-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread 失败");
            }

            inputStream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(ourInput.DangerousGetHandle(), ownsHandle: true), FileAccess.Write, 4096);
            stdin = new StreamWriter(inputStream) { AutoFlush = true };
            ourInput = null!; // 所有权已交给流

            outputStream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(ourOutput.DangerousGetHandle(), ownsHandle: true), FileAccess.Read, 4096);
            ourOutput = null!;

            // 读输出必须与等待同时进行，否则管道缓冲满了子进程会卡住
            var pumpTask = PumpAsync(outputStream, request.OutputEncoding, OnLine);

            var (canceled, timedOut) = await Task.Run(() =>
                WaitForExit(processInfo.hProcess, request.Timeout, stopwatch, cancellationToken)).ConfigureAwait(false);

            if (canceled || timedOut)
            {
                if (job != IntPtr.Zero)
                {
                    TerminateJobObject(job, 1);
                }
                else
                {
                    TerminateProcess(processInfo.hProcess, 1);
                }

                WaitForSingleObject(processInfo.hProcess, Infinite);
            }

            GetExitCodeProcess(processInfo.hProcess, out var exitCode);

            // 子进程退出时，伪控制台可能还有没渲染完的输出留在它自己的缓冲里（它有自己的渲染线程）。
            // 直接 ClosePseudoConsole 会把这些输出丢掉——实测会把子进程的整段输出丢光，
            // 只剩下开头的清屏与设置标题序列。
            // 所以先等输出"安静下来"（连续一段时间没有新行），再关掉伪控制台。
            var quietDeadline = DateTime.UtcNow.AddSeconds(3);
            var lastSeen = -1;

            while (DateTime.UtcNow < quietDeadline)
            {
                await Task.Delay(120).ConfigureAwait(false);

                var current = totalLines;
                if (current == lastSeen)
                {
                    break;
                }

                lastSeen = current;
            }

            // 关掉伪控制台，输出管道才会到达 EOF，剩下的输出才读得完
            ClosePseudoConsole(pseudoConsole);
            pseudoConsole = IntPtr.Zero;

            // 给读流一个上限，避免极端情况下卡在这里
            await Task.WhenAny(pumpTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            stopwatch.Stop();

            return new ProcessRunResult
            {
                ExitCode = unchecked((int)exitCode),
                Canceled = canceled,
                TimedOut = timedOut,
                Duration = stopwatch.Elapsed,
                Lines = lines,
                TotalLineCount = totalLines,
            };
        }
        finally
        {
            stdin?.Dispose();
            inputStream?.Dispose();
            outputStream?.Dispose();

            if (pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(pseudoConsole);
            }

            if (processInfo.hThread != IntPtr.Zero)
            {
                CloseHandle(processInfo.hThread);
            }

            if (processInfo.hProcess != IntPtr.Zero)
            {
                CloseHandle(processInfo.hProcess);
            }

            // 关掉 job 会触发 KILL_ON_JOB_CLOSE：即使主进程已经退出，它留下的子孙也会被清理掉。
            if (job != IntPtr.Zero)
            {
                CloseHandle(job);
            }

            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            ourInput?.Dispose();
            ourOutput?.Dispose();

            // PTY 侧的两个句柄必须活得和伪控制台一样久，否则子进程的输出会全部丢失。
            ptyInput?.Dispose();
            ptyOutput?.Dispose();
        }
    }

    private static (bool Canceled, bool TimedOut) WaitForExit(
        IntPtr process,
        TimeSpan? timeout,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (WaitForSingleObject(process, 100) == WaitObject0)
            {
                return (false, false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (true, false);
            }

            if (timeout is { } limit && stopwatch.Elapsed >= limit)
            {
                return (false, true);
            }
        }
    }

    /// <summary>把输出管道读成一行行文本。切行同时认 \r 与 \n——这正是进度能被抓住的原因。</summary>
    private static async Task PumpAsync(FileStream output, Encoding encoding, Action<string> onLine)
    {
        var decoder = encoding.GetDecoder();
        var bytes = new byte[4096];
        var chars = new char[encoding.GetMaxCharCount(bytes.Length)];
        var pending = new StringBuilder();

        while (true)
        {
            int read;
            try
            {
                read = await output.ReadAsync(bytes.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 读中断必须让用户看见：静默停下会表现为"程序没有任何输出"，极难排查。
                onLine($"# 伪控制台输出读取中断：{ex.GetType().Name}: {ex.Message}");
                break;
            }

            if (read == 0)
            {
                break;
            }

            var charCount = decoder.GetChars(bytes, 0, read, chars, 0);

            for (var i = 0; i < charCount; i++)
            {
                var ch = chars[i];

                if (ch is '\r' or '\n')
                {
                    if (pending.Length > 0)
                    {
                        onLine(pending.ToString());
                        pending.Clear();
                    }

                    continue;
                }

                pending.Append(ch);
            }
        }

        if (pending.Length > 0)
        {
            onLine(pending.ToString());
        }
    }

    /// <summary>构造 CreateProcess 需要的 Unicode 环境块（当前环境 + 覆盖项，按键名排序）。</summary>
    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        foreach (var (key, value) in overrides)
        {
            variables[key] = value;
        }

        var builder = new StringBuilder();
        foreach (var (key, value) in variables)
        {
            builder.Append(key).Append('=').Append(value).Append('\0');
        }

        builder.Append('\0');
        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    // ------------------------------------------------------------------ 互操作

    /// <summary>
    /// 诊断用：CreateProcess 要求 STARTUPINFOEX 的 cb 字段等于本结构体的大小，
    /// 写错的后果是"伪控制台属性被静默忽略、子进程的标准句柄变成无效值"。
    /// 所以把尺寸暴露出来，让测试能断言它是 64 位下的 112。
    /// </summary>
    public static int StartupInfoExSize => Marshal.SizeOf<StartupInfoEx>();

    public static int StartupInfoSize => Marshal.SizeOf<StartupInfo>();

    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out Microsoft.Win32.SafeHandles.SafeFileHandle hReadPipe,
        out Microsoft.Win32.SafeHandles.SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        Coord size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue,
        IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int jobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);
}
