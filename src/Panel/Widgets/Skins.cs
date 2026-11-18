using Godot;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Which skin CS2's window shows, from the player's choice, whether mElvUI is installed with a palette CS2 can read,
/// and whether mElvUI's skin is on. Only mElvUI's public surface is used: its loaded-mod entry and palette file, the
/// <c>mElvBaseTheme</c> engine meta (present exactly while its skin is on), the <c>mElvSkip</c> node group and the
/// <c>mElvSkip</c> label-settings meta. Main thread only.
/// </summary>
internal static class Skins
{
    private const string MElvUIId = "mElvUI";

    /// <summary>The engine meta holding the game's own theme while mElvUI's skin is on.</summary>
    private const string BaseThemeMeta = "mElvBaseTheme";

    /// <summary>The node group mElvUI's skin leaves alone, with everything under it.</summary>
    public const string SkipGroup = "mElvSkip";

    /// <summary>The label-settings meta that keeps their colours and sizes from mElvUI's skin.</summary>
    public const string SkipMeta = "mElvSkip";

    private static readonly Model.Note NotInstalled = Model.Note.Of("mElvUI isn't installed");

    private static IReadOnlyDictionary<string, object>? _melvui;
    private static bool _looked;
    private static PaletteLoad _palette = new(null, NotInstalled);
    private static bool? _skinOn;
    private static SkinResolution? _current;

    /// <summary>The skin shown now, and how it is drawn.</summary>
    public static SkinResolution Current => _current ??= Resolve();

    /// <summary>mElvUI's palette, when mElvUI's look can be drawn.</summary>
    public static MelvPalette? Palette
    {
        get
        {
            Find();
            return _palette.Palette;
        }
    }

    /// <summary>Whether the mElvUI skin can be chosen and shown.</summary>
    public static bool MElvUIAvailable => Palette != null;

    /// <summary>Whether mElvUI's skin is restyling the game now.</summary>
    public static bool SkinOn => Godot.Engine.Singleton.HasMeta(BaseThemeMeta);

    /// <summary>The game's own theme while mElvUI's skin is on; null while it is off.</summary>
    public static Theme? BaseTheme =>
        Godot.Engine.Singleton.HasMeta(BaseThemeMeta)
            ? Godot.Engine.Singleton.GetMeta(BaseThemeMeta).As<Theme>()
            : null;

    /// <summary>
    /// Re-reads mElvUI's skin state and the chosen skin; looks for mElvUI again when its skin flipped (it may have
    /// been reloaded) or <paramref name="findAgain"/>.
    /// </summary>
    public static void Refresh(bool findAgain = false)
    {
        if (findAgain || SkinOn != _skinOn)
            _looked = false;
        _current = Resolve();
    }

    private static SkinResolution Resolve()
    {
        Find();
        _skinOn = SkinOn;
        return SkinChoice.Resolve(PanelState.Prefs.Skin, _palette.Reason, _skinOn.Value);
    }

    // mElvUI in the mod table (it may load after CS2, so this is looked up late), and its palette, read once per
    // load of mElvUI: each load publishes a new record. A record without a folder counts as none.
    private static void Find()
    {
        if (_looked)
            return;
        _looked = true;
        var record = ModTable.Process.FindMod(MElvUIId);
        var directory = record == null ? null : ModTable.Get<string>(record, ModTable.Directory);
        var melvui = directory == null ? null : record;
        if (ReferenceEquals(melvui, _melvui) && (melvui == null || _palette.Palette != null))
            return;
        _melvui = melvui;
        _palette =
            directory == null
                ? new(null, NotInstalled)
                : MelvPalette.Load(Path.Combine(directory, "data", "palette.json"));
        if (_palette.Reason is { } reason && melvui != null)
            Log.Warn("mElvUI skin unavailable: " + reason.English());
    }

    /// <summary>The note under the Skin setting, or null when the chosen skin is the one shown.</summary>
    public static string? Note(SkinResolution skin) =>
        skin.Reason is { } reason ? Tr.Format("{0}: showing Base Game.", Tr.Note(reason)) : null;

    public static void Clear()
    {
        _melvui = null;
        _looked = false;
        _palette = new(null, NotInstalled);
        _skinOn = null;
        _current = null;
    }
}
