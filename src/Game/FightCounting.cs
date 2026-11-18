using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Counts what the copy's own fight events report: both fighters' attacks, the enemy's health lost by cause and
/// healed, and the player's health lost by cause. Counts come from these events alone, never from watching health:
/// an enemy removed when the player dies loses its health with no event, and that is not damage.
/// </summary>
internal sealed class FightCounting : IDisposable
{
    private readonly CombatEntity _combat;
    private readonly Tallying _tallying;

    public FightCounting(Copy copy, Tallying tallying)
    {
        _combat = copy.Combat;
        _tallying = tallying;
        _combat.Player.Attacked += PlayerAttacked;
        _combat.Enemy.Attacked += EnemyAttacked;
        _combat.Enemy.Health.Damaged += EnemyDamaged;
        _combat.Enemy.Health.Healed += EnemyHealed;
        _combat.Player.GlobalHealth.Damaged += PlayerDamaged;
    }

    /// <summary>The open trial's counts; the driver takes them at each trial's end.</summary>
    public FightCounts Trial { get; } = new();

    /// <summary>The open dungeon fight's counts; the driver takes them at each fight's end.</summary>
    public FightCounts Fight { get; } = new();

    private void PlayerAttacked(object? sender, AttackEventArgs e)
    {
        if (_tallying.Writing)
            return;
        Attack(Trial.Dealt, e);
        Attack(Fight.Dealt, e);
    }

    private void EnemyAttacked(object? sender, AttackEventArgs e)
    {
        if (_tallying.Writing)
            return;
        Attack(Trial.Taken, e);
        Attack(Fight.Taken, e);
    }

    private void EnemyDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (_tallying.Writing)
            return;
        Loss(Trial.Dealt, e);
        Loss(Fight.Dealt, e);
    }

    private void EnemyHealed(object? sender, HealthEventArgs e)
    {
        if (_tallying.Writing)
            return;
        Trial.Dealt.Healed += e.Amount;
        Fight.Dealt.Healed += e.Amount;
    }

    // The player's inner health, as for risk: every cause of loss arrives here, damage over time included.
    private void PlayerDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (_tallying.Writing)
            return;
        Loss(Trial.Taken, e);
        Loss(Fight.Taken, e);
    }

    private static void Attack(SideCounts side, AttackEventArgs e)
    {
        side.Attacks++;
        if (!e.WasHit)
            return;
        side.Hits++;
        side.WardDamage += e.WardDamage;
    }

    private static void Loss(SideCounts side, HealthDamagedEventArgs e)
    {
        switch (e.Cause)
        {
            case HealthChangeCause.Attack:
                side.Attack += e.Amount;
                break;
            case HealthChangeCause.CombatEffect:
                side.Effect += e.Amount;
                break;
            case HealthChangeCause.Reflect:
                side.Reflect += e.Amount;
                break;
            default:
                return;
        }
        var damage = e.Damage;
        side.Overkill += damage.Modified - e.Amount;
        // The game gives resistance as a negative change to the damage; a weakness would make it positive and isn't
        // something resistance took off.
        side.Resisted += Math.Max(0, -damage.ResistanceReduction);
        side.WardCut += damage.WardReduction;
        side.ArmourCut += damage.ArmourReduction;
    }

    public void Dispose()
    {
        _combat.Player.Attacked -= PlayerAttacked;
        _combat.Enemy.Attacked -= EnemyAttacked;
        _combat.Enemy.Health.Damaged -= EnemyDamaged;
        _combat.Enemy.Health.Healed -= EnemyHealed;
        _combat.Player.GlobalHealth.Damaged -= PlayerDamaged;
    }
}
