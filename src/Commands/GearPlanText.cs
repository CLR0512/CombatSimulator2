using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>What <c>cs2 gear plan</c> prints, in English.</summary>
internal static class GearPlanText
{
    /// <summary>
    /// <c>cs2 gear plan</c>: the sets the gear search would fight, each kind's options a pass adds, and how long it
    /// is expected to take against the long-search limit.
    /// </summary>
    public static string Of(PlanPreview plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var text = new StringBuilder();
        if (plan.Pool == ItemPool.Every)
        {
            text.Append("pool: every item\n");
            foreach (var count in plan.PoolCounts)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"{Model.Ids.Brief(count.SlotType)}: {count.Items} (owned {count.Owned}, wearable {count.Wearable})\n"
                );
        }
        text.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"sets: {plan.Possible:#,0} possible, {plan.RuledOut:#,0} ruled out, {(plan.Climb ? "too many" : plan.ToFight.ToString("#,0", CultureInfo.InvariantCulture))} to simulate, {plan.LeftOut} left out"
            )
        );
        foreach (var skipped in plan.SkippedStyles)
            text.Append(CultureInfo.InvariantCulture, $"\nstyle {skipped.Style}: skipped, {skipped.Why.English()}");
        // Only the kinds switched on: the others are counted for the panel's switches.
        foreach (var kind in plan.Kinds.Where(k => plan.On.Contains(k.Kind)))
        {
            var name =
                kind.Kind == ChoiceKind.Toolbelt && kind.Name is { Length: > 0 } slot && char.IsDigit(slot[^1])
                    ? "utility " + slot[^1]
                    : kind.Name;
            text.Append(CultureInfo.InvariantCulture, $"\n{name}: {kind.Options} to try");
            if (kind.Kind == ChoiceKind.Prayers)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $", {kind.Slots} slot{(kind.Slots == 1 ? "" : "s")}, {kind.Pairs} pairs at most"
                );
            if (kind.Kind == ChoiceKind.Potion && kind.Options == 0)
                text.Append(plan.PotionsInBuild ? ": you have no combat potions" : ": no combat potions in this build");
        }
        if (plan.NothingToSearch)
            text.Append("\nnothing to search: nothing else owned fits an unlocked slot");
        else if (plan.Climb)
        {
            text.Append("\na slot at a time: too many sets to fight them all");
            if (plan.ClimbSeconds is { } climbing)
                text.Append(CultureInfo.InvariantCulture, $"\nexpected: about {Math.Ceiling(climbing):0} s")
                    .Append(
                        plan.MaxSeconds is { } max && climbing > max
                            ? string.Create(
                                CultureInfo.InvariantCulture,
                                $"\nover the limit: it stops at {max:0} s and keeps the best set found"
                            )
                            : ""
                    );
        }
        if (plan.Seconds is { } seconds)
            text.Append(CultureInfo.InvariantCulture, $"\nexpected: about {Math.Ceiling(seconds):0} s")
                .Append(
                    plan.OverLimit ? "\nover the limit: a slot at a time unless told otherwise (short)"
                    : plan.KindsOverLimit
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"\nover the limit: longer than {plan.LimitSeconds:0} s; switch kinds off to shorten it"
                        )
                    : ""
                );
        else if (!plan.NothingToSearch && !plan.Climb)
            text.Append("\nexpected: not timed yet for this kind of target");
        return text.ToString();
    }
}
