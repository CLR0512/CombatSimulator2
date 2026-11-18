using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// The panel's state that outlives the modal: remembered choices, the latest target catalog, the selected target,
/// the comparison view and the banner (the pins are <see cref="PanelPins"/>'s). Views redraw on
/// <see cref="Changed"/>. Main thread only.
/// </summary>
internal static class PanelState
{
    private static ModContext? _context;
    private static readonly Dictionary<string, PriceTable> _prices = [];
    private static CharacterPrefs? _characters;
    private static string? _characterKey;
    private static HashSet<string> _ignored = new(StringComparer.Ordinal);

    public static Preferences Prefs { get; private set; } = new();

    /// <summary>The latest catalog, for the setup it was checked for.</summary>
    public static TargetCatalog? Catalog { get; set; }

    public static BannerState? Banner { get; private set; }

    // What side B is compared as, in the terms of the comparison commands and the follower.

    /// <summary>
    /// What side B is to the setup bar: null for none, a setup (its slot id, or <c>current</c>),
    /// <see cref="WhatIf"/> when it starts from what side A starts from, or <see cref="SavedRun"/>.
    /// </summary>
    public static string? CompareWith =>
        B is not { } b ? (_pickingSavedRun ? SavedRun : null)
        : b.Base.Kind == SideBaseKind.SavedRun ? SavedRun
        : b.Base.Equals(A.Base) ? WhatIf
        : b.Base.Text;

    /// <summary>The value of <see cref="CompareWith"/> for a saved run as side B.</summary>
    public const string SavedRun = "saved";

    /// <summary>The value of <see cref="CompareWith"/> for side B starting from side A's base.</summary>
    public const string WhatIf = "whatif";

    // "Saved run…" was chosen and no run is picked yet.
    private static bool _pickingSavedRun;

    /// <summary>The saved run side B is, when it is one.</summary>
    public static string? Baseline => B is { Base.Kind: SideBaseKind.SavedRun } b ? b.Base.Id : null;

    /// <summary>Whether the comparison replaces the single run's results.</summary>
    public static bool ComparisonShown { get; private set; }

    /// <summary>Whether the saved-run picker replaces the results.</summary>
    public static bool PickingBaseline { get; private set; }

    public static CompareFilter CompareFilter { get; private set; }

    public static CompareOrder CompareOrder { get; private set; }

    /// <summary>Makes side B a setup, side A's base with changes, or a saved run; null ends side B.</summary>
    public static void SetCompareWith(string? with, string? baseline = null)
    {
        _pickingSavedRun = with == SavedRun && baseline == null;
        PickingBaseline = false;
        if (with == null)
            ComparisonShown = false;
        var b = with switch
        {
            null => null,
            WhatIf => CompareWith == WhatIf ? B : new SideState(A.Base, null, A.IgnoreRequirements),
            SavedRun => baseline == null ? null : new SideState(SideBase.SavedRun(baseline)),
            _ => new SideState(SideBase.Parse(with), null, B?.IgnoreRequirements ?? false),
        };
        SetSide(SideLetter.B, b);
        Notify();
    }

    public static void ShowComparison(bool shown)
    {
        if (ComparisonShown == shown)
            return;
        ComparisonShown = shown;
        Notify();
    }

    public static void PickBaseline(bool open)
    {
        if (PickingBaseline == open)
            return;
        PickingBaseline = open;
        Notify();
    }

    public static void SetCompareFilter(CompareFilter filter)
    {
        CompareFilter = filter;
        Notify();
    }

    public static void SetCompareOrder(CompareOrder order)
    {
        CompareOrder = order;
        Notify();
    }

    /// <summary>What the help layer shows; see <see cref="HelpState"/>.</summary>
    public static HelpState Help { get; } = new();

    /// <summary>Raised after any state change a view shows.</summary>
    public static event Action? Changed;

    public static string? Selected => Prefs.SelectedTarget;

    public static LookKind Look => Widgets.Look.Kind;

    internal static void Load(ModContext context)
    {
        _context = context;
        Prefs = context.LoadConfig<Preferences>();
        if (Prefs.Upgrade())
            Save();
        var (characters, problem) = CharacterPrefs.Read(context.DataRoot);
        if (problem != null)
            Log.Warn($"per-character state not read, starting empty: {problem}");
        _characters = characters;
        PanelPins.ForgetShownRun();
        PanelPins.Watch();
    }

    /// <summary>
    /// The loaded character's own state (its ignored targets and its sides); null without a character. The first
    /// time a character is seen it starts from what the settings held for every character.
    /// </summary>
    public static CharacterState? Character()
    {
        if (_characters == null || LiveGame.Find() is not { } game)
            return null;
        var key = LiveGame.CharacterKey(game);
        var state = _characters.For(key, LiveGame.CharacterName(game), Prefs, out var seeded);
        if (seeded)
        {
            SaveCharacters();
            if (Prefs.DropCharacterSeeds())
                Save();
        }
        if (key != _characterKey)
        {
            _characterKey = key;
            var known = Targets.All(game).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            if (_characters.DropUnknown(key, known))
                SaveCharacters();
            _ignored = new(state.Ignored, StringComparer.Ordinal);
        }
        return state;
    }

    private static void SaveCharacters()
    {
        if (_context != null)
            _characters?.Write(_context.DataRoot);
    }

    /// <summary>Side A: what the character fights with. Always a setup that can be simulated.</summary>
    public static SideState A => Character()?.A ?? SideState.AsNow;

    /// <summary>Side B, when the character is comparing.</summary>
    public static SideState? B => Character()?.B;

    /// <summary>
    /// Sets a side (null ends side B). Side A is never a saved run: a saved run only has results to show beside it.
    /// </summary>
    public static void SetSide(SideLetter letter, SideState? side)
    {
        if (Character() is not { } character)
            return;
        if (letter == SideLetter.A)
        {
            if (side is not { CanChange: true })
                throw new CommandException("side A is the character or one of its loadouts");
            if (side.Equals(character.A))
                return;
            character.A = side;
            CheckA = null;
            ClearRefused();
        }
        else
        {
            if (Equals(side, character.B))
                return;
            character.B = side;
            CheckB = null;
            ClearRefused();
        }
        SaveCharacters();
        Notify();
    }

    private static readonly List<(SideLetter Letter, ChangeOutcome Outcome)> _refused = [];

    /// <summary>The changes the game last refused, each with its side; shown as a notice, since they aren't kept.</summary>
    public static IReadOnlyList<(SideLetter Letter, ChangeOutcome Outcome)> Refused => _refused;

    /// <summary>When the refusals were noted, in ticks; −1 when there is none to show.</summary>
    public static long RefusedAt { get; private set; } = -1;

    /// <summary>
    /// Takes out of <paramref name="letter"/>'s side the changes <paramref name="check"/> found to do nothing: the
    /// refused ones, which are noted (the side's next change clears the note), and the ones that set what the side
    /// already has (a level at the level it is), which were never changes.
    /// </summary>
    public static void DropRefused(SideLetter letter, SideState side, SideCheck check)
    {
        var idle = check
            .Changes.Outcomes.Where(o =>
                (o.LeftOut || o.State == ChangeState.NoChange) && side.Changes.Find(o.Change.Key) == o.Change
            )
            .ToList();
        if (idle.Count == 0)
            return;
        var kept = idle.Aggregate(side.Changes, (list, outcome) => list.Without(outcome.Change.Key));
        SetSide(letter, side.WithChanges(kept));
        var refused = idle.Where(o => o.LeftOut).ToList();
        if (refused.Count == 0)
            return;
        _refused.AddRange(refused.Select(o => (letter, o)));
        RefusedAt = System.Environment.TickCount64;
        Notify();
    }

    public static void ClearRefused()
    {
        if (RefusedAt < 0)
            return;
        _refused.Clear();
        RefusedAt = -1;
    }

    /// <summary>
    /// The sides the panel last started a run with, and that run's id (side A's for a comparison): edited sides
    /// read "Not run yet" until they are run.
    /// </summary>
    public static (string Run, SideState A, SideState? B)? Ran { get; set; }

    /// <summary>The latest check of side A, for the side as it is now; null before it comes back.</summary>
    public static SideCheck? CheckA { get; private set; }

    /// <summary>The latest check of side B; null before it comes back and without a side B.</summary>
    public static SideCheck? CheckB { get; private set; }

    /// <summary>Keeps a check that came back for the side as it is now.</summary>
    public static void SetCheck(SideLetter letter, SideCheck? check)
    {
        if (letter == SideLetter.A)
            CheckA = check;
        else
            CheckB = check;
        Notify();
    }

    /// <summary>The side the Setup tab is on; side A when there is no side B.</summary>
    public static SideLetter Editing => B != null && Character()?.Editing == SideLetter.B ? SideLetter.B : SideLetter.A;

    public static void SetEditing(SideLetter letter)
    {
        if (Character() is not { } character || character.Editing == letter)
            return;
        character.Editing = letter;
        SaveCharacters();
        Notify();
    }

    /// <summary>Side A as the setup a run uses, without the character's name.</summary>
    public static Setup SetupA() => A.ToSetup("");

    private static Presets? _presets;

    /// <summary>The presets every character shares, read from their file the first time they are asked for.</summary>
    public static Presets Presets()
    {
        if (_presets != null)
            return _presets;
        if (_context == null)
            return Model.Presets.Read("").Presets;
        var (presets, problem) = Model.Presets.Read(_context.DataRoot);
        if (problem != null)
            Log.Warn($"presets not read, starting empty: {problem}");
        return _presets = presets;
    }

    /// <summary>Writes the presets after one was saved, renamed or deleted, and tells the views.</summary>
    public static void SavePresets()
    {
        if (_context != null)
            _presets?.Write(_context.DataRoot);
        Notify();
    }

    /// <summary>The loaded character's ignored targets: left out of runs until brought back.</summary>
    public static IReadOnlySet<string> Ignored => Character() == null ? new HashSet<string>() : _ignored;

    /// <summary>
    /// Ignores <paramref name="keys"/> or brings them back; the list, the chart and the strip all change this set.
    /// </summary>
    public static void SetIgnored(IEnumerable<string> keys, bool ignored)
    {
        if (Character() is not { } character)
            return;
        var changed = false;
        foreach (var key in keys)
            changed |= ignored ? _ignored.Add(key) : _ignored.Remove(key);
        if (!changed)
            return;
        character.Ignored = [.. _ignored];
        SaveCharacters();
        Notify();
    }

    /// <summary>
    /// Runs <paramref name="keys"/> alone: every other target the filter shows is ignored. When any of them is ignored
    /// it is brought back instead, beside those kept, so a few can be picked one by one.
    /// </summary>
    public static void RunOnly(IReadOnlyCollection<string> keys)
    {
        if (keys.Any(Ignored.Contains))
        {
            SetIgnored(keys, false);
            return;
        }
        SetIgnored([.. Selection()?.Shown.Select(t => t.Key).Where(k => !keys.Contains(k)) ?? []], true);
    }

    /// <summary>Brings every ignored target back.</summary>
    public static void BringAllBack() => SetIgnored([.. _ignored], false);

    /// <summary>Changes which targets are shown and run; the search text is kept for the session only.</summary>
    public static void SetFilter(TargetFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Equals(Prefs.Filter))
            return;
        var saved = (filter with { Search = "" }).Equals(Prefs.Filter with { Search = "" });
        Prefs.Filter = filter;
        if (saved)
            Notify();
        else
            Update();
    }

    /// <summary>
    /// What the filter shows of the latest catalog, what of that is ignored and what a run covers; null until
    /// targets have been checked for the setup.
    /// </summary>
    public static TargetSelection? Selection()
    {
        if (Catalog is not { } catalog)
            return null;
        var ignore = A.IgnoreRequirements;
        return TargetSelection.Of([.. catalog.List.Select(t => t.Fact(ignore))], Prefs.Filter, Ignored);
    }

    /// <summary>Saves on discrete actions only (a choice made), never per frame.</summary>
    public static void Save() => _context?.SaveConfig(Prefs);

    /// <summary>Shows CS2's own text in <paramref name="locale"/> at once (null: the game's language), and saves it.</summary>
    public static void ChooseLanguage(string? locale)
    {
        Prefs.Language = locale;
        Save();
        if (_context != null)
            _context.Text.Language = LanguageList.Applied(locale, Shipped(_context));
    }

    /// <summary>The saved language, before the window is built; one CS2 doesn't ship shows the game's.</summary>
    internal static void ApplyLanguage(ModContext context) =>
        context.Text.Language = LanguageList.Applied(Prefs.Language, Shipped(context));

    // The languages CS2 ships, by their files: readable at load, before Godot has the catalogues.
    private static IReadOnlyList<string> Shipped(ModContext context)
    {
        var folder = Path.Combine(context.Directory, "data", "locale");
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, "*.po").Select(f => Path.GetFileNameWithoutExtension(f)!)]
            : [];
    }

    /// <summary>Saves and tells the views.</summary>
    public static void Update()
    {
        Save();
        Notify();
    }

    /// <summary>
    /// Steps on every change the views are told of: a frame callback compares it to do nothing while nothing changed.
    /// </summary>
    public static int Version { get; private set; }

    public static void Notify()
    {
        Version++;
        Changed?.Invoke();
    }

    public static void Select(string? key)
    {
        if (Prefs.SelectedTarget == key)
            return;
        Prefs.SelectedTarget = key;
        Update();
    }

    public static void SetBanner(BannerState? banner)
    {
        Banner = banner;
        Notify();
    }

    /// <summary>Puts a metric's row at the top of the pinned targets' comparison, or back in its place.</summary>
    public static void TogglePinnedRow(string metric)
    {
        if (!Prefs.PinnedRows.Remove(metric))
            Prefs.PinnedRows.Add(metric);
        Update();
    }

    public static void ToggleFold(string area)
    {
        if (!Prefs.FoldedAreas.Remove(area))
            Prefs.FoldedAreas.Add(area);
        Update();
    }

    /// <summary>Opens areas without results on the chart, or folds them back into a stub when any is open.</summary>
    public static void ToggleOpen(IReadOnlyList<string> areas)
    {
        if (areas.Any(Prefs.OpenedAreas.Contains))
            Prefs.OpenedAreas.RemoveAll(areas.Contains);
        else
            Prefs.OpenedAreas.AddRange(areas.Where(a => !Prefs.OpenedAreas.Contains(a)));
        Update();
    }

    /// <summary>The targets a run covers: what the filter shows, less the ignored ones.</summary>
    public static IReadOnlyList<TargetSpec> ToRun() =>
        Selection() is { } selection
            ? [.. selection.ToRun.Select(t => TargetSpec.FromKey(t.Key))]
            : throw new CommandException("targets aren't checked yet: open the panel, or cs2 ignored");

    /// <summary>Sell prices, read once per character's data.</summary>
    public static PriceTable Prices()
    {
        if (LiveGame.Find() is not { } game)
            return PriceTable.Empty;
        var key = LiveGame.CharacterName(game);
        if (!_prices.TryGetValue(key, out var table))
            _prices[key] = table = LiveGame.Prices(game);
        return table;
    }

    /// <summary>The game's names for ids, or the short id without a character.</summary>
    public static Func<string, string> Names()
    {
        var game = LiveGame.Find();
        return id => game == null ? Ids.Brief(id) : LiveGame.NameOf(game, id);
    }

    public static void Reset()
    {
        _context = null;
        Catalog = null;
        PanelPins.ForgetShownRun();
        Banner = null;
        _pickingSavedRun = false;
        Ran = null;
        CheckA = null;
        CheckB = null;
        ComparisonShown = false;
        PickingBaseline = false;
        CompareFilter = CompareFilter.All;
        CompareOrder = CompareOrder.Difference;
        Changed = null;
        _prices.Clear();
        _characters = null;
        _presets = null;
        _characterKey = null;
        _ignored = new(StringComparer.Ordinal);
    }
}
