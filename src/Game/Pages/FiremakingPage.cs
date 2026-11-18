using GamesByMalcs.Melvor2.Entities.Firemaking;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Firemaking: the bonfire's tiers in the game's order, the burning one ticked. A side lights a tier at or above its
/// own (the game raises a bonfire, never lowers it) or puts the bonfire out. A tier's bonus is listed with what
/// reaches a fight (XP gained) before what helps Firemaking only.
/// </summary>
internal static class FiremakingPage
{
    private static readonly ChangeKey Key = new(ChangeKind.Bonfire, null);

    /// <summary>
    /// The save's bonfire: its tier as its place in the chain from 1 (0 for none) under the bonfire's key, and 1
    /// under the tier's own id while it burns.
    /// </summary>
    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (!Dependencies.IsAvailable(Dependencies.Firemaking) || BonfireChange.Component(game) is not { } bonfire)
            return;
        var tiers = BonfireChange.Tiers(bonfire);
        values[Key] = bonfire.CurrentBonfire is { } current ? tiers.IndexOf(current) + 1 : 0;
        if (BonfireChange.Burning(bonfire))
            values[new ChangeKey(ChangeKind.Bonfire, bonfire.CurrentBonfire!.Id)] = 1;
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Firemaking))
            return PageReader.Unsupported(SetupPage.Firemaking);
        if (BonfireChange.Component(game) is not { } bonfire || bonfire.Bonfires.Count == 0)
            return PageReader.Page(SetupPage.Firemaking, PageState.NoData, Note.Of("No bonfires yet"));
        var tiers = BonfireChange.Tiers(bonfire);
        var held = BonfireChange.Held(bonfire);
        var savedAt = save.GetValueOrDefault(Key);
        var saved = savedAt > 0 && savedAt <= tiers.Count ? tiers[savedAt - 1] : null;
        var savedBurning = saved != null && save.GetValueOrDefault(new ChangeKey(ChangeKind.Bonfire, saved.Id)) > 0;
        var entries = tiers.Select((tier, at) => Entry(tier, at < savedAt - 1, held, savedBurning ? saved : null));
        var note =
            saved == null ? null
            : savedBurning ? Note.Of("Your bonfire: {0}, burning", Name(saved))
            : Note.Of("Your bonfire: {0}, not burning", Name(saved));
        return new SetupPage(
            SetupPage.Firemaking,
            PageSection.Skills,
            PageState.Changeable,
            note,
            PageGates.Unmet(game, BonfireChange.FiremakingId),
            [new PageGroup("bonfire", "", null, null, [.. entries], Note.Of("Bonfire"))]
        );
    }

    private static PageEntry Entry(FiremakingBonfire tier, bool below, string held, FiremakingBonfire? burning)
    {
        var name = Name(tier);
        var (combat, other) = CombatReach.Split(tier.Bonuses);
        var cost = tier.Costs.Currencies.FirstOrDefault();
        return new PageEntry(
            tier.Id,
            name,
            tier.Id,
            EntryKind.Toggle,
            held == tier.Id ? 1 : 0,
            1,
            tier == burning ? 1 : 0,
            Targets.Unmet(tier.Requirements),
            [
                .. SetupSheets.Describe(ModifierGroup.Other, tier.Id, name, combat),
                .. SetupSheets.Describe(ModifierGroup.Other, tier.Id, name, other),
            ],
            below ? null : Key,
            below ? Note.Of("The game can't lower a bonfire.") : null,
            Remark: cost == null
                ? Note.Of("Burns up to {0} h", tier.MaxDuration)
                : Note.Of(
                    "{0:N0} {1} · burns up to {2} h",
                    cost.Quantity,
                    cost.Currency.DisplayName.Text,
                    tier.MaxDuration
                )
        );
    }

    private static string Name(FiremakingBonfire tier) =>
        ((GamesByMalcs.Melvor2.Data.INamedObject)tier).DisplayName.Text;
}
