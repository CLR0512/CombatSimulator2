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

/// <summary>What an answer marks on a changed slot or choice the character doesn't have or can't use yet.</summary>
public sealed record Mark(MarkKind Kind, Note? Needs = null)
{
    public static readonly Mark None = new(MarkKind.None);
    public static readonly Mark NotInBank = new(MarkKind.NotInBank);

    public static Mark Locked(Note needs) => new(MarkKind.Locked, needs);

    /// <summary>The mark of an option: locked before not in the bank, as a requirement is the harder to meet.</summary>
    public static Mark Of(ChoiceOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.Unmet.Count > 0 ? Locked(option.Unmet[0])
            : option.Owned ? None
            : NotInBank;
    }
}
