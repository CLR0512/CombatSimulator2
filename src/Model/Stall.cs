using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// The trials of a target that ran over the trial limit, taken together: what the game reported in them and how far
/// the enemy was from dying when each was stopped.
/// </summary>
public sealed class Stall
{
    /// <summary>How many trials ran over.</summary>
    public int Trials { get; set; }

    /// <summary>What the game reported in those trials, added together.</summary>
    public FightCounts Counts { get; set; } = new();

    /// <summary>The enemy's health left when each was stopped, added over those trials.</summary>
    public long HpLeft { get; set; }

    /// <summary>The enemy's maximum health, added over those trials.</summary>
    public long HpMax { get; set; }

    /// <summary>The enemy's ward left, added over those trials.</summary>
    public long WardLeft { get; set; }

    /// <summary>Whether the game marked the enemy immune to the player's attacks in any of them.</summary>
    public bool Immune { get; set; }

    /// <summary>The trial limit that was passed, in game seconds.</summary>
    public double LimitSeconds { get; set; }

    /// <summary>For a dungeon: the stage, from 1, of the first trial that ran over; null for a monster.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Stage { get; set; }

    /// <summary>For a dungeon: the enemy in front of the player in the first trial that ran over.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Enemy { get; set; }

    /// <summary>The share of the enemy's health left, over every trial that ran over.</summary>
    [JsonIgnore]
    public double HpLeftShare => HpMax > 0 ? (double)HpLeft / HpMax : 0;

    /// <summary>Adds a further trial that ran over; the first one's stage and enemy stay.</summary>
    public void Add(Stall trial)
    {
        ArgumentNullException.ThrowIfNull(trial);
        Trials += trial.Trials;
        Counts.Add(trial.Counts);
        HpLeft += trial.HpLeft;
        HpMax += trial.HpMax;
        WardLeft += trial.WardLeft;
        Immune |= trial.Immune;
        LimitSeconds = trial.LimitSeconds;
        Stage ??= trial.Stage;
        Enemy ??= trial.Enemy;
    }
}
