using System.Globalization;
using MelvorMods.CombatSimulator2.Engine.Dev;

namespace MelvorMods.CombatSimulator2.Engine;

// What each slot did, handed to the dev log when the run's threads have ended.
internal sealed partial class SlotClock
{
    partial void Ended()
    {
        var end = _end ?? _now();
        var lines = new List<string>();
        for (var slot = 1; slot <= Slots; slot++)
        {
            double Of(SlotWork work) => Seconds(_work[slot][(int)work]);
            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"slot {slot}: fought {_fought[slot]}, fight {Of(SlotWork.Fight):0.00} s, reset {Of(SlotWork.Reset):0.00}, build {Of(SlotWork.Build):0.00}, prepare {Of(SlotWork.Prepare):0.00}, check {Of(SlotWork.Check):0.00}, idle {Seconds(Idle(slot)):0.00}, left at {Seconds((_left[slot] ?? end) - _start):0.00} of {Seconds(end - _start):0.00}"
                )
            );
        }
        SlotLog.Keep(lines);
    }
}
