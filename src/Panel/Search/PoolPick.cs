using System.Text.RegularExpressions;
using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What "Find best setup" chooses from, at the end of the Gear tab's sentence: "from [owned items]" or "from [every
/// item]", remembered with the preferences. Every item is for one target at a time: for an area, a master or the pins
/// the word says owned items and greys every item, saying why.
/// </summary>
internal sealed class PoolPick
{
    private readonly Dropdown _pick;

    /// <summary>The words; <paramref name="changed"/> hears of a new pool, to look up and count again.</summary>
    public PoolPick(Action changed)
    {
        Root = Ui.Row(4, "GearSearchPoolWords");
        _pick = new Dropdown("GearSearchPool", Dropdown.Link) { RightAligned = true };
        _pick.ItemSelected += i =>
        {
            var pool = i == 1 ? ItemPool.Every : ItemPool.Owned;
            if (pool == PanelState.Prefs.SearchPool)
                return;
            PanelState.Prefs.SearchPool = pool;
            PanelState.Save();
            changed();
        };
        // Translated whole, so a language may put the choice before its word.
        // Translators: End of the Gear tab sentence "Find [best setup] for [this target] from [every item]"; {0} is "owned items" or "every item", a gold choice.
        foreach (var part in Regex.Split(Tr.Get("from {0}"), @"(\{0\})"))
        {
            if (part == "{0}")
                Root.AddChild(_pick.Root);
            else if (part.Trim().Length > 0)
            {
                var words = Ui.Add(Root, Ui.Label(part.Trim(), TextStyle.Body));
                words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            }
        }
    }

    public HBoxContainer Root { get; }

    /// <summary>The words for a search of <paramref name="kind"/> on <paramref name="target"/>; only "Find best setup" has them.</summary>
    public void Show(SearchKind kind, string target)
    {
        Root.Visible = kind == SearchKind.BestGear;
        _pick.Clear();
        // Translators: Gear tab sentence "… from [owned items]": the items the character owns.
        _pick.AddItem(Tr.Get("owned items"));
        _pick.AddItem(
            // Translators: Gear tab sentence "… from [every item]": every item in the game, owned or not.
            Tr.Get("every item"),
            // Translators: Hover of the greyed "every item" choice for an area, a master or the pins.
            off: OneTarget(target) ? null : Tr.Get("Every item: one target at a time")
        );
        _pick.Select(For(kind, target) == ItemPool.Every ? 1 : 0);
    }

    /// <summary>The pool a search of <paramref name="kind"/> on <paramref name="target"/> chooses from.</summary>
    public static ItemPool For(SearchKind kind, string target) =>
        kind == SearchKind.BestGear && OneTarget(target) ? PanelState.Prefs.SearchPool : ItemPool.Owned;

    /// <summary><paramref name="request"/> with the pool and, over every item, the narrowing as saved.</summary>
    public static SearchRequest Apply(SearchRequest request)
    {
        var prefs = PanelState.Prefs;
        var every = For(request.Kind, request.Target) == ItemPool.Every;
        return request with
        {
            Pool = every ? ItemPool.Every : ItemPool.Owned,
            WearableOnly = every && prefs.SearchPoolWearable,
            ReachedOnly = every && prefs.SearchPoolReached,
            LeftOutSources = every
                ? prefs.SearchLeftOut.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(),
        };
    }

    /// <summary>The pool as a kept answer's key holds it, to find the answer of the pool chosen.</summary>
    public static AnswerKey Key(SearchKind kind, string target) =>
        new AnswerKey().WithPool(Apply(new SearchRequest(kind, new Setup(), target, new Goal(""), 1, 1)));

    private static bool OneTarget(string target) => !Ids.IsMaster(target) && !Ids.IsSet(target);
}
