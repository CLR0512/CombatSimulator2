using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>What is simulated. Identity = (character, loadout, ignoreRequirements, changes).</summary>
public sealed class Setup
{
    private ChangeList? _changes;

    public string Character { get; set; } = "";

    /// <summary>The loadout simulated: <c>current</c>, or a loadout slot id.</summary>
    public string Loadout { get; set; } = Current;
    public string? LoadoutName { get; set; }
    public bool IgnoreRequirements { get; set; }

    public const string Current = "current";

    /// <summary>
    /// Changes tried for the simulation only, applied in each copy after the loadout; null for none (an empty list is
    /// kept as null, so a setup without changes is written and identified as before changes existed).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChangeList? Changes
    {
        get => _changes;
        set => _changes = value is { IsEmpty: false } ? value : null;
    }

    public string Identity =>
        $"{Character}/{Loadout}{(IgnoreRequirements ? "/ignore-req" : "")}{(Changes is { } c ? "/changes:" + c.ShortForm : "")}";

    /// <summary>The loadout's name, or <c>current</c>; the panel shows the requirements flag beside it.</summary>
    [JsonIgnore]
    public string Name => Loadout == Current ? Current : LoadoutName ?? Loadout;

    /// <summary>The setup in English, for replies and logs: the name, and whether requirements are ignored.</summary>
    [JsonIgnore]
    public string Label => Name + (IgnoreRequirements ? RequirementsIgnoredSuffix : "");

    /// <summary>How runs saved before notes marked an ignored-requirements setup in their label.</summary>
    public const string RequirementsIgnoredSuffix = " (requirements ignored)";
}
