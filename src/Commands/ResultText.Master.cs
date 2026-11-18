using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Commands;

internal static partial class ResultText
{
    // A master's line in the summary: its kills and deaths over its time, or the monster it gets stuck on.
    private static void AppendMaster(StringBuilder text, TargetResult r, TimeUnit unit, PriceTable prices)
    {
        var parts = MasterParts.Counted(r);
        if (r.State != TargetState.Finished || parts.Count == 0)
        {
            if (r.Reasons.Count > 0)
                text.Append(' ').Append(r.Reasons.English(" | "));
            return;
        }
        if (MasterFigures.Stuck(parts) is { } stuck)
        {
            text.Append(" stuck on ").Append(Ids.BriefKey(Ids.PartTarget(stuck.Key))).Append(": ");
            text.Append(stuck.Reason.English());
            return;
        }
        var per = TimeUnits.Name(unit);
        var kills = Metrics.Estimate(r, Metrics.Kills, unit, prices)!;
        var deaths = Metrics.Estimate(r, Metrics.Deaths, unit, prices)!;
        text.Append(
            CultureInfo.InvariantCulture,
            $" kills/{per} {Fmt.Range(kills)} deaths/{per} {Fmt.Range(deaths)} monsters {parts.Count}"
        );
    }

    // A master's details before its figures: what it is, then each monster's share of its time, rate and deaths, and
    // the loadout of each monster given one.
    private static void AppendMasterDetails(
        StringBuilder text,
        Run run,
        TargetResult r,
        TimeUnit unit,
        PriceTable prices,
        string metricKey
    )
    {
        var parts = MasterParts.Counted(r);
        var blocked = r.Blocked ?? [];
        text.Append(
            CultureInfo.InvariantCulture,
            $"\n{Ids.BriefKey(r.Key)} · {Possessive.Of(r.Name)} tasks · {parts.Count} monsters, {blocked.Count} blocked"
        );
        if (r.Master is { } facts)
            text.Append(
                CultureInfo.InvariantCulture,
                $" · block {facts.BlockCost} coins, slots open {facts.OpenSlots} of {facts.Slots.Count}"
            );
        if (r.State != TargetState.Finished)
            return;
        if (MasterFigures.Stuck(parts) is { } stuck)
        {
            text.Append("\nstuck on ").Append(Ids.BriefKey(Ids.PartTarget(stuck.Key))).Append(": ");
            text.Append(stuck.Reason.English());
        }
        var metric = Metrics.Resolve(run, metricKey);
        var shares = MasterFigures.Shares(parts).ToDictionary(s => s.Key, s => s.Share, StringComparer.Ordinal);
        var per = TimeUnits.Name(unit);
        var loadouts = r.Loadouts ?? [];
        text.Append(
            CultureInfo.InvariantCulture,
            $"\npool (share of time · {Ids.BriefKey(metric)}/{per} · deaths/{per}{(loadouts.Count > 0 ? " · loadout" : "")}):"
        );
        foreach (var part in parts)
        {
            var rate = Metrics.Estimate(part, metric, unit, prices);
            var deaths = Metrics.Estimate(part, Metrics.Deaths, unit, prices);
            text.Append("\n  ").Append(Ids.BriefKey(Ids.PartTarget(part.Key)));
            text.Append(
                CultureInfo.InvariantCulture,
                $"  {(shares.TryGetValue(part.Key, out var share) ? share.ToString("P1", CultureInfo.InvariantCulture) : "-")}"
            );
            text.Append("  ").Append(rate != null ? Fmt.Range(rate) : Name(part.State));
            text.Append("  ").Append(deaths != null ? Fmt.Range(deaths) : "-");
            // By id: a reply stays the same whatever the player names a loadout.
            if (loadouts.TryGetValue(Ids.PartTarget(part.Key), out var slot))
                text.Append("  ").Append(Ids.Brief(slot));
        }
        foreach (var key in blocked)
            text.Append("\n  ").Append(Ids.BriefKey(key)).Append("  blocked");
    }
}
