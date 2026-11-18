using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Combat;
using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The game's own loaded textures for what the panel shows: enemies, areas and dungeons, items, currencies, skills
/// and stances. Nothing is copied into the mod. A missing texture falls back to the game's "missing artwork" image,
/// and the members used are in the dependency table under <c>art</c>. Main thread only.
/// </summary>
internal static class Art
{
    /// <summary>The dependency feature for the game styles the panel uses.</summary>
    public const string BoxFeature = "panel-look";

    // The game's shared StyleBoxes, by file name under BoxFolder.
    public const string BoxFolder = "res://Assets/UI/StyleBoxes/";
    public const string FrameBox = "panel_popup_1_8x";
    public const string SideBox = "panel_inner_1_8x";
    public const string RowBox = "panel_inner_1_4x";
    public const string RibbonBox = "panel_textured_header_2";
    public const string TabSelectedBox = "tab_selected_8x";
    public const string TabUnselectedBox = "tab_unselected_8x";
    public const string TabHoverBox = "tab_hover_8x";
    public const string TooltipDarkBox = "tooltip_textured_dark_large";
    public const string TooltipLightBox = "tooltip_textured_light_regular";

    /// <summary>
    /// The game's ability card: its item-slot texture, a fine grey-blue edge lighter on top, a dark line inside it and
    /// a see-through dark fill. CS2 builds the box from the texture (the game keeps it inside its attack panel scene).
    /// </summary>
    public const string CardBox = "cs2_card";

    public const string CardTexture = "res://Assets/UI/Textures/Combat/TEX_Combat_ItemSlot.png";

    /// <summary>The game's flat box for a thing not picked, as an enemy's attacks are listed.</summary>
    public const string DeselectedBoxPath = "res://Assets/Styles/Button/panel_deselected.tres";

    /// <summary>The small textured box the game puts a combat stat in: behind a side's letter on its card.</summary>
    public const string BadgeBox = "panel_textured_combat_stat_4x";

    /// <summary>The bar of a foldable section, as Smithing's recipe groups are drawn.</summary>
    public const string SectionBox = "panel_textured_artisan_subcategory";

    public static readonly string[] Boxes =
    [
        FrameBox,
        SideBox,
        RowBox,
        RibbonBox,
        TabSelectedBox,
        TabUnselectedBox,
        TabHoverBox,
        TooltipDarkBox,
        BadgeBox,
        TooltipLightBox,
        SectionBox,
    ];

    private const string MissingPath = "res://Assets/Icons/missing_artwork.png";
    private const string TickOnPath = "res://Assets/Icons/GreenTickbox_64.png";
    public const string TickOffPath = "res://Assets/Icons/EmptyTickbox_64Artboard 1.png";
    public const string PinPath = "res://Assets/Icons/pin.png";
    public const string LockPath = "res://Assets/Icons/lock_icon.png";
    public const string InfoPath = "res://Assets/Icons/info.png";
    private const string SearchPath = "res://Assets/Icons/search.png";
    public const string SearchBarPath = "res://DataEditor/search_icon.png";
    public const string OpenArrowPath = "res://Assets/down-arrow.png";
    public const string ClosedArrowPath = "res://Assets/right-arrow.png";
    private const string PlayPath = "res://Assets/Icons/Play.png";

    // Icons for explaining how CS2 works: the steps and the assumptions of every fight.
    public const string SavePath = "res://Assets/Icons/LocalSave.png";
    public const string CombatPath = "res://Assets/Icons/Combat.png";
    public const string StatisticsPath = "res://Assets/Icons/Statistics_64.png";
    public const string HitpointsPath = "res://Assets/Icons/Hitpoints.png";
    public const string InfinityPath = "res://Assets/Icons/infinity.png";
    public const string XpPath = "res://Assets/Icons/XP_Icon_64.png";
    public const string BankPath = "res://Assets/Icons/Bank_64.png";
    public const string GuideBookPath = "res://Assets/Icons/GuideBook.png";
    public const string TourPath = "res://Assets/Icons/CheckeredFlag.png";
    public const string DiscordPath = "res://Assets/Icons/friends.png";
    public const string ModsPath = "res://Assets/Icons/ModManager_64.png";
    public const string DuplicatePath = "res://Assets/Icons/duplicate.png";
    public const string CopyPath = "res://Assets/Icons/copy.png";
    public const string ShopPath = "res://Assets/Icons/Shop.png";
    public const string QuestsPath = "res://Assets/Icons/Quests.png";
    public const string MasteryPath = "res://Assets/Icons/Mastery_Icon.png";
    public const string SlayerPath = "res://Assets/Icons/Slayer.png";
    public const string PrayerPath = "res://Assets/Icons/Prayer.png";

    // The fixed and fight stats' icons, from the game's combat stat icons.
    private const string TimePath = "res://Assets/Icons/TimeInterval_48.png";
    private const string AccuratePath = "res://Assets/Icons/Accurate.png";
    private const string HitSplatPath = "res://Assets/Icons/HitSplat.png";
    private const string MissSplatPath = "res://Assets/Icons/MissSplat.png";
    private const string PowerfulPath = "res://Assets/Icons/Powerful.png";
    private const string StrengthPath = "res://Assets/Icons/MeleeStrength.png";
    private const string EvasionPath = "res://Assets/Icons/evasion_increase.png";
    private const string BlockPath = "res://Assets/Icons/Block.png";
    private const string SpecialPath = "res://Assets/Icons/special_attack.png";
    private const string AttacksPath = "res://Assets/Icons/AttacksIcon.png";
    private const string SlowedPath = "res://Assets/Icons/slowed.png";
    private const string AmmoPath = "res://Assets/Icons/Combat/AmmoEmpty.png";
    private const string FoodPath = "res://Assets/Icons/Cooking.png";
    private const string PotionPath = "res://Assets/Icons/Combat/PotionEmpty.png";
    private const string ConsumablePath = "res://Assets/Icons/Combat/UtilityEmpty.png";
    private const string RunesPath = "res://Assets/Icons/Runecrafting.png";

    public static readonly string[] Paths =
    [
        MissingPath,
        TickOnPath,
        TickOffPath,
        PinPath,
        LockPath,
        InfoPath,
        SearchPath,
        SearchBarPath,
        OpenArrowPath,
        ClosedArrowPath,
        PlayPath,
        SavePath,
        CombatPath,
        StatisticsPath,
        HitpointsPath,
        InfinityPath,
        XpPath,
        BankPath,
        GuideBookPath,
        TourPath,
        DiscordPath,
        ModsPath,
        ShopPath,
        QuestsPath,
        MasteryPath,
        SlayerPath,
        PrayerPath,
        TimePath,
        AccuratePath,
        HitSplatPath,
        MissSplatPath,
        PowerfulPath,
        StrengthPath,
        EvasionPath,
        BlockPath,
        SpecialPath,
        AttacksPath,
        SlowedPath,
    ];

    private static readonly Dictionary<string, Texture2D?> _files = new(StringComparer.Ordinal);

    private static readonly HashSet<string> _requested = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts loading resources on Godot's loader threads, so the first panel open doesn't decode them on the main
    /// thread. Call once the game is running.
    /// </summary>
    public static void Preload(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_requested.Contains(path) || !ResourceLoader.Exists(path))
                continue;
            if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok)
                _requested.Add(path);
        }
    }

    /// <summary>
    /// A resource by path: from the preload if it was requested (waiting if still loading), else loaded now.
    /// </summary>
    public static T? Resource<T>(string path)
        where T : Resource
    {
        if (_requested.Remove(path))
            return ResourceLoader.LoadThreadedGet(path) as T;
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<T>(path) : null;
    }

    /// <summary>A game image by path, or null if the game no longer has it.</summary>
    public static Texture2D? File(string path)
    {
        if (_files.TryGetValue(path, out var texture))
            return texture;
        texture = Resource<Texture2D>(path);
        return _files[path] = texture;
    }

    public static Texture2D? Missing => File(MissingPath);

    private static Texture2D? Or(Texture2D? texture) => IsArt(texture) ? texture : Missing;

    private static bool Available => Dependencies.IsAvailable(Dependencies.Art);

    /// <summary>The game's question mark, for what Spoiler guard hides.</summary>
    public static Texture2D? Hidden => File(QuestionPath) ?? Missing;

    public static Texture2D? Enemy(MelvorGame game, string id) =>
        Spoilers.Current.Hides(id) ? Hidden
        : Available && game.Data.TryGetObject<Enemy>(id, out var enemy) ? Or(enemy.Sprite)
        : Missing;

    /// <summary>A target's picture: the enemy's sprite, or the dungeon's art.</summary>
    public static Texture2D? Target(MelvorGame game, TargetSpec spec) =>
        spec.Enemy != null ? Enemy(game, spec.Enemy)
        : spec.Master is { } master ? Master(game, master)
        : Area(game, spec.Area);

    /// <summary>A Slayer master's portrait.</summary>
    public static Texture2D? Master(MelvorGame game, string id) =>
        Available && Slayer.SlayerTasks.Master(game, id)?.Data.Icon is { } icon && IsArt(icon) ? icon : Missing;

    /// <summary>The game's placeholder icon for content without art, treated like missing artwork.</summary>
    private const string QuestionPath = "res://Assets/Icons/question.png";

    private static bool IsArt(Texture2D? texture) =>
        texture != null
        && texture is not PlaceholderTexture2D
        && texture.ResourcePath != MissingPath
        && texture.ResourcePath != QuestionPath;

    /// <summary>
    /// An area's or dungeon's art: its cover background, else its own icon, else its first enemy's sprite, else its
    /// category's icon. Placeholders (missing artwork, the question mark) don't count as art.
    /// </summary>
    public static Texture2D? Area(MelvorGame game, string id)
    {
        if (Ids.IsMaster(id))
            return Master(game, Ids.MasterOf(id));
        if (Spoilers.Current.Hides(id))
            return Hidden;
        if (!Available || !game.Data.TryGetObject<BaseCombatArea>(id, out var area))
            return Missing;
        if (IsArt(area.CoverBackground))
            return area.CoverBackground;
        if (IsArt(area.Icon))
            return area.Icon;
        if (area is CombatArea { Enemies.Count: > 0 } combat && IsArt(combat.Enemies[0].Sprite))
            return combat.Enemies[0].Sprite;
        if (IsArt(area.Category?.Icon))
            return area.Category!.Icon;
        return Missing;
    }

    public static Texture2D? Item(MelvorGame game, string id) =>
        Available && game.Data.TryGetObject<Item>(id, out var item) ? Or(item.Icon) : Missing;

    public static Texture2D? Currency(MelvorGame game, string id) =>
        Available && game.Data.TryGetObject<Currency>(id, out var currency) ? Or(currency.Icon) : Missing;

    /// <summary>A skill's or other entity's icon (Combat, Attack, …).</summary>
    public static Texture2D? Skill(MelvorGame game, string id) =>
        Available && game.Entities.TryGetObjectById(id, out var entity) && entity is GameEntity e
            ? Or(e.Icon)
            : Missing;

    /// <summary>
    /// The icon for a metric key: a fixed or fight stat's own, or its skill, item or currency; the Combat icon
    /// otherwise.
    /// </summary>
    public static Texture2D? Metric(MelvorGame game, string key)
    {
        if (StatPath(key) is { } path)
            return Or(File(path));
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        var rest = colon < 0 ? "" : key[(colon + 1)..];
        return (colon < 0 ? key : key[..colon]) switch
        {
            "xp" when rest == "main" => Skill(game, LiveGame.CombatId),
            "xp" => Skill(game, rest),
            "item" => Item(game, rest),
            "currency" or "value" or "worth" or "bury" => Currency(game, rest),
            "used" when rest.IndexOf(':', StringComparison.Ordinal) is var c and > 0 => Item(game, rest[(c + 1)..]),
            "used" when UsedTotalPath(rest) is { } total => Or(File(total)),
            _ => Skill(game, LiveGame.CombatId),
        };
    }

    // The totals of what is used, each with an icon of its own so they tell apart in a list.
    private static string? UsedTotalPath(string total) =>
        total switch
        {
            "food" => FoodPath,
            "potions" => PotionPath,
            "consumables" => ConsumablePath,
            "runes" => RunesPath,
            "ammo" => AmmoPath,
            _ => null,
        };

    private static string? StatPath(string key) =>
        key switch
        {
            Model.Metrics.Deaths or Model.Metrics.DeathRate => HitpointsPath,
            Model.Metrics.KillTime => TimePath,
            Model.Metrics.HighestHit or Model.Metrics.HighestReflect => HitSplatPath,
            Model.Metrics.LowestHp => HitpointsPath,
            Model.FightNumbers.Hit => AccuratePath,
            Model.FightNumbers.PerHit => StrengthPath,
            Model.FightNumbers.Dps => PowerfulPath,
            Model.FightNumbers.EnemyHit => EvasionPath,
            Model.FightNumbers.EnemyPerHit => BlockPath,
            Model.FightNumbers.Taken => HitSplatPath,
            Model.FightNumbers.Overkill => SpecialPath,
            Model.FightNumbers.AttacksPerKill => AttacksPath,
            Model.FightNumbers.MissedPerKill => MissSplatPath,
            Model.FightNumbers.RanOver => SlowedPath,
            _ => null,
        };

    public static void Clear()
    {
        _files.Clear();
        _requested.Clear();
    }
}
