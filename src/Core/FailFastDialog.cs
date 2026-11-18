using System.Runtime.InteropServices;

namespace MelvorMods.Core;

/// <summary>
/// Keeps Windows from drawing its error box when the runtime ends a worker on a fatal error. The runtime's last step
/// is <c>RaiseFailFastException</c> with no flags, and where Windows Error Reporting is off Windows answers that with
/// a box of its own, which the worker's error mode doesn't reach and which outlives the worker. In the worker only,
/// the runtime's import of that function is pointed at a native stub that adds <c>FAIL_FAST_NO_HARD_ERROR_DLG</c>
/// and jumps on. The stub is machine code, so nothing managed runs while the runtime fails. The runtime has written
/// its crash dump and event-log entry by then, so both stay.
/// </summary>
internal static unsafe partial class FailFastDialog
{
    private const uint NoHardErrorDialog = 0x2;

    /// <summary>Patches the runtime's import; null when done, else why not.</summary>
    public static string? Off()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return "not Windows x64";
        var runtime = Native.GetModuleHandleW("coreclr.dll");
        var kernel = Native.GetModuleHandleW("kernel32.dll");
        var target = kernel == 0 ? 0 : Native.GetProcAddress(kernel, "RaiseFailFastException");
        if (runtime == 0 || target == 0)
            return "the runtime or RaiseFailFastException wasn't found";
        var slots = ImportSlots((byte*)runtime, target);
        if (slots.Count == 0)
            return "the runtime doesn't import RaiseFailFastException";
        var stub = Stub(target);
        if (stub == 0)
            return "no memory for the stub";
        foreach (var slot in slots)
        {
            if (!Native.VirtualProtect(slot, (nuint)sizeof(nint), PageReadWrite, out var old))
                return "the runtime's import table couldn't be written";
            *(nint*)slot = stub;
            _ = Native.VirtualProtect(slot, (nuint)sizeof(nint), old, out _);
        }
        return null;
    }

    // The import address table slots of the module at <base> that hold <target>.
    private static List<nint> ImportSlots(byte* image, nint target)
    {
        var slots = new List<nint>();
        var headers = image + *(int*)(image + 0x3C);
        // PE32+: the optional header follows the 4-byte signature and the 20-byte file header; its import directory
        // entry (index 1) is at offset 120.
        var imports = *(uint*)(headers + 24 + 120);
        if (imports == 0)
            return slots;
        for (var descriptor = image + imports; *(uint*)(descriptor + 12) != 0; descriptor += 20)
        {
            for (var slot = (nint*)(image + *(uint*)(descriptor + 16)); *slot != 0; slot++)
            {
                if (*slot == target)
                    slots.Add((nint)slot);
            }
        }
        return slots;
    }

    // or r8d, 2 ; mov rax, <target> ; jmp rax
    private static nint Stub(nint target)
    {
        var code = Native.VirtualAlloc(0, 4096, MemCommitReserve, PageReadWrite);
        if (code == 0)
            return 0;
        var bytes = (byte*)code;
        ReadOnlySpan<byte> head = [0x41, 0x83, 0xC8, (byte)NoHardErrorDialog, 0x48, 0xB8];
        head.CopyTo(new Span<byte>(bytes, head.Length));
        *(nint*)(bytes + head.Length) = target;
        bytes[head.Length + 8] = 0xFF;
        bytes[head.Length + 9] = 0xE0;
        _ = Native.VirtualProtect(code, 4096, PageExecuteRead, out _);
        _ = Native.FlushInstructionCache(Native.GetCurrentProcess(), code, 4096);
        return code;
    }

    private const uint MemCommitReserve = 0x3000;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecuteRead = 0x20;

    private static partial class Native
    {
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint GetModuleHandleW(string name);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint GetProcAddress(nint module, string name);

        [LibraryImport("kernel32.dll")]
        internal static partial nint VirtualAlloc(nint address, nuint size, uint type, uint protect);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool VirtualProtect(nint address, nuint size, uint protect, out uint old);

        [LibraryImport("kernel32.dll")]
        internal static partial nint GetCurrentProcess();

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool FlushInstructionCache(nint process, nint address, nuint size);
    }
}
