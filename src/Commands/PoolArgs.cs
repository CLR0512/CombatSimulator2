using System.Globalization;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// A best-gear search's pool from its arguments: <c>pool=own|every</c>, and with every item the narrowing
/// <c>wearable</c>, <c>reached</c> and <c>leave=&lt;source&gt;,…</c>; and its longest, <c>limit=&lt;minutes&gt;|none</c>,
/// as the player's setting has it. Whether they go together is the request's to say.
/// </summary>
internal static class PoolArgs
{
    public static readonly string[] Options = ["pool", "leave", "limit"];
    public static readonly string[] Flags = ["wearable", "reached"];

    public const string Usage = "[pool=own|every] [wearable] [reached] [leave=<source>,…] [limit=<minutes>|none]";

    public static SearchRequest Apply(SearchRequest request, CommandArgs args)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(args);
        return request with
        {
            Pool = PoolFrom(args.String("pool")),
            WearableOnly = args.Flag("wearable"),
            ReachedOnly = args.Flag("reached"),
            LeftOutSources = (args.String("leave") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            MaxSeconds = LimitFrom(args.String("limit")),
        };
    }

    // Minutes as the setting offers them, or none; the setting's default when not given.
    private static double? LimitFrom(string? text) =>
        text?.ToLowerInvariant() switch
        {
            null => SearchRequest.DefaultMaxSeconds,
            "none" => null,
            var minutes
                when int.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0 => m
                * 60.0,
            var other => throw new CommandException($"limit={other}: use minutes, as limit=10, or limit=none"),
        };

    /// <summary>The pool the arguments name, as a kept answer's key holds it.</summary>
    public static AnswerKey Key(CommandArgs args) =>
        new AnswerKey().WithPool(
            Apply(new SearchRequest(SearchKind.BestGear, new Model.Setup(), "", new Goal(""), 1, 1), args)
        );

    private static ItemPool PoolFrom(string? text) =>
        text?.ToLowerInvariant() switch
        {
            null or "own" => ItemPool.Owned,
            "every" => ItemPool.Every,
            var other => throw new CommandException($"pool={other}: use pool=own or pool=every"),
        };
}
