using System.Globalization;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Reads a setup's stats and modifiers from a prepared copy: the stats the game's own out-of-combat view shows, in
/// its groups and order (hit chance and resistances need an enemy, so they are left out as the game leaves them
/// out), and every modifier source with the game's own description of each value. Nothing ticks.
/// </summary>
internal static class SetupSheets
{
    /// <summary>Reads <paramref name="combat"/> in <paramref name="game"/>, a copy prepared with the setup.</summary>
    public static SetupSheet Read(MelvorGame game, CombatEntity combat, Setup setup, string? setupHash) =>
        new(setup, setupHash, LiveGame.GameBuild, Stats(combat), Modifiers(game, combat), DateTime.UtcNow);

    /// <summary>Every stat of <see cref="Read"/> as "key=text", to tell whether two moments show the same stats.</summary>
    public static List<string> StatsText(CombatEntity combat) =>
        Dependencies.IsAvailable(Dependencies.SetupSheet)
            ? [.. Stats(combat).Select(line => $"{line.Key}={line.Text}")]
            : [];

    private static List<StatLine> Stats(CombatEntity combat)
    {
        var player = combat.Player;
        var stats = player.Stats;
        var lines = new List<StatLine>();
        void Number(string key, StatGroup group, Note name, double value, string text) =>
            lines.Add(new StatLine(key, group, name, value, text));
        void Named(string key, StatGroup group, Note name, string text) =>
            lines.Add(new StatLine(key, group, name, null, text));

        foreach (var (level, node) in stats.ModifiedCombatLevels.Dict.OrderBy(l => l.Key.DisplayPriority))
            Number("level:" + level.Id, StatGroup.Levels, Note.Text(level.DisplayName.Text), node.Value, I(node.Value));

        var style = stats.CombatStyle.Value;
        var stance = stats.AttackStance.Value;
        var combatLevel = style.ComputeCombatLevel(player.CombatLevels);
        Number("combat-level", StatGroup.Offence, Note.Of("Combat Level"), combatLevel, I(combatLevel));
        Named("style", StatGroup.Offence, Note.Of("Combat Style"), style.DisplayName.Text);
        Named("stance", StatGroup.Offence, Note.Of("Attack Stance"), stance.DisplayName.Text);
        Named("element", StatGroup.Offence, Note.Of("Element"), stats.Element.Value.DisplayName.Text);
        using (var hits = new AllAttacksDamageWatcher(player))
        {
            Number("min-hit", StatGroup.Offence, Note.Of("Min Hit"), hits.Minimum, I(hits.Minimum));
            Number("max-hit", StatGroup.Offence, Note.Of("Max Hit"), hits.Maximum, I(hits.Maximum));
        }
        var accuracy = stats.AccuracyRating.Value;
        Number(
            "accuracy",
            StatGroup.Offence,
            Note.Of("Accuracy Rating ({0})", Note.Text(stance.DisplayName.Text)),
            accuracy,
            I(accuracy)
        );
        var interval = stats.AttackInterval.Value / 1000;
        Number(
            "attack-interval",
            StatGroup.Offence,
            Note.Of("Attack Interval"),
            interval,
            interval.ToString("0.00", CultureInfo.InvariantCulture) + "s"
        );
        foreach (var type in combat.DamageTypeDisplayOrder)
        {
            if (stats.PowerRatings.Dict.TryGetValue(type, out var power))
                Number(
                    "power:" + type.Id,
                    StatGroup.Offence,
                    Note.Of("Power ({0})", Note.Text(type.ResistanceNameShort.Text)),
                    power.Value,
                    I(power.Value)
                );
        }

        var hp = stats.MaxHitpoints.Value;
        Number("max-hp", StatGroup.Defence, Note.Of("Max Hitpoints"), hp, I(hp));
        foreach (var evasionStance in combat.Config.AttackStances)
        {
            if (stats.EvasionRatings.Dict.TryGetValue(evasionStance, out var evasion))
                Number(
                    "evasion:" + evasionStance.Id,
                    StatGroup.Defence,
                    Note.Of("Evasion ({0})", Note.Text(evasionStance.DisplayName.Text)),
                    evasion.Value,
                    I(evasion.Value)
                );
        }
        foreach (var type in combat.DamageTypeDisplayOrder)
        {
            if (stats.ProtectionRatings.Dict.TryGetValue(type, out var protection))
                Number(
                    "protection:" + type.Id,
                    StatGroup.Defence,
                    Note.Of("Protection ({0})", Note.Text(type.ResistanceNameShort.Text)),
                    protection.Value,
                    I(protection.Value)
                );
        }

        Number(
            "crit-chance",
            StatGroup.Other,
            Note.Of("Critical Hit Chance"),
            stats.CriticalHitChance.Value,
            Percent(stats.CriticalHitChance.Value)
        );
        Number(
            "crit-multiplier",
            StatGroup.Other,
            Note.Of("Critical Hit Multiplier"),
            stats.CriticalHitMultiplier.Value,
            Percent(stats.CriticalHitMultiplier.Value)
        );
        var reflect = stats.PercentAttackReflect.Total.Value;
        Number("reflect", StatGroup.Other, Note.Of("Attack Reflect"), reflect, Percent(reflect));
        var light = stats.FlatLight.Total.Value;
        Number("light", StatGroup.Other, Note.Of("Light"), light, light.ToString("0.##", CultureInfo.InvariantCulture));

        // The equipment stat totals, offensive categories then defensive, each stat in its display order.
        var all = combat.Equipment.CombatStatsRegistry.GetAllObjects().ToList();
        foreach (var category in combat.Config.OffensiveStatCategories.Concat(combat.Config.DefensiveStatCategories))
        {
            foreach (var stat in all.Where(s => s.Category == category).OrderBy(s => s.DisplayPriority))
            {
                if (!stats.ModifiedCombatStatTotals.Dict.TryGetValue(stat, out var total))
                    continue;
                Number(
                    "stat:" + stat.Id,
                    StatGroup.Totals,
                    Note.Text(stat.DisplayName.Text),
                    total.Value,
                    stat.NumberFormat.Format(total.Value)
                );
            }
        }
        return lines;
    }

    private static string I(double value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    // As the game's percent labels show a fraction.
    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static List<ModifierLine> Modifiers(MelvorGame game, CombatEntity combat)
    {
        var lines = new List<ModifierLine>();
        foreach (var (source, values) in game.Modifiers.Values)
        {
            var group = GroupOf(source, combat);
            var id = (source as INamespacedObject)?.Id ?? source.GetType().Name;
            var name = Targets.Plain(source.DisplayName.Text);
            foreach (var (modifiers, multiplier) in values)
                lines.AddRange(Describe(group, id, name, modifiers, multiplier));
        }
        // Grouped in the order shown, sources in the game's own order within a group.
        return
        [
            .. lines.Select((line, i) => (line, i)).OrderBy(x => x.line.Group).ThenBy(x => x.i).Select(x => x.line),
        ];
    }

    /// <summary>
    /// One line per value of <paramref name="modifiers"/>, described as the game describes it, for the source
    /// <paramref name="id"/>: what a page shows a star, a purchase or a bonus gives, in force or not.
    /// </summary>
    public static List<ModifierLine> Describe(
        ModifierGroup group,
        string id,
        string name,
        ModifierValues modifiers,
        double multiplier = 1
    )
    {
        var lines = new List<ModifierLine>();
        foreach (var value in modifiers.Global)
            lines.Add(Line(group, id, name, value, multiplier));
        foreach (var value in modifiers.Component)
            lines.Add(Line(group, id, name, value, multiplier));
        foreach (var effect in modifiers.CombatEffect)
        {
            var text = Targets.Plain(
                effect.Modifier.Effect.GetDescription(effect.Modifier.AppliesTo, effect.Chance * multiplier)
            );
            lines.Add(
                new ModifierLine(
                    group,
                    id,
                    name,
                    text + Conditions(effect.Condition, effect.Scaling),
                    Sign(effect.Modifier.Kind),
                    effect.IsEnemy
                )
            );
        }
        return lines;
    }

    private static ModifierLine Line(
        ModifierGroup group,
        string id,
        string name,
        IModifierValue value,
        double multiplier
    ) =>
        new(
            group,
            id,
            name,
            Targets.Plain(value.GetDescription(multiplier)) + Conditions(value.Condition, value.Scaling),
            Sign(value.Kind),
            value.IsEnemy
        );

    // A value that applies only when a condition holds, or scales with something, says so after its description.
    private static string Conditions(
        GamesByMalcs.Melvor2.Conditions.Condition? condition,
        GamesByMalcs.Melvor2.ScalingFactors.ScalingFactor? scaling
    )
    {
        var text = "";
        if (condition != null)
        {
            var when = Targets.Plain(condition.GetDescription(centered: false, coloured: false));
            if (when.Length > 0)
                text += " (" + when + ")";
        }
        if (scaling != null)
        {
            var per = Targets.Plain(scaling.PerDescription);
            if (per.Length > 0)
                text += " " + per;
        }
        return text;
    }

    private static ModifierSign Sign(ModifierKind kind) =>
        kind switch
        {
            ModifierKind.Positive => ModifierSign.Positive,
            ModifierKind.Negative => ModifierSign.Negative,
            _ => ModifierSign.Neutral,
        };

    // An item belongs where the copy holds it; other sources by their kind, read from the type's name so a type a
    // build lacks only falls to Other.
    private static ModifierGroup GroupOf(object source, CombatEntity combat)
    {
        if (source is Item item)
        {
            if (combat.Player.Equipment._activeSlots.Values.Any(s => s.Equipped?.Item == item))
                return ModifierGroup.Equipment;
            if (combat.Toolbelt._activeSlots.Values.Any(s => s.Equipped?.Item == item))
                return ModifierGroup.Toolbelt;
            if (combat.Food.SelectedFood?.Item == item)
                return ModifierGroup.Food;
            return ModifierGroup.Other;
        }
        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var name = type.Name;
            switch (name)
            {
                case "EquipmentSetBonus":
                    return ModifierGroup.SetBonus;
                case "Prayer":
                    return ModifierGroup.Prayer;
                case "Pet":
                    return ModifierGroup.Pet;
                case "AttackStance" or "CombatStyle" or "Element" or "BaseMagicSpell" or "CombatPassive":
                    return ModifierGroup.StanceSpell;
                case "ShopPurchase":
                    return ModifierGroup.Shop;
                case "BaseCombatArea" or "CombatAreaCategory" or "AreaEffect":
                    return ModifierGroup.Area;
            }
            if (name.Contains("Mastery", StringComparison.Ordinal))
                return ModifierGroup.Mastery;
            if (name.Contains("Agility", StringComparison.Ordinal))
                return ModifierGroup.Agility;
            if (
                name.Contains("Astrology", StringComparison.Ordinal)
                || name.Contains("Constellation", StringComparison.Ordinal)
            )
                return ModifierGroup.Astrology;
        }
        return ModifierGroup.Other;
    }
}
