using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// One fighter's attacks on the other, as the game reported them. Whole numbers; <see cref="Hits"/> is never above
/// <see cref="Attacks"/>, and no field is below 0.
/// </summary>
public sealed class SideCounts
{
    /// <summary>Attack events: each is rolled on its own, so an enemy that attacks several times a turn has several.</summary>
    public long Attacks { get; set; }

    /// <summary>Attacks that landed.</summary>
    public long Hits { get; set; }

    /// <summary>Health removed by attacks.</summary>
    public long Attack { get; set; }

    /// <summary>Health removed by combat effects (damage over time and the like).</summary>
    public long Effect { get; set; }

    /// <summary>Health removed by reflected damage.</summary>
    public long Reflect { get; set; }

    /// <summary>The part of the losses above the health that was left.</summary>
    public long Overkill { get; set; }

    /// <summary>What resistance took off the losses.</summary>
    public long Resisted { get; set; }

    /// <summary>What the ward took off the losses.</summary>
    public long WardCut { get; set; }

    /// <summary>What armour took off the losses.</summary>
    public long ArmourCut { get; set; }

    /// <summary>Ward removed by landed attacks.</summary>
    public long WardDamage { get; set; }

    /// <summary>
    /// Health the defender got back. Kept for the enemy only: the player's healing is already counted as food used.
    /// </summary>
    public long Healed { get; set; }

    /// <summary>Health removed from every cause.</summary>
    [JsonIgnore]
    public long Damage => Attack + Effect + Reflect;

    public void Add(SideCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Attacks += other.Attacks;
        Hits += other.Hits;
        Attack += other.Attack;
        Effect += other.Effect;
        Reflect += other.Reflect;
        Overkill += other.Overkill;
        Resisted += other.Resisted;
        WardCut += other.WardCut;
        ArmourCut += other.ArmourCut;
        WardDamage += other.WardDamage;
        Healed += other.Healed;
    }

    public void Clear()
    {
        Attacks = Hits = 0;
        Attack = Effect = Reflect = Overkill = 0;
        Resisted = WardCut = ArmourCut = WardDamage = Healed = 0;
    }
}

/// <summary>What the game reported in a set of fights: both fighters' attacks and the time spent fighting.</summary>
public sealed class FightCounts
{
    /// <summary>The player's attacks on the enemy.</summary>
    public SideCounts Dealt { get; set; } = new();

    /// <summary>The enemy's attacks on the player.</summary>
    public SideCounts Taken { get; set; } = new();

    /// <summary>
    /// Game seconds from each enemy's spawn to its fight's end, so the wait for the next spawn, which depends on the
    /// area and not the setup, stays out of damage per second.
    /// </summary>
    public double Seconds { get; set; }

    public void Add(FightCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Dealt.Add(other.Dealt);
        Taken.Add(other.Taken);
        Seconds += other.Seconds;
    }

    public void Clear()
    {
        Dealt.Clear();
        Taken.Clear();
        Seconds = 0;
    }

    public FightCounts Copy()
    {
        var copy = new FightCounts();
        copy.Add(this);
        return copy;
    }
}
