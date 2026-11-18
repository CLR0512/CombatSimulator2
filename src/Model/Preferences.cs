using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What the panel remembers between opens and restarts, saved in the mod's settings file. Call
/// <see cref="Upgrade"/> after loading: files from before the current format get the new fields' defaults.
/// </summary>
public sealed class Preferences
{
    public const int CurrentFormat = 3;
    public const int MaxPins = 3;
    private const int MinKeepRuns = RunStore.MinLimit;
    private const int MaxKeepRuns = RunStore.MaxLimit;

    private List<string> _pins = [];
    private int _keepRuns = RunStore.DefaultMax;

    /// <summary>The file's format; a file without one is format 1.</summary>
    public int Format { get; set; } = 1;

    public string? SelectedTarget { get; set; }

    /// <summary>
    /// From before state was kept per character: the targets switched off in the list. Read so the first character
    /// seen can start from it, then dropped; null once that is done.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public List<string>? DisabledTargets { get; set; }

    /// <summary>The metric ranked by now (<c>xp:main</c> allowed).</summary>
    public string Metric { get; set; } = Metrics.MainXp;

    /// <summary>The metric "Rank by" starts on.</summary>
    public string MetricDefault { get; set; } = Metrics.MainXp;
    public string TimeUnit { get; set; } = "hour";

    /// <summary>
    /// From before state was kept per character: the setup simulated (<c>current</c>, or a loadout slot id) and
    /// whether its requirements were ignored. Read and dropped as <see cref="DisabledTargets"/> is.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string? Setup { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public bool? IgnoreRequirements { get; set; }

    /// <summary>
    /// Forgets what a character's own state has now taken over, so it is no longer written. True if anything was
    /// still held.
    /// </summary>
    public bool DropCharacterSeeds()
    {
        if (DisabledTargets == null && Setup == null && IgnoreRequirements == null)
            return false;
        DisabledTargets = null;
        Setup = null;
        IgnoreRequirements = null;
        return true;
    }

    /// <summary>How many copies fight at once; null for the default.</summary>
    public int? Copies { get; set; }
    public int Trials { get; set; } = 1000;

    /// <summary>A fixed seed (1 by default); null for a new one per run.</summary>
    public ulong? Seed { get; set; } = 1;

    /// <summary>Where the item picker was dragged to on the Setup tab; null where it opens by default.</summary>
    public double? PickerX { get; set; }

    public double? PickerY { get; set; }

    private double _scale = ScaleSteps.Default;

    /// <summary>
    /// How large CS2's window draws, one of <see cref="ScaleSteps.Steps"/> (1 is what 100% means). Kept as chosen
    /// when the screen has less room; only the drawn scale is capped.
    /// </summary>
    public double Scale
    {
        get => _scale;
        set => _scale = ScaleSteps.Snap(value);
    }

    /// <summary>
    /// The skin with the game's look; <see cref="MElvUISkin"/> is the other one the window can be saved with.
    /// </summary>
    public const string BaseSkin = "base";

    public const string MElvUISkin = "melvui";

    /// <summary>A saved or typed skin name, with anything unknown as <see cref="BaseSkin"/>.</summary>
    public static string NormaliseSkin(string? name) =>
        string.Equals(name?.Trim(), MElvUISkin, StringComparison.OrdinalIgnoreCase) ? MElvUISkin : BaseSkin;

    private string _skin = BaseSkin;

    /// <summary>
    /// CS2's window skin, <c>base</c> (the game's look) or <c>melvui</c>; anything else reads as <c>base</c>. Kept
    /// even while it can't be shown, so reinstalling mElvUI brings it back.
    /// </summary>
    public string Skin
    {
        get => _skin;
        set => _skin = NormaliseSkin(value);
    }

    public const string InHost = "host";
    public const string InGame = "game";

    private string _simulateIn = InHost;

    /// <summary>
    /// Where runs simulate: <c>host</c> (the simulator process, the default) or <c>game</c> (the game's process, which
    /// may stutter); anything else reads as <c>host</c>.
    /// </summary>
    public string SimulateIn
    {
        get => _simulateIn;
        set => _simulateIn = value == InGame ? InGame : InHost;
    }

    /// <summary>
    /// Whether runs simulate every target instead of reusing results of earlier runs with the same setup.
    /// </summary>
    public bool SimulateFresh { get; set; }

    /// <summary>
    /// Whether targets stop before their full trials once their kills rate is within <see cref="StopPrecision"/>.
    /// </summary>
    public bool StopEarly { get; set; }

    /// <summary>The relative precision early stopping aims for (0.05 = ±5%).</summary>
    public double StopPrecision { get; set; } = 0.05;

    /// <summary>How much the panel animates; null until the player chooses, and then the game's own setting leads.</summary>
    public MotionLevel? Motion { get; set; }

    private TargetFilter _filter = new();

    /// <summary>Which targets are shown and run, for every character; its search text is never saved.</summary>
    public TargetFilter Filter
    {
        get => _filter;
        set => _filter = value ?? new();
    }

    /// <summary>Whether the header shows the Sim button.</summary>
    public bool ShowHeaderButton { get; set; } = true;

    /// <summary>Whether the Gear tab shows its (i) button that opens the gear search guide.</summary>
    public bool ShowGearGuide { get; set; } = true;

    /// <summary>Whether the About button shows a dot after an update, until About is opened.</summary>
    public bool UpdateNotifications { get; set; } = true;

    /// <summary>Whether notices (a refused change) show at the foot of the window; at its top by default.</summary>
    public bool NoticesAtBottom { get; set; }

    /// <summary>Whether the game's notifications are hidden while the window is open; they show by default.</summary>
    public bool QuietNotifications { get; set; }

    /// <summary>
    /// Whether monsters and places the game hasn't shown the character yet are named <see cref="Spoilers.Mask"/>; on
    /// unless switched off.
    /// </summary>
    public bool SpoilerGuard { get; set; } = true;

    /// <summary>
    /// The language CS2's own text is in: null is "Same as the game"; otherwise a locale code <c>mm locale</c> accepts
    /// (<c>fr</c>, <c>pt_BR</c>). A code with no shipped translation shows the game's language and is kept, so a later
    /// version that ships it picks it up again.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string? Language { get; set; }

    /// <summary>A bug report carries the character's save, so the run can be repeated; on unless switched off.</summary>
    public bool ReportIncludesSave { get; set; } = true;

    private int _crashFilesMegabytes = DefaultCrashFilesMegabytes;
    public const int DefaultCrashFilesMegabytes = 15;

    /// <summary>
    /// The most space crash dumps may take, and again bug reports, in MB, 5…1000: the oldest go first. A machine
    /// where CS2 keeps crashing keeps at most twice this.
    /// </summary>
    public int CrashFilesMegabytes
    {
        get => _crashFilesMegabytes;
        set => _crashFilesMegabytes = Math.Clamp(value, 5, 1000);
    }

    /// <summary>The view CS2 opens on: <c>simulate</c>, <c>setup</c> or <c>settings</c>.</summary>
    public string View { get; set; } = "simulate";

    /// <summary>The details' tab last chosen ("rates", "fights" or "gear"; an unknown one opens Rates), kept from one target to the next.</summary>
    public string DetailTab { get; set; } = "rates";

    /// <summary>Whether a dungeon's "Fight by fight" section is open in the details.</summary>
    public bool FightsOpen { get; set; }

    /// <summary>Whether the "Rare drops" section is open in the details.</summary>
    public bool RareDropsOpen { get; set; }

    /// <summary>Modifier groups the player opened on the Setup tab, by group name.</summary>
    public List<string> OpenModifierGroups { get; set; } = [];

    /// <summary>Whether the Setup tab shows only the stats that differ between A and B.</summary>
    public bool StatsOnlyDifferences { get; set; }

    /// <summary>How results are shown: <c>list</c> or <c>chart</c>.</summary>
    public string ResultsMode { get; set; } = "chart";

    /// <summary>Areas and dungeons folded into one bar on the chart.</summary>
    public List<string> FoldedAreas { get; set; } = [];

    /// <summary>Areas without any result that the player opened; the chart collapses the others to a stub.</summary>
    public List<string> OpenedAreas { get; set; } = [];

    /// <summary>Metric keys whose rows the pinned targets' comparison puts at its top, in the order pinned.</summary>
    public List<string> PinnedRows { get; set; } = [];

    /// <summary>Pinned target keys, in pin order; at most <see cref="MaxPins"/>.</summary>
    public List<string> Pins
    {
        get => _pins;
        set => _pins = [.. (value ?? []).Distinct(StringComparer.Ordinal).Take(MaxPins)];
    }

    /// <summary>How many runs are kept on disk, <see cref="MinKeepRuns"/>…<see cref="MaxKeepRuns"/>.</summary>
    public int KeepRuns
    {
        get => _keepRuns;
        set => _keepRuns = Math.Clamp(value, MinKeepRuns, MaxKeepRuns);
    }

    public const double DefaultTieShare = 0.01;
    public const double MaxTieShare = 0.05;

    private double _tieShare = DefaultTieShare;

    /// <summary>
    /// How close to the best a set must be to count as good as it, as a share of the best's value: 0 to
    /// <see cref="MaxTieShare"/>. At 0 only the 95% range decides.
    /// </summary>
    public double TieShare
    {
        get => _tieShare;
        set => _tieShare = double.IsNaN(value) ? DefaultTieShare : Math.Clamp(value, 0, MaxTieShare);
    }

    private List<string> _searchKinds = [.. ChoiceKinds.Defaults.Select(ChoiceKinds.Name)];

    /// <summary>
    /// The kinds of choice "Find best setup" tries besides gear, by name; names this build doesn't know are dropped.
    /// </summary>
    public List<string> SearchKinds
    {
        get => _searchKinds;
        set =>
            _searchKinds = [
                .. ChoiceKinds
                    .Normal(
                        (value ?? [])
                            .Select(n => ChoiceKinds.TryParse(n, out var k) ? k : (ChoiceKind?)null)
                            .OfType<ChoiceKind>()
                    )
                    .Select(ChoiceKinds.Name),
            ];
    }

    /// <summary>The switched-on kinds, in the search's order.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ChoiceKind> SearchKindList =>
        [.. _searchKinds.Select(n => ChoiceKinds.TryParse(n, out var k) ? k : default)];

    /// <summary>"Include what I don't have": choices the character doesn't own or can't use yet.</summary>
    public bool SearchIncludeUnowned { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeUnit Unit => TimeUnits.TryParse(TimeUnit, out var unit) ? unit : Model.TimeUnit.Hour;

    /// <summary>
    /// Brings a loaded file to the current format. From format 1, the metric starts on the default: format 1 only
    /// ever held its own default there, not a choice. Returns true if anything changed.
    /// </summary>
    public bool Upgrade()
    {
        if (Format >= CurrentFormat)
            return false;
        if (Format < 2)
            Metric = MetricDefault;
        Format = CurrentFormat;
        return true;
    }

    /// <summary>
    /// Drops target keys the game's data no longer has, and folded areas it doesn't know when
    /// <paramref name="areas"/> is given. True if anything was dropped.
    /// </summary>
    public bool DropUnknown(IReadOnlySet<string> known, IReadOnlySet<string>? areas = null)
    {
        ArgumentNullException.ThrowIfNull(known);
        var changed = false;
        if (SelectedTarget != null && !known.Contains(SelectedTarget))
        {
            SelectedTarget = null;
            changed = true;
        }
        changed |= _pins.RemoveAll(k => !known.Contains(k)) > 0;
        if (areas != null)
        {
            changed |= FoldedAreas.RemoveAll(a => !areas.Contains(a)) > 0;
            changed |= OpenedAreas.RemoveAll(a => !areas.Contains(a)) > 0;
        }
        return changed;
    }
}
