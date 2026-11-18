using MelvorMods.CombatSimulator2.Host;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class CrashEventLogTests
{
    // As the runtime writes it: the process id in System/Execution, the message lines in EventData.
    private const string Entry = """
        <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
          <System>
            <Provider Name='.NET Runtime'/>
            <EventID Qualifiers='0'>1026</EventID>
            <Execution ProcessID='47936' ThreadID='1'/>
            <Channel>Application</Channel>
          </System>
          <EventData>
            <Data>Application: Melvor2.exe
        CoreCLR Version: 10.0.1426.52326
        Description: The process was terminated due to an unhandled exception.
        Exception Info: System.InvalidOperationException: cs2dev throw thread</Data>
          </EventData>
        </Event>
        """;

    [Fact]
    public void TheHostsEntryIsFoundByItsProcessId()
    {
        var entry = CrashEventLog.Match(Entry, 47936);

        Assert.NotNull(entry);
        Assert.StartsWith(".NET Runtime 1026: Application: Melvor2.exe", entry, StringComparison.Ordinal);
        Assert.Contains("cs2dev throw thread", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherProcesssEntryIsNotTheHosts() => Assert.Null(CrashEventLog.Match(Entry, 47937));
}
