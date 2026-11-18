using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// Reads a crashed host's entry from the Windows Application log: the runtime writes one (<c>.NET Runtime</c>,
/// events 1023–1027) with the exception and stack as the process dies, under the process's own id. A stack overflow
/// writes none.
/// </summary>
internal static partial class CrashEventLog
{
    private const int AccessDenied = 5;

    /// <summary>The entry for <paramref name="pid"/> since <paramref name="startedUtc"/>, or why there is none.</summary>
    public static (string? Entry, string? Note) Read(int pid, DateTime startedUtc)
    {
        if (!OperatingSystem.IsWindows())
            return (null, EventLogNotes.NotFound);
        var since = (long)Math.Max(1000, (DateTime.UtcNow - startedUtc).TotalMilliseconds + 60_000);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"*[System[Provider[@Name='.NET Runtime'] and (EventID>=1023 and EventID<=1027) and TimeCreated[timediff(@SystemTime) <= {since}]]]"
        );
        var results = Native.EvtQuery(0, "Application", query, QueryChannelPath | QueryReverseDirection);
        if (results == 0)
            return (
                null,
                Marshal.GetLastPInvokeError() == AccessDenied ? EventLogNotes.AccessRefused : EventLogNotes.NotFound
            );
        try
        {
            var events = new nint[16];
            while (Native.EvtNext(results, events.Length, events, 1000, 0, out var count) && count > 0)
            {
                string? found = null;
                for (var i = 0; i < count; i++)
                {
                    if (found == null && Render(events[i]) is { } xml && Match(xml, pid) is { } entry)
                        found = entry;
                    Native.EvtClose(events[i]);
                }
                if (found != null)
                    return (found, null);
            }
            return (null, EventLogNotes.NotFound);
        }
        finally
        {
            Native.EvtClose(results);
        }
    }

    /// <summary>The entry as text when its process id is <paramref name="pid"/>: the event id, then its message.</summary>
    internal static string? Match(string xml, int pid)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var system = doc.Root?.Element(ns + "System");
        var process = system?.Element(ns + "Execution")?.Attribute("ProcessID")?.Value;
        if (process != pid.ToString(CultureInfo.InvariantCulture))
            return null;
        var id = system?.Element(ns + "EventID")?.Value ?? "?";
        var data = doc.Root?.Element(ns + "EventData")?.Elements(ns + "Data").Select(d => d.Value) ?? [];
        return $".NET Runtime {id}: {string.Join("\n", data).Trim()}";
    }

    private static unsafe string? Render(nint handle)
    {
        const int renderXml = 1;
        _ = Native.EvtRender(0, handle, renderXml, 0, null, out var used, out _);
        if (used <= 0)
            return null;
        var buffer = new char[used / 2 + 1];
        fixed (char* text = buffer)
        {
            if (!Native.EvtRender(0, handle, renderXml, buffer.Length * 2, text, out used, out _))
                return null;
        }
        return new string(buffer, 0, Math.Max(0, used / 2 - 1));
    }

    private const int QueryChannelPath = 0x1;
    private const int QueryReverseDirection = 0x200;

    private static unsafe partial class Native
    {
        [LibraryImport("wevtapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint EvtQuery(nint session, string path, string query, int flags);

        [LibraryImport("wevtapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EvtNext(
            nint results,
            int count,
            [Out] nint[] events,
            int timeout,
            int flags,
            out int returned
        );

        [LibraryImport("wevtapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EvtRender(
            nint context,
            nint fragment,
            int flags,
            int bufferSize,
            char* buffer,
            out int used,
            out int properties
        );

        [LibraryImport("wevtapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EvtClose(nint handle);
    }
}
