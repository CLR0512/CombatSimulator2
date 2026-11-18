using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>A master's block suggestion as command text.</summary>
internal static class BlocksText
{
    /// <summary>
    /// The suggestion: what it is from, the best blocks within the open slots, then a better choice that needs a slot
    /// not yet open, or that the blocks are as good.
    /// </summary>
    public static string Of(BlockSuggestion suggestion, TargetResult master, string runId, Func<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(master);
        var text = new StringBuilder(
            $"blocks {Ids.BriefKey(master.Key)} by {suggestion.Metric}, from {runId} "
                + $"(blocks now: {Names(suggestion.Current, names)}; slots open {master.Master?.OpenSlots ?? 0}; "
                + $"{suggestion.Choices} choices)"
        );
        if (suggestion.Best is { } best)
            text.Append("\n  open slots: ").Append(Choice(best, names));
        if (suggestion.NeedsSlot is { } needs)
            text.Append("\n  ").Append(BlocksNote(needs)).Append(": ").Append(Choice(needs, names));
        if (suggestion.AsGood)
            text.Append("\n  blocks: as good");
        if (suggestion is { Best: null, NeedsSlot: null })
            text.Append("\n  blocks: every choice still gets stuck");
        return text.ToString();
    }

    /// <summary>A master row's short line: "best blocks: +38% [35, 41]", or "blocks: as good"; null without one.</summary>
    public static string? Short(BlockSuggestion? suggestion) =>
        suggestion switch
        {
            null => null,
            { AsGood: true } => "blocks: as good",
            { Best.Gain: { } gain } => $"best blocks: {Percent(gain)}",
            { Best: { } best } => $"best blocks: {string.Join(',', best.Blocks.Select(Ids.BriefKey))}",
            _ => null,
        };

    private static string BlocksNote(BlockChoice choice) =>
        $"needs slot {choice.Blocks.Count} · {choice.TasksNeeded} more task{(choice.TasksNeeded == 1 ? "" : "s")}";

    private static string Choice(BlockChoice choice, Func<string, string> names)
    {
        var text = new StringBuilder(Names(choice.Blocks, names));
        text.Append("   ").Append(choice.Gain is { } gain ? Percent(gain) : "gets past a monster it is stuck on");
        text.Append("   ").Append(Fmt.N(choice.Figure.Point));
        text.Append("   ").Append(Fmt.I(choice.Cost)).Append(" coins");
        if (choice.Hours is { } hours && choice.Cost > 0)
            text.Append(" · ").Append(Fmt.N(hours)).Append(" h");
        if (choice.SafeToLeave)
            text.Append("   safe to leave running");
        return text.ToString();
    }

    private static string Percent(Estimate gain) =>
        $"{(gain.Point >= 0 ? "+" : "")}{Fmt.N(gain.Point * 100)}% [{Fmt.N(gain.Low * 100)}, {Fmt.N(gain.High * 100)}]";

    private static string Names(IReadOnlyList<string> keys, Func<string, string> names) =>
        keys.Count == 0 ? "none" : string.Join(", ", keys.Select(k => names(Enemy(k))));

    // The enemy of an area/enemy key.
    private static string Enemy(string key) => key[(key.IndexOf('/', StringComparison.Ordinal) + 1)..];
}
