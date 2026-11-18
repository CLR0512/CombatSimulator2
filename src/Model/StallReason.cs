namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Why trials ran over the limit, in the order they are tested and listed.</summary>
public enum StallReason
{
    NoAttack,
    Immune,
    NeverHit,
    HealsBack,
    Armour,
    TooSlow,
}

/// <summary>
/// Reads the reason a target's trials ran over from what the game reported in them. Each test compares counted
/// amounts; none computes a hit chance or damage from stats.
/// </summary>
public static class StallReasons
{
    /// <summary>The share of trials running over that, once clearly reached, ends a target unfinished.</summary>
    public const double GiveUpShare = 0.05;

    /// <summary>The complete batches needed before that share is judged.</summary>
    public const int GiveUpAfterBatches = 3;

    /// <summary>
    /// Whether trials run over so often that fighting on would cost up to the limit for each of many more: the whole
    /// 95% range of the share is at or above <see cref="GiveUpShare"/>.
    /// </summary>
    public static bool TooOftenOver(IReadOnlyList<Tally> completeBatches)
    {
        ArgumentNullException.ThrowIfNull(completeBatches);
        if (completeBatches.Count < GiveUpAfterBatches)
            return false;
        var share = Estimators.Wilson(completeBatches.Sum(b => b.RanOver ?? 0), completeBatches.Sum(b => b.Trials));
        return share.Low >= GiveUpShare;
    }

    /// <summary>The reason a target ended because its trials ran over too often: how often, then why.</summary>
    public static Note GaveUp(Stall stall, int ranOver, int trials, string? enemyName = null)
    {
        ArgumentNullException.ThrowIfNull(stall);
        // Translators: How many trials passed the time limit, with why: where a target stopped for it, and in the hover of a row's ran-over chip; {2} is the limit in minutes, {3} the reason sentence.
        return Model.Note.Of(
            "{0} of {1} trials ran over {2} minutes. {3}",
            ranOver,
            trials,
            Math.Round(stall.LimitSeconds / 60, 1),
            Note(stall, enemyName)
        );
    }

    /// <summary>The first reason that holds for the stall's counts.</summary>
    public static StallReason Of(Stall stall)
    {
        ArgumentNullException.ThrowIfNull(stall);
        var dealt = stall.Counts.Dealt;
        if (dealt.Attacks == 0)
            return StallReason.NoAttack;
        if (dealt.Hits == 0)
            return stall.Immune ? StallReason.Immune : StallReason.NeverHit;
        // Healing is tested before armour: an enemy that heals everything back can't be killed whatever armour does.
        if (dealt.Damage > 0 && dealt.Healed * 2 >= dealt.Damage)
            return StallReason.HealsBack;
        if (dealt.ArmourCut > dealt.Damage)
            return StallReason.Armour;
        return StallReason.TooSlow;
    }

    /// <summary>
    /// The reason in words with the numbers that show it; a dungeon's starts with its stage and the enemy's name.
    /// </summary>
    public static Note Note(Stall stall, string? enemyName = null)
    {
        ArgumentNullException.ThrowIfNull(stall);
        var dealt = stall.Counts.Dealt;
        var minutes = Math.Round(stall.LimitSeconds / 60, 1);
        var hpLeft = (long)Math.Round(stall.HpLeftShare * 100);
        var reason = Of(stall) switch
        {
            // Translators: Why a target has no result: the player made no attack within the time limit ({0} minutes).
            StallReason.NoAttack => Model.Note.Of("You never attacked in {0} minutes.", minutes),
            // Translators: Why a target has no result: the game marks the enemy immune; {0} is the number of attacks made.
            StallReason.Immune => Model.Note.Of("Immune to your attacks: 0 of {0} landed.", dealt.Attacks),
            // Translators: Why a target has no result: no attack landed; {0} is the number of attacks made.
            StallReason.NeverHit => Model.Note.Of("Never hit it: 0 of {0} attacks landed.", dealt.Attacks),
            // Translators: Why a target has no result: the enemy heals back about what it is dealt; {0} damage dealt, {1} healed, {2} percent of its health left.
            StallReason.HealsBack => Model.Note.Of(
                "It heals back what you deal: {0} dealt, {1} healed, {2}% HP left.",
                dealt.Damage,
                dealt.Healed,
                hpLeft
            ),
            // Translators: Why a target has no result: the enemy's armour takes nearly every hit; {0} hits, {1} damage done, {2} percent of its health left.
            StallReason.Armour => Model.Note.Of(
                "Armour stops your hits: {0} hits did {1} damage, {2}% HP left.",
                dealt.Hits,
                dealt.Damage,
                hpLeft
            ),
            // Translators: Why a target has no result: the fight is too slow for the time limit; {0} percent health left, {1} minutes, {2} of {3} attacks landed, {4} damage per hit.
            _ => Model.Note.Of(
                "Too slow: {0}% HP left after {1} minutes. {2} of {3} attacks landed, {4} damage a hit.",
                hpLeft,
                minutes,
                dealt.Hits,
                dealt.Attacks,
                (long)Math.Round((double)dealt.Attack / dealt.Hits)
            ),
        };
        return stall.Stage is { } stage
            // Translators: Before a dungeon's stall reason: {0} the stage number, {1} the enemy's name, {2} the reason sentence.
            ? Model.Note.Of("Stage {0}, {1}: {2}", stage, enemyName ?? stall.Enemy ?? "", reason)
            : reason;
    }

    /// <summary>The heading of a group of targets with this reason, with their count.</summary>
    public static Note Heading(StallReason reason, int count) =>
        reason switch
        {
            // Translators: Heading of a group of results with no attack made; {0} is how many targets. The other headings work the same way.
            StallReason.NoAttack => Model.Note.Of("Never attacked ({0})", count),
            StallReason.Immune => Model.Note.Of("Immune ({0})", count),
            StallReason.NeverHit => Model.Note.Of("Never hit ({0})", count),
            StallReason.HealsBack => Model.Note.Of("Heals back ({0})", count),
            StallReason.Armour => Model.Note.Of("Armour stops your hits ({0})", count),
            _ => Model.Note.Of("Too slow ({0})", count),
        };

    /// <summary>The reason in a few words, as its heading says it without the count: "Too slow".</summary>
    public static Note Brief(StallReason reason) =>
        reason switch
        {
            // Translators: The reason in a few words, in a comparison row's verdict place, e.g. "A: Too slow". Same words as the group headings.
            StallReason.NoAttack => Model.Note.Of("Never attacked"),
            StallReason.Immune => Model.Note.Of("Immune"),
            StallReason.NeverHit => Model.Note.Of("Never hit"),
            StallReason.HealsBack => Model.Note.Of("Heals back"),
            StallReason.Armour => Model.Note.Of("Armour stops your hits"),
            _ => Model.Note.Of("Too slow"),
        };

    /// <summary>The reason's word for <c>cs2</c>'s group lines and the bridge.</summary>
    public static string Word(StallReason reason) =>
        reason switch
        {
            StallReason.NoAttack => "no attack made",
            StallReason.Immune => "immune",
            StallReason.NeverHit => "never hit",
            StallReason.HealsBack => "heals back",
            StallReason.Armour => "armour",
            _ => "too slow",
        };
}
