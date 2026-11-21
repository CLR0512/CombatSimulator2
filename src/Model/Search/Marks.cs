using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model.Search;

public enum MarkKind
{
    /// <summary>The character has it and can use it.</summary>
    None,

    /// <summary>An item the bank holds none of.</summary>
    NotInBank,

    /// <summary>Something the character can't use yet; the note says what it needs.</summary>
    Locked,
}

/// <summary>
/// What an answer marks on a slot or choice the character doesn't have or can't use yet. <see cref="Needs"/> is the
/// first requirement it doesn't meet, in the game's words; <see cref="Source"/> is where an item it doesn't own comes
/// from, null when the data doesn't say, and <see cref="From"/> how it is got from there in full. An item neither
/// owned nor wearable yet is not in the bank, and needs both.
/// </summary>
public sealed record Mark(
    MarkKind Kind,
    Note? Needs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Note? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Note? From = null
)
{
    public static readonly Mark None = new(MarkKind.None);
    public static readonly Mark NotInBank = new(MarkKind.NotInBank);

    public static Mark Locked(Note needs) => new(MarkKind.Locked, needs);

    /// <summary>Whether fighting it needs requirements ignored.</summary>
    [JsonIgnore]
    public bool HasNeeds => Needs != null;

    /// <summary>The mark of an option: locked before not in the bank, as a requirement is the harder to meet.</summary>
    public static Mark Of(ChoiceOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.Unmet.Count > 0 ? Locked(option.Unmet[0])
            : option.Owned ? None
            : NotInBank;
    }

    /// <summary>The mark of an item: what it needs when owned, and also where it comes from when not.</summary>
    public static Mark OfItem(GearItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var needs = item.Unmet.Count > 0 ? item.Unmet[0] : null;
        return item.Owned == 0 ? new Mark(MarkKind.NotInBank, needs, item.Source, item.SourceDetail)
            : needs != null ? Locked(needs)
            : None;
    }
}
