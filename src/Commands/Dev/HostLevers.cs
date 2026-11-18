using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Host.Dev;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev host</c>: the host's handshake, memory and warm stock, a forced collection, its fault levers and
/// overrides.
/// </summary>
internal static class HostLevers
{
    /// <summary>
    /// <c>cs2dev batches on|off|dump</c>: the record of which copy fought which target after which, for telling why
    /// two runs with one seed differ.
    /// </summary>
    public static void Batches(CommandCall call, CommandArgs args)
    {
        var root = HostClient.DataRoot ?? throw new CommandException("the data folder isn't known yet");
        switch (args.Word(0))
        {
            case "on":
                FightLog.On(root);
                call.Ok("recording fights from now");
                return;
            case "off":
                FightLog.Off();
                call.Ok("record ended");
                return;
            case "dump":
                var lines = FightLog.Dump(root) ?? throw new CommandException("not recording: cs2dev batches on");
                var folder = Path.Combine(root, "dumps");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(
                    folder,
                    string.Create(CultureInfo.InvariantCulture, $"batches-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt")
                );
                File.WriteAllLines(path, lines);
                call.Ok($"{lines.Count} fights {path}");
                return;
            default:
                throw new CommandException(
                    $"usage: cs2dev batches on|off|dump (recording: {(FightLog.IsOn ? "on" : "off")})"
                );
        }
    }

    public static void Run(CommandCall call, CommandArgs args)
    {
        var host = HostClient.Instance;
        if (args.String("idle") is not null)
        {
            var seconds = args.Int("idle", 120, 5, 86_400);
            host.Idle.ClosedLimit = TimeSpan.FromSeconds(seconds);
            call.Ok($"idle stop {seconds} s after the panel closes");
            return;
        }
        if (args.String("idle-open") is not null)
        {
            var seconds = args.Int("idle-open", 900, 5, 86_400);
            host.Idle.OpenLimit = TimeSpan.FromSeconds(seconds);
            call.Ok($"idle stop after {seconds} s with the panel open");
            return;
        }
        if (args.String("release") is not null)
        {
            var seconds = args.Int("release", 60, 1, 86_400);
            RunService.SetReleaseDelay(TimeSpan.FromSeconds(seconds));
            call.Ok($"warm stock dropped {seconds} s after the panel closes");
            return;
        }
        if (args.String("reserve") is { } gb)
        {
            if (
                !double.TryParse(gb, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || value < 0
                || value > 1024
            )
                throw new CommandException("reserve=<GB>, for example reserve=1 or reserve=6.5");
            host.SetReserveBytes((long)(value * 1024 * 1024 * 1024));
            call.Ok(string.Create(CultureInfo.InvariantCulture, $"reserve {value:0.##} GB for the next runs"));
            return;
        }
        if (args.String("cores") is not null)
        {
            var cores = args.Int("cores", 0, 0, 63);
            DefaultCopies.CoresOverride = cores;
            if (cores > 0)
                EnvironmentOverrides.Variables[ProcessorCount] = cores.ToString(CultureInfo.InvariantCulture);
            else
                EnvironmentOverrides.Variables.Remove(ProcessorCount);
            call.Ok(
                cores > 0
                    ? $"as on {cores} cores: {DefaultCopies.Now} copies by default; the next host is told {cores} processors and held to {cores} cores (cs2dev host stop to start one)"
                    : $"the machine's own cores again: {DefaultCopies.Now} copies by default (cs2dev host stop to start a host on them)"
            );
            return;
        }
        if (args.String("standins") is { } standIns)
        {
            if (standIns == "off")
                EnvironmentOverrides.Variables[HostStandIns.OffVariable] = "1";
            else if (standIns == "on")
                EnvironmentOverrides.Variables.Remove(HostStandIns.OffVariable);
            else
                throw new CommandException("standins=off|on");
            call.Ok($"the next host starts with stand-ins {standIns}");
            return;
        }
        if (args.String("stock") is not null)
        {
            var stock = args.Int("stock", 0, 0, 16);
            DefaultCopies.StockOverride = stock > 0 ? stock : null;
            call.Ok(
                stock > 0 ? $"the next warm-ups ask for {stock} bare copies" : "the next warm-ups ask for the default"
            );
            return;
        }
        if (args.Word(0) == "env")
        {
            // NAME:value sets a variable, NAME: takes it out, clear forgets every override.
            foreach (var word in args.Words.Skip(1))
            {
                if (word == "clear")
                    EnvironmentOverrides.Variables.Clear();
                else if (word.Split(':', 2) is [var name, var value])
                    EnvironmentOverrides.Variables[name] = value;
                else
                    throw new CommandException("usage: cs2dev host env <NAME>:<value>… | cs2dev host env clear");
            }
            var set = EnvironmentOverrides.Variables;
            call.Ok(
                "next hosts start with: "
                    + (set.Count == 0 ? "no overrides" : string.Join(" ", set.Select(o => $"{o.Key}={o.Value}")))
            );
            return;
        }
        switch (args.Word(0) ?? "status")
        {
            case "status":
                var text = new StringBuilder(HostStatusText.Of(host));
                if (host.Hello is { } hello)
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\nhandshake build {hello.Build} game {hello.GameMvid} cs2 {hello.Cs2Mvid} protocol {hello.Protocol}"
                    );
                if (host.StartSeconds is { } started)
                    text.Append(CultureInfo.InvariantCulture, $"\nstarted in {started:0.0} s");
                if (host.HeartbeatAge is { } age)
                    text.Append(CultureInfo.InvariantCulture, $"\nheartbeat {age.TotalSeconds:0.0} s ago");
                if (host.LastBeat is { } beat)
                    text.Append(
                            CultureInfo.InvariantCulture,
                            $"\navailable {beat.AvailableBytes / 1048576} MB, reserve {host.ReserveBytes / 1048576} MB, cpu {beat.CpuPercent:0}%"
                        )
                        .Append(
                            CultureInfo.InvariantCulture,
                            $"\nheap {Mb(beat.HeapBytes)} MB committed {Mb(beat.HeapCommittedBytes)} MB native {Mb(beat.PrivateBytes - beat.HeapCommittedBytes)} MB run-peak {Mb(beat.RunPeakPrivateBytes)} MB"
                        );
                if (host.Collections.Last is { } c)
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\ncollected {(DateTime.UtcNow - c.At).TotalSeconds:0} s ago in {c.State}: {Mb(c.Before.PrivateBytes)} → {Mb(c.After.PrivateBytes)} MB private, {Mb(c.Before.HeapBytes)} → {Mb(c.After.HeapBytes)} MB heap, {Mb(c.Before.HeapCommittedBytes)} → {Mb(c.After.HeapCommittedBytes)} MB committed, freed {Mb(c.Freed)} MB"
                    );
                call.Ok(text.ToString());
                break;
            case "collect":
                if (!host.Collections.Ask(host))
                    throw new CommandException("the host isn't running, or hasn't sent a heartbeat yet");
                call.Ok("host told to collect");
                break;
            case "start":
                // A host with no stock and no run: the panel isn't told, so nothing is warmed.
                Task.Run(() =>
                {
                    try
                    {
                        host.Ensure();
                    }
                    catch (HostException e)
                    {
                        Common.Log.Warn($"host start: {e.Reason.English()}");
                    }
                });
                call.Ok("host starting with no stock (cs2dev host status)");
                break;
            case "warm":
                RunService.Instance.Warm();
                call.Ok($"warming: {Engine.DefaultCopies.Now} bare copies asked (cs2dev host stock)");
                break;
            case "stock":
                var reading = host.Reading;
                call.Ok(
                    reading == null
                        ? $"host {HostHealth.Name(host.State)}, no heartbeat"
                        : $"stock {reading.Stock}/{reading.StockTarget} {reading.Warm}"
                );
                break;
            case "kill":
                var pid = host.Pid ?? throw new CommandException("the host isn't running");
                host.Kill();
                call.Ok($"killed host pid {pid}");
                break;
            case "hang":
                if (!host.Hang())
                    throw new CommandException("the host isn't running");
                RunService.Instance.LeverSent("hang");
                call.Ok("host told to hang");
                break;
            case "crash":
                if (!host.Crash())
                    throw new CommandException("the host isn't running");
                RunService.Instance.LeverSent("crash");
                call.Ok("host told to crash");
                break;
            case "throw" or "failfast" or "overflow" or "av":
                var action = args.Word(0) == "throw" && args.Word(1) is { } where ? $"throw {where}" : args.Word(0)!;
                if (!host.Lever(action))
                    throw new CommandException("the host isn't running");
                RunService.Instance.LeverSent(action);
                call.Ok($"host told to {action}");
                break;
            case "fail-start":
                host.FailNextStart = true;
                call.Ok("the next host start uses a missing exe");
                break;
            case "mismatch":
                host.Stop();
                host.Mismatch = true;
                call.Ok("the next host reports another CS2 build until cs2dev host stop");
                break;
            case "stop":
                var stopped = host.Pid;
                host.Stop();
                call.Ok(stopped is { } p ? $"host pid {p} stopped" : "host stopped (it wasn't running)");
                break;
            default:
                throw new CommandException(
                    "usage: cs2dev host [status|start|warm|stock|collect|env <NAME>:<value>…|env clear|kill|hang|crash|throw [build|thread|<target>]|failfast|overflow|av|fail-start|mismatch|stop|reserve=<GB>|idle=<s>|idle-open=<s>|release=<s>|cores=<n>|standins=off|on|stock=<n>]"
                );
        }
    }

    // The runtime's own name for the processor count it reports and sizes its thread pool and collector by.
    private const string ProcessorCount = "DOTNET_PROCESSOR_COUNT";

    private static long Mb(long bytes) => bytes / 1048576;
}
