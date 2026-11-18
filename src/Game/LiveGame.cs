using System.Diagnostics;
using System.Security.Cryptography;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.RareEvents;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.GameMenus;
using GamesByMalcs.Melvor2.Saves;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>The live game's full state at one moment, as the bytes of a save.</summary>
internal sealed record Snapshot(
    string Character,
    byte[] Bytes,
    string Sha256,
    DateTime TakenAt,
    string GameBuild,
    double TakeMs,
    bool Checked = true
);

/// <summary>A saved loadout of the live character.</summary>
internal sealed record LoadoutInfo(string Slot, string Name, bool Unlocked);

/// <summary>The live character's current target.</summary>
internal sealed record CurrentTarget(string Key, string Name, TargetKind Kind);

/// <summary>Read-only access to the live game, on the main thread only.</summary>
internal static partial class LiveGame
{
    public const string CombatId = "MelvorBase:Combat";
    public static readonly Note NoGame = Note.Of("no game is loaded: open a character first");

    /// <summary>
    /// The save version the running game writes. The game's writer always writes its newest layout and its reader skips
    /// fields by the version in the header, so a copy must name this one; it is read at run time because a constant
    /// would keep the version of the build the mod was compiled against.
    /// </summary>
    public static readonly SaveVersion SaveVersion = (SaveVersion)
        typeof(SaveManager).GetField(nameof(SaveManager.CurrentSaveVersion))!.GetRawConstantValue()!;

    private static WeakReference<GameView>? _view;
    private static bool _wasLoaded;

    /// <summary>Raised on the main thread when the loaded game goes away (back to the title screen).</summary>
    public static event Action? Left;

    /// <summary>Raised on the main thread on the first frame a character is in game (also after a reload of CS2).</summary>
    public static event Action? Entered;

    /// <summary>The game assembly's identity: the host must run the same one.</summary>
    public static string AssemblyMvid => typeof(MelvorGame).Assembly.ManifestModule.ModuleVersionId.ToString();

    /// <summary>The Steam build id, or <c>unknown</c>; read from the game's folder, wherever the mod is installed.</summary>
    public static string SteamBuild { get; set; } = MelvorMods.Core.GameBuilds.Unknown;

    /// <summary>Whether CS2 was checked with this game; set at load and again once the version text can be read.</summary>
    public static CheckedState Checked { get; set; } = CheckedState.Of(MelvorMods.Core.GameBuilds.Unknown, null, null);

    /// <summary>
    /// The checked state from the manifest's list. <paramref name="version"/> is null at load, when the game's
    /// version text can't be read yet.
    /// </summary>
    public static CheckedState CheckedFor(MelvorMods.Core.Manifest? manifest, string? version) =>
        CheckedState.Of(
            SteamBuild,
            manifest is null ? null : MelvorMods.Core.GameBuilds.Listed(manifest, SteamBuild)?.Version,
            version
        );

    /// <summary>The game's version as its version overlay shows it.</summary>
    public static string GameVersion =>
        MelvorMods.Core.GameBuilds.VersionText(Constants.GameVersion, Constants.GameSubversion);

    public static string GameBuild => $"{SteamBuild} {Constants.GameVersion}{Constants.GameSubversion}";

    /// <summary>The loaded game: the first <see cref="GameView"/> in the scene tree with a loaded save.</summary>
    public static MelvorGame? Find()
    {
        MainThread.Assert("LiveGame.Find");
        if (_view != null && _view.TryGetTarget(out var cached) && GodotObject.IsInstanceValid(cached))
        {
            if (cached.Game is { IsSaveLoaded: true } game)
                return game;
        }
        _view = null;
        if (Godot.Engine.GetMainLoop() is not SceneTree tree)
            return null;
        var view = FindView(tree.Root);
        if (view == null)
            return null;
        _view = new WeakReference<GameView>(view);
        return view.Game;
    }

    public static MelvorGame Require() => Find() ?? throw new CommandException(NoGame.English());

    /// <summary>Raises <see cref="Left"/> once when the loaded game goes away; called every frame.</summary>
    public static void Watch()
    {
        var loaded = Find() != null;
        if (_wasLoaded && !loaded)
            Left?.Invoke();
        else if (!_wasLoaded && loaded)
            Entered?.Invoke();
        _wasLoaded = loaded;
    }

    private static GameView? FindView(Node node)
    {
        if (node is GameView { Game.IsSaveLoaded: true } view)
            return view;
        foreach (var child in node.GetChildren())
        {
            if (FindView(child) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>The loaded character's name.</summary>
    public static string CharacterName(MelvorGame game) => game.CharacterName;

    /// <summary>
    /// What state kept per character is filed under: the id of the character's save, which a rename keeps, or its
    /// name on a build that gives a loaded save no id.
    /// </summary>
    public static string CharacterKey(MelvorGame game)
    {
        if (
            !Dependencies.IsAvailable(Dependencies.CharacterKey)
            || !GamesByMalcs.Melvor2.Saves.SaveManager.HasActiveSaveId
        )
            return game.CharacterName;
        // The panel asks for this several times a frame: the id's text is made once per save, not per call.
        var id = GamesByMalcs.Melvor2.Saves.SaveManager.ActiveSaveId;
        if (id != _keyFor)
            (_keyFor, _key) = (id, id.ToString("N"));
        return _key!;
    }

    private static Guid _keyFor;
    private static string? _key;

    /// <summary>
    /// An enemy's combat level from the live game's data; null without a character or if the enemy is gone.
    /// </summary>
    public static int? EnemyLevel(string? enemyId)
    {
        if (enemyId == null || Find() is not { } game)
            return null;
        try
        {
            return game.Data.TryGetObject<Enemy>(enemyId, out var enemy) ? enemy.CombatLevel : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.WarnOnce($"no combat level for {enemyId}", e);
            return null;
        }
    }

    /// <summary>The game's simulation ticks per second.</summary>
    public static int TicksPerSecond => Constants.TicksPerSecond;

    // An area's name, or the pins named one by one.
    private static string SetName(MelvorGame game, TargetSpec set, bool masked) =>
        set.Area.StartsWith(Ids.AreaPrefix, StringComparison.Ordinal)
            ? NameOf(game, set.Area[Ids.AreaPrefix.Length..], masked)
            : string.Join(", ", Targets.Members(game, set).Select(m => TargetName(game, m, masked)));

    /// <summary>
    /// A target's name: the area, or the enemy with its area; a master's is the master's own, which views show as its
    /// tasks. Names kept in a run are read with <paramref name="masked"/> off, so they show once the guard allows.
    /// </summary>
    public static string TargetName(MelvorGame game, TargetSpec target, bool masked = true) =>
        target.Master is { } master
            ? Slayer.SlayerTasks.Master(game, master)?.Data.DisplayName.Text ?? Ids.Brief(master)
        : target.Kind == TargetKind.Set ? SetName(game, target, masked)
        : target.Enemy == null ? NameOf(game, target.Area, masked)
        : $"{NameOf(game, target.Enemy, masked)} ({NameOf(game, target.Area, masked)})";

    public static Snapshot TakeSnapshot(MelvorGame game)
    {
        MainThread.Assert("LiveGame.TakeSnapshot");
        var watch = Stopwatch.StartNew();
        var bytes = Write(game);
        var ms = watch.Elapsed.TotalMilliseconds;
        Log.Info($"snapshot {bytes.Length} bytes in {ms:0.0} ms");
        return new Snapshot(
            game.CharacterName,
            bytes,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            DateTime.UtcNow,
            GameBuild,
            ms,
            !Checked.Shows
        );
    }

    /// <summary>
    /// Writes the live game into memory, as the game writes its own saves. Main thread, between ticks.
    /// </summary>
    private static byte[] Write(MelvorGame game)
    {
        using var stream = new MemoryStream(1 << 20);
        using (var writer = new SaveWriter(stream, LiveGame.SaveVersion, null, game.Data))
        {
            game.WriteTo(writer);
            writer.Flush();
        }
        return stream.ToArray();
    }

    public static CombatEntity Combat(MelvorGame game) => (CombatEntity)game.Entities.GetObjectById(CombatId);

    /// <summary>The enemy or dungeon the live character is fighting; null when not fighting.</summary>
    public static CurrentTarget? Current(MelvorGame game)
    {
        MainThread.Assert("LiveGame.Current");
        var combat = Combat(game);
        if (!game.ActionManager.ActiveActions.Contains(combat.Action))
            return null;
        var area = combat.Areas.CurrentArea?.Area;
        return area switch
        {
            Dungeon dungeon => new CurrentTarget(dungeon.Id, dungeon.DisplayName.Text, TargetKind.Dungeon),
            CombatArea when combat.Areas.SelectedEnemy is { } enemy => new CurrentTarget(
                area.Id + "/" + enemy.Id,
                enemy.DisplayName.Text,
                TargetKind.Enemy
            ),
            _ => null,
        };
    }

    /// <summary>The live character's loadouts in the player's display order.</summary>
    public static IReadOnlyList<LoadoutInfo> Loadouts(MelvorGame game)
    {
        MainThread.Assert("LiveGame.Loadouts");
        return
        [
            .. Combat(game)
                .Loadouts.LoadoutDisplayOrder.Select(slot => new LoadoutInfo(
                    slot.Data.Id,
                    LoadoutName(slot),
                    slot.IsUnlocked
                )),
        ];
    }

    /// <summary>A loadout's name as the player sees it: the one they gave it, else the game's.</summary>
    public static string LoadoutName(GamesByMalcs.Melvor2.Components.Loadouts.ActiveLoadoutSlot slot) =>
        string.IsNullOrWhiteSpace(slot.Name) ? slot.Data.DisplayName.Text : slot.Name;

    /// <summary>
    /// What the live character wears for a setup, read straight from the game: the gear on now for the character as
    /// is or the loadout it is wearing, else the loadout as the game stores it. Quick, for the setup bar to show at
    /// once; a check copy gives the exact answer later.
    /// </summary>
    public static SetupGear Gear(MelvorGame game, string? slot)
    {
        MainThread.Assert("LiveGame.Gear");
        var combat = Combat(game);
        if (slot == null || slot == combat.Loadouts.CurrentLoadout.Data.Id)
            return SetupGear.Read(combat, null);
        var stored = combat.Loadouts.LoadoutDisplayOrder.FirstOrDefault(l => l.Data.Id == slot)?.Data;
        return stored == null ? SetupGear.None : SetupGear.Read(combat, stored);
    }

    /// <summary>
    /// The game's display name for an id (entity, item, currency, enemy, area, rare event), else the short id; an
    /// enemy or area Spoiler guard hides is <see cref="Spoilers.Mask"/> unless <paramref name="masked"/> is off.
    /// </summary>
    public static string NameOf(MelvorGame game, string id, bool masked = true)
    {
        if (masked && Spoilers.Current.Hides(id))
            return Spoilers.Mask;
        if (game.Entities.TryGetObjectById(id, out var entity))
            return entity.DisplayName.Text;
        if (game.Data.TryGetObject<Currency>(id, out var currency))
            return currency.DisplayName.Text; // not an INamedObject
        if (game.Data.TryGetObject<GamesByMalcs.Melvor2.Components.Equipment.EquipmentSlot>(id, out var slot))
            return slot.DisplayName.Text; // nor is an equipment slot
        if (game.Data.TryGetObject<GamesByMalcs.Melvor2.Components.Toolbelt.ToolbeltSlot>(id, out var belt))
            return belt.DisplayName.Text; // nor a toolbelt slot
        if (game.Data.TryGetObject<GamesByMalcs.Melvor2.Entities.Quests.Quest>(id, out var quest))
            return quest.DisplayName.Text; // nor a quest
        // A star keeps its id ("DeedreeA1"): its own name ("A1") leaves out the constellation it is in.
        foreach (var lookup in NameLookups)
        {
            if (lookup(game, id) is INamedObject named)
                return named.DisplayName.Text;
        }
        // A loadout by the name the player gave it, as the game shows it.
        if (game.Data.TryGetObject<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot>(id, out _))
        {
            var loadout = Combat(game).Loadouts.ActiveLoadouts.Values.FirstOrDefault(s => s.Data.Id == id);
            return loadout != null ? LoadoutName(loadout) : Ids.Brief(id);
        }
        return Ids.Brief(id);
    }

    private static readonly Func<MelvorGame, string, object?>[] NameLookups =
    [
        (g, id) => g.Data.TryGetObject<Item>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<Enemy>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<BaseCombatArea>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<RareEvent>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<GamesByMalcs.Melvor2.Components.Prayers.Prayer>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<AttackStance>(id, out var o) ? o : null,
        (g, id) =>
            g.Data.TryGetObject<GamesByMalcs.Melvor2.Components.MagicSpells.MagicSpell>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<GamesByMalcs.Melvor2.Components.MagicSpells.Imbuement>(id, out var o) ? o : null,
        (g, id) => g.Data.TryGetObject<GamesByMalcs.Melvor2.Entities.Shop.ShopPurchase>(id, out var o) ? o : null,
    ];

    /// <summary>Every item's sell price, for value per currency.</summary>
    public static PriceTable Prices(MelvorGame game)
    {
        var prices = new Dictionary<string, Price>(StringComparer.Ordinal);
        foreach (var item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
        {
            if (item.SellsFor is { Quantity: > 0 } sells)
                prices[item.Id] = new Price(sells.Currency.Id, sells.Quantity);
        }
        return new PriceTable(prices);
    }
}
