using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace MelvorMods.Core;

// The worker flags' second part: reading this process's own command line needs the shared code.
sealed partial record WorkerArgs
{
    /// <summary>
    /// This process's own command line, split as Windows splits it. Read from the OS rather than from .NET, which
    /// may not have been given the arguments of a game that hosts it.
    /// </summary>
    public static IReadOnlyList<string> ProcessArgs() => Standalone.ProcessArgs();
}

/// <summary>
/// A mod that can run part of itself in a worker, a headless game instance it starts from the live game.
/// </summary>
interface IWorkerMod
{
    /// <summary>
    /// Called instead of <see cref="IMod.Load"/> in a worker, on a dedicated thread once the scene tree exists.
    /// Return when the worker should exit; the process then ends.
    /// </summary>
    void RunWorker(WorkerContext context);
}

/// <summary>What a mod gets in its worker.</summary>
sealed class WorkerContext
{
    private readonly Harmony _harmony;
    private readonly Assembly _assembly;

    internal WorkerContext(
        string id,
        string role,
        Stream channel,
        string modDirectory,
        string configPath,
        int parentPid,
        Assembly assembly
    )
    {
        Role = role;
        Channel = channel;
        Log = new ModLog($"{id}/{role}");
        ModDirectory = modDirectory;
        ConfigPath = configPath;
        ParentPid = parentPid;
        _assembly = assembly;
        _harmony = new Harmony($"melvormods.{id.ToLowerInvariant()}");
    }

    public string Role { get; }

    /// <summary>
    /// The pipe to the live game, both directions. Its end (a read of 0 bytes, a failed write) ends the worker.
    /// </summary>
    public Stream Channel { get; }

    /// <summary>Writes <c>logs/&lt;mod&gt;.&lt;role&gt;.log</c>, with <c>&lt;mod&gt;/&lt;role&gt;</c> as the source.</summary>
    public ModLog Log { get; }

    public string ModDirectory { get; }

    /// <summary>The mod's settings file; read it only, the live game owns it.</summary>
    public string ConfigPath { get; }

    public int ParentPid { get; }

    /// <summary>
    /// Applies every [HarmonyPatch] class in the mod that has no category: the start-up patches and the shared code's
    /// are applied by category, the start-up ones before the game's main scene, and the shared ones never here.
    /// </summary>
    public void PatchAll()
    {
        _harmony.PatchAllUncategorized(_assembly);
        Log.Info($"{_harmony.GetPatchedMethods().Count()} methods patched");
    }
}

/// <summary>The running worker's side: whether this process is one, its pipe, and how it ends.</summary>
internal static partial class WorkerMode
{
    /// <summary>The Harmony category of the patches that keep a worker from starting up as a game.</summary>
    internal const string StartupCategory = "melvormods.worker-startup";

    private static int _exiting;

    /// <summary>This process's worker arguments, or null in the live game.</summary>
    public static WorkerArgs? Current { get; set; }

    public static bool Active => Current != null;

    /// <summary>
    /// Keeps Windows from showing an error box or a console window when this process crashes: a worker has no window
    /// and no one watching it, and the live game already reports a worker that went away. The error mode covers
    /// ordinary faults, and the WER flag a machine where Windows Error Reporting is on; where it is off, the worker's
    /// job ends it on an unhandled exception and <see cref="FailFastDialog"/> covers the runtime's fatal errors. The
    /// runtime starts createdump for the crash dump, a console program: the worker's own console, hidden (it is
    /// started with a hidden window), keeps it from opening one. The live game's own settings are untouched.
    /// </summary>
    public static void SilenceErrorBoxes()
    {
        if (!OperatingSystem.IsWindows())
            return;
        const uint failCriticalErrors = 0x0001;
        const uint noGpFaultErrorBox = 0x0002;
        const uint noOpenFileErrorBox = 0x8000;
        const uint werNoUi = 0x0020;
        _ = Native.SetErrorMode(Native.GetErrorMode() | failCriticalErrors | noGpFaultErrorBox | noOpenFileErrorBox);
        _ = Native.WerSetFlags(werNoUi);
        if (Native.GetConsoleWindow() == 0)
            _ = Native.AllocConsole();
    }

    private static partial class Native
    {
        [LibraryImport("kernel32.dll")]
        internal static partial int WerSetFlags(uint flags);

        [LibraryImport("kernel32.dll")]
        internal static partial nint GetConsoleWindow();

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AllocConsole();

        [LibraryImport("kernel32.dll")]
        internal static partial uint GetErrorMode();

        [LibraryImport("kernel32.dll")]
        internal static partial uint SetErrorMode(uint mode);
    }

    /// <summary>Connects to the parent's pipe; the parent created it before starting this process.</summary>
    public static Stream Connect(WorkerArgs args)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            args.Pipe,
            PipeDirection.InOut,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous
        );
        try
        {
            pipe.Connect(TimeSpan.FromSeconds(10));
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
        return new EndingStream(pipe, () => Exit(0, "the pipe to the game closed"));
    }

    /// <summary>Ends this process when the live game's process goes, however it goes.</summary>
    public static void WatchParent(int parentPid)
    {
        System.Diagnostics.Process parent;
        try
        {
            parent = System.Diagnostics.Process.GetProcessById(parentPid);
        }
        catch (ArgumentException)
        {
            Exit(0, $"the game (pid {parentPid}) is not running");
            return;
        }
        new Thread(() =>
        {
            parent.WaitForExit();
            Exit(0, "the game exited");
        })
        {
            IsBackground = true,
            Name = "melvormods-worker-parent",
        }.Start();
    }

    /// <summary>Quits through the scene tree, and exits outright if that hasn't ended the process within 5 s.</summary>
    public static void Exit(int code, string reason)
    {
        if (Interlocked.Exchange(ref _exiting, 1) == 1)
            return;
        Log.Write(Current?.Mod ?? "core", $"worker exiting ({code}): {reason}");
        if (GameTree.Current is { } tree)
            Godot.Callable.From(() => tree.Quit(code)).CallDeferred();
        new Thread(() =>
        {
            Thread.Sleep(5000);
            Environment.Exit(code);
        })
        {
            IsBackground = true,
            Name = "melvormods-worker-exit",
        }.Start();
    }

    // The pipe, with its end noticed: a read of nothing or a failed read or write ends the worker.
    private sealed class EndingStream(Stream inner, Action ended) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Watch(() => inner.Read(buffer, offset, count), count);

        public override int Read(Span<byte> buffer)
        {
            try
            {
                var read = inner.Read(buffer);
                if (read == 0 && buffer.Length > 0)
                    ended();
                return read;
            }
            catch (IOException)
            {
                ended();
                throw;
            }
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Watch(
                () =>
                {
                    inner.Write(buffer, offset, count);
                    return count;
                },
                0
            );

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try
            {
                inner.Write(buffer);
            }
            catch (IOException)
            {
                ended();
                throw;
            }
        }

        public override void Flush() =>
            Watch(
                () =>
                {
                    inner.Flush();
                    return 0;
                },
                0
            );

        private int Watch(Func<int> io, int asked)
        {
            try
            {
                var read = io();
                if (read == 0 && asked > 0)
                    ended();
                return read;
            }
            catch (IOException)
            {
                ended();
                throw;
            }
            catch (ObjectDisposedException)
            {
                ended();
                throw;
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

internal static partial class ModEntry
{
    // In the mod's own worker, before the game's main scene runs: read the worker flags, silence error boxes and
    // apply the start-up patches; the worker itself starts once the scene tree is ready. A worker that can't run
    // ends the process, so it never goes on to start up as a game, whatever loader started it.
    static partial void RunAsWorker(Manifest manifest, string directory, string dataRoot, string role, ref bool started)
    {
        try
        {
            var args =
                WorkerArgs.Parse(WorkerArgs.ProcessArgs(), out var problem)
                ?? throw new InvalidOperationException(problem ?? "no worker flags");
            WorkerMode.Current = args;
            WorkerMode.SilenceErrorBoxes();
            WorkerHost.Configure(manifest.Id, role, directory, dataRoot);
            if (FailFastDialog.Off() is { } why)
                Log.Write(manifest.Id, $"WARN fatal errors may show a system box: {why}");
            new Harmony($"melvormods.{manifest.Id.ToLowerInvariant()}").PatchCategory(
                typeof(ModEntry).Assembly,
                WorkerMode.StartupCategory
            );
            started = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(manifest.Id, $"ERROR {manifest.Id} can't run as a worker: {e}");
            Environment.Exit(2);
        }
    }
}
