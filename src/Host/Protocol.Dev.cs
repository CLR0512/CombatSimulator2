namespace MelvorMods.CombatSimulator2.Host;

/// <summary>A dev lever for the host, by name: a fault (<c>hang</c>, <c>crash</c>, …) or <c>collect</c>.</summary>
internal sealed record DevMessage(string Action);
