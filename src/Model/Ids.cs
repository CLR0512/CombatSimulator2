namespace MelvorMods.CombatSimulator2.Model;

/// <summary>The game's namespaced ids: the bridge accepts short ids for the base namespace.</summary>
public static class Ids
{
    public const string Base = "MelvorBase";

    /// <summary>What starts a Slayer master's target key: <c>master:MelvorBase:Kuren</c>.</summary>
    public const string MasterPrefix = "master:";

    /// <summary>The id with its namespace: <c>Golbin</c> becomes <c>MelvorBase:Golbin</c>; others are kept.</summary>
    public static string Full(string id) => id.Contains(':', StringComparison.Ordinal) ? id : Base + ":" + id;

    /// <summary>The short id: <c>MelvorBase:Golbin</c> becomes <c>Golbin</c>; other namespaces are kept.</summary>
    public static string Brief(string id) =>
        id.StartsWith(Base + ":", StringComparison.Ordinal) ? id[(Base.Length + 1)..] : id;

    /// <summary>
    /// A target key (<c>area/enemy</c>, <c>dungeon</c>, <c>master:id</c> or a master's monster) with each part shortened.
    /// </summary>
    public static string BriefKey(string key) =>
        key.StartsWith(AreaPrefix, StringComparison.Ordinal) ? AreaPrefix + Brief(key[AreaPrefix.Length..])
        : key.StartsWith(PinnedPrefix, StringComparison.Ordinal)
            ? PinnedPrefix + string.Join(',', key[PinnedPrefix.Length..].Split(',').Select(BriefKey))
        : IsMaster(key) || IsPart(key)
            ? MasterPrefix + string.Join('/', key[MasterPrefix.Length..].Split('/').Select(Brief))
        : string.Join('/', key.Split('/').Select(Brief));

    /// <summary>A target key with each part given its namespace.</summary>
    public static string FullKey(string key) =>
        IsMaster(key) || IsPart(key)
            ? MasterPrefix + string.Join('/', key[MasterPrefix.Length..].Split('/').Select(Full))
            : string.Join('/', key.Split('/').Select(Full));

    /// <summary>Whether the key names a Slayer master's tasks.</summary>
    public static bool IsMaster(string key) =>
        key.StartsWith(MasterPrefix, StringComparison.Ordinal) && !key.Contains('/', StringComparison.Ordinal);

    /// <summary>
    /// Whether the key names a monster fought for a master: the master's key, then the monster's
    /// (<c>master:MelvorBase:Kuren/MelvorBase:GolbinForest/MelvorBase:Golbin</c>). Its own key keeps it apart from the
    /// monster's own row everywhere a result is keyed, and seeds its fights apart too, whatever order a master's
    /// monsters are fought in.
    /// </summary>
    public static bool IsPart(string key) =>
        key.StartsWith(MasterPrefix, StringComparison.Ordinal) && key.Contains('/', StringComparison.Ordinal);

    /// <summary>What starts an area's key in a gear search: every enemy of the area, each counted equally.</summary>
    public const string AreaPrefix = "area:";

    /// <summary>What starts the pinned targets' key in a gear search: <c>pinned:&lt;key&gt;,&lt;key&gt;</c>, sorted.</summary>
    public const string PinnedPrefix = "pinned:";

    /// <summary>Whether the key names targets a gear search scores together: an area's enemies or the pins.</summary>
    public static bool IsSet(string key) =>
        key.StartsWith(AreaPrefix, StringComparison.Ordinal) || key.StartsWith(PinnedPrefix, StringComparison.Ordinal);

    /// <summary>The pinned targets' key: their keys sorted, so the same pins make the same key.</summary>
    public static string PinnedKey(IEnumerable<string> keys) =>
        PinnedPrefix + string.Join(',', keys.Order(StringComparer.Ordinal));

    /// <summary>A master's target key from its id, short or full.</summary>
    public static string MasterKey(string masterId) => MasterPrefix + Full(masterId);

    /// <summary>The master's id in a master's key or a part's key, as written there.</summary>
    public static string MasterOf(string key)
    {
        var rest = key[MasterPrefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? rest : rest[..slash];
    }

    /// <summary>The key of a master's monster.</summary>
    public static string PartKey(string masterId, string targetKey) => MasterKey(masterId) + "/" + targetKey;

    /// <summary>The monster's own target key in a part's key.</summary>
    public static string PartTarget(string partKey) => partKey[(partKey.IndexOf('/', StringComparison.Ordinal) + 1)..];
}
