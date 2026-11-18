using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MelvorMods.Core;

// A mod's handle on starting workers.
sealed partial class ModContext
{
    /// <summary>
    /// Starts a worker: a headless instance of the game that loads only this mod and calls its
    /// <see cref="IWorkerMod.RunWorker"/>, connected to this game by <see cref="WorkerProcess.Channel"/>. Returns once
    /// the worker has connected; throws <see cref="WorkerStartException"/> with a plain reason otherwise. Blocks for up
    /// to 30 s: call it off the main thread. The mod's workers stop when it unloads, and all of them when the
    /// game exits.
    /// </summary>
    public WorkerProcess StartWorker(string role, WorkerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (!Hooks.Active)
            throw new WorkerStartException($"{Id} is unloaded");
        return Workers.Launch(Id, role, _root, options ?? new WorkerOptions());
    }

    /// <summary>
    /// Stops this mod's workers. Called last in the mod's own unload, after it has stopped talking to them: owned
    /// clean-ups run before the mod's unload, while its threads may still be writing to a worker.
    /// </summary>
    internal void StopWorkers() => Workers.StopAll(Id);
}

/// <summary>How to start a worker.</summary>
/// <param name="Environment">Extra environment variables for the worker only, such as runtime GC settings.</param>
/// <param name="MaxFps">The worker's frame cap: its main thread only idles.</param>
/// <param name="ExePathOverride">Starts this exe instead of the game's (for testing start failures).</param>
sealed record WorkerOptions(
    IReadOnlyDictionary<string, string>? Environment = null,
    int MaxFps = 10,
    string? ExePathOverride = null
);

/// <summary>A worker couldn't start; the message says what happened and what to do, in plain words.</summary>
sealed class WorkerStartException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A running worker: a headless game instance running part of a mod, connected by a pipe.</summary>
sealed class WorkerProcess : IDisposable
{
    private readonly Process _process;
    private readonly NamedPipeServerStream _pipe;
    private int _disposed;
    private int _queryFailed; // logged once: every later query fails the same way

    internal WorkerProcess(string owner, string role, Process process, NamedPipeServerStream pipe)
    {
        Owner = owner;
        Role = role;
        _process = process;
        _pipe = pipe;
        Pid = process.Id;
        _process.Exited += (_, _) => Exited?.Invoke(ExitCode());
    }

    internal string Owner { get; }

    public int Pid { get; }
    public string Role { get; }

    /// <summary>The connected pipe, both directions. Read and write it off the main thread.</summary>
    public Stream Channel => _pipe;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException e)
            {
                QueryFailed(e);
                return true;
            }
        }
    }

    /// <summary>Raised on a worker thread when the process ends, with its exit code if known.</summary>
    public event Action<int?>? Exited;

    private int? ExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException e)
        {
            QueryFailed(e);
            return null;
        }
    }

    private void QueryFailed(InvalidOperationException error)
    {
        if (Interlocked.Exchange(ref _queryFailed, 1) == 0)
            Log.Write(Owner, $"WARN worker {Role} pid {Pid} can't be queried, counted as ended: {error.Message}");
    }

    /// <summary>Terminates the worker now.</summary>
    public void Kill()
    {
        try
        {
            _process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It has exited already, or is exiting: killing it is what was wanted.
        }
    }

    /// <summary>Closes the pipe (the worker exits when it sees that), then kills it if still alive after 2 s.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        Workers.Forget(this);
        _pipe.Dispose();
        Task.Run(async () =>
        {
            try
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill();
            }
            finally
            {
                _process.Dispose();
            }
        });
    }
}

/// <summary>Starts and tracks the live game's workers. All of them die with the game, however it ends.</summary>
internal static partial class Workers
{
    private static readonly Lock _lock = new();
    private static readonly List<WorkerProcess> _running = [];
    private static string? _exe;
    private static nint _job;

    /// <summary>At the mod's load in the live game, notes the game's exe and stops every worker at exit.</summary>
    public static void Start()
    {
        _exe = System.Environment.ProcessPath;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll(null);
    }

    public static WorkerProcess Launch(string mod, string role, string root, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var exe = options.ExePathOverride ?? _exe;
        if (exe == null || !File.Exists(exe))
            throw new WorkerStartException("the game exe was not found: verify the game files in Steam");

        var parent = System.Environment.ProcessId;
        var pipeName =
            $"MelvorMods.{mod}.{role}.{parent}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        // The game's language as last read on the main thread (this runs off it), so the worker's game text matches.
        var locale = TextHost.Locale;
        var worker = new WorkerArgs(mod, role, pipeName, parent, WorkerArgs.IsLocale(locale) ? locale : null);
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // A worker's console, when it makes one, opens hidden.
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var arg in WorkerArgs.Arguments(options.MaxFps, Path.Combine(root, "logs", worker.GodotLog), worker))
            info.ArgumentList.Add(arg);
        foreach (var (key, value) in options.Environment ?? new Dictionary<string, string>())
            info.Environment[key] = value;

        Process? process = null;
        try
        {
            process = Process.Start(info) ?? throw new WorkerStartException("the game exe did not start");
            AddToJob(process);
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A worker at normal priority still works; the game only loses a little smoothness to it.
            }
            process.EnableRaisingEvents = true;
            WaitForConnection(pipe, process);
        }
        catch (Exception e) when (e is not WorkerStartException)
        {
            Abandon(pipe, process);
            throw new WorkerStartException(
                $"the simulator could not be started ({e.GetType().Name}: {e.Message}): "
                    + "an antivirus may be blocking a second Melvor2.exe; allow it, or simulate in the game's process",
                e
            );
        }
        catch
        {
            Abandon(pipe, process);
            throw;
        }
        var started = new WorkerProcess(mod, role, process, pipe);
        lock (_lock)
            _running.Add(started);
        return started;
    }

    private static void WaitForConnection(NamedPipeServerStream pipe, Process process)
    {
        var connect = pipe.WaitForConnectionAsync();
        var exited = process.WaitForExitAsync();
        var finished = Task.WaitAny([connect, exited], TimeSpan.FromSeconds(30));
        if (finished == 0)
        {
            connect.GetAwaiter().GetResult();
            return;
        }
        if (finished == 1)
            throw new WorkerStartException(
                $"the simulator exited while starting (code {process.ExitCode}): an antivirus may be blocking a "
                    + "second Melvor2.exe; allow it, or simulate in the game's process"
            );
        throw new WorkerStartException(
            "the simulator did not connect within 30 s: the machine may be busy; try again, or simulate in the "
                + "game's process"
        );
    }

    private static void Abandon(NamedPipeServerStream pipe, Process? process)
    {
        pipe.Dispose();
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone: abandoning it is done.
        }
        process.Dispose();
    }

    internal static void Forget(WorkerProcess worker)
    {
        lock (_lock)
            _running.Remove(worker);
    }

    /// <summary>Stops one mod's workers (on unload or reload), or every worker (null, at game exit).</summary>
    public static void StopAll(string? mod)
    {
        List<WorkerProcess> stopping;
        lock (_lock)
            stopping = [.. _running.Where(w => mod == null || w.Owner.Equals(mod, StringComparison.OrdinalIgnoreCase))];
        foreach (var worker in stopping)
        {
            if (mod == null)
                worker.Kill(); // the game is going: no time to wait for a clean exit
            worker.Dispose();
        }
    }

    // One kill-on-close job for every worker: when the game's process ends, even killed outright, Windows closes
    // the job's last handle and ends the workers with it.
    private static void AddToJob(Process process)
    {
        if (!OperatingSystem.IsWindows())
            return;
        lock (_lock)
        {
            if (_job == 0)
            {
                var job = CreateJobObjectW(0, 0);
                if (job == 0)
                    throw new WorkerStartException(
                        $"the simulator could not be contained (job object error {Marshal.GetLastPInvokeError()}); "
                            + "simulate in the game's process"
                    );
                var limits = new ExtendedLimits
                {
                    BasicLimitInformation = { LimitFlags = KillOnJobClose | DieOnUnhandledException },
                };
                if (
                    !SetInformationJobObject(
                        job,
                        ExtendedLimitInformation,
                        ref limits,
                        Marshal.SizeOf<ExtendedLimits>()
                    )
                )
                    throw new WorkerStartException(
                        $"the simulator could not be contained (job limit error {Marshal.GetLastPInvokeError()}); "
                            + "simulate in the game's process"
                    );
                _job = job;
            }
        }
        if (!AssignProcessToJobObject(_job, process.Handle))
            throw new WorkerStartException(
                $"the simulator could not be contained (job error {Marshal.GetLastPInvokeError()}); "
                    + "simulate in the game's process"
            );
    }

    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private const uint DieOnUnhandledException = 0x0400;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
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
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateJobObjectW(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimits info, int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);
}
