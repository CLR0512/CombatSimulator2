using System.Text;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>What a line carries.</summary>
public enum LineKind
{
    /// <summary>A whole setup: every value CS2 can set.</summary>
    Setup,

    /// <summary>A preset: its name and its own changes.</summary>
    Preset,

    /// <summary>The Simulate page: both sides, the targets left out and the settings (format version 2).</summary>
    Simulation,
}

/// <summary>Why a text isn't a line that can be used. The first that applies, in this order.</summary>
public enum LineProblem
{
    /// <summary>Nothing but white space.</summary>
    Empty,

    /// <summary>Not <c>CS2-</c> followed by letters and digits.</summary>
    NotALine,

    /// <summary>More than <see cref="SetupLine.MaxLength"/> characters (a simulation line, <see cref="SetupLine.SimulationMaxLength"/>).</summary>
    TooLong,

    /// <summary>Cut short or mistyped: its check value doesn't match.</summary>
    Damaged,

    /// <summary>Made by a later CS2 in a format this one doesn't read.</summary>
    NewerFormat,

    /// <summary>Whole, but what it says isn't a setup.</summary>
    Malformed,
}

/// <summary>
/// A game id in a line: its hash, or the id in full when two ids of its registry share a hash. <c>None</c> is no id
/// (an emptied slot, no food). When one id of a kind's entries is in full, all of that kind's are.
/// </summary>
public sealed record LineId(uint Hash, string? Full = null)
{
    public static readonly LineId None = new(0);

    public bool IsNone => Hash == 0 && Full == null;
}

/// <summary>
/// One entry of a line. <c>Subject</c> is the skill, slot, quest, purchase, prayer or star the entry is about (or a
/// constellation, with <c>StarMask</c>: <c>Number</c> then has one bit per star in the game's order); <c>Item</c>
/// is the item, stance, spell or imbuement it sets; <c>Number</c> a level, count, rank, or 1 for on or done.
/// </summary>
public sealed record LineEntry(ChangeKind Kind, LineId Subject, LineId Item, int Number, bool StarMask = false);

/// <summary>
/// What a line says, before its ids are looked up in a game. A simulation line's <see cref="Entries"/> are side A's,
/// and <see cref="Simulation"/> holds the rest.
/// </summary>
public sealed record LineContent(
    int Version,
    LineKind Kind,
    ulong GameBuild,
    string? Name,
    IReadOnlyList<LineEntry> Entries
)
{
    public LineSimulation? Simulation { get; init; }
}

/// <summary>
/// A setup or a preset as one line of text to hand to another player: <c>CS2-</c> then letters and digits. Reading
/// treats the text as untrusted: every length and number is bounded, nothing in it is run or looked up here, and
/// the first thing wrong refuses the whole line.
/// </summary>
public static partial class SetupLine
{
    public const string Prefix = "CS2-";
    public const int MaxLength = 1000;

    /// <summary>The format of setup and preset lines, which an older CS2 reads.</summary>
    public const int Version = 1;

    // Every kind once, and the stars twice: one by one and by constellation.
    private static readonly int MaxGroups = Enum.GetValues<ChangeKind>().Length + 1;
    private const int MaxEntries = 255;
    private const int MaxNameBytes = 120;
    private const int MaxIdBytes = 64;
    private const int HighestLevel = 999;

    // A group's first byte: the kind (its low four bits, and HighKind for the kinds from 16 on), and these marks.
    private const int KindBits = 0x0F;
    private const int SingleStars = 0x10;
    private const int FullIds = 0x20;
    private const int HighKind = 0x40;

    private static int HeadOf(ChangeKind kind) => ((int)kind & KindBits) | ((int)kind > KindBits ? HighKind : 0);

    private static int KindOf(int head) => (head & KindBits) | ((head & HighKind) != 0 ? KindBits + 1 : 0);

    /// <summary>The line for <paramref name="content"/>; false when it would be longer than a line may be.</summary>
    public static bool TryWrite(LineContent content, out string text)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Kind == LineKind.Simulation)
            return TryWriteSimulation(content, out text);
        var bytes = new List<byte> { Version, (byte)content.Kind };
        Varint(bytes, content.GameBuild);
        var name = Encoding.UTF8.GetBytes(content.Kind == LineKind.Preset ? content.Name ?? "" : "");
        if (name.Length > MaxNameBytes)
            throw new ArgumentException($"a name is at most {MaxNameBytes} bytes", nameof(content));
        bytes.Add((byte)name.Length);
        bytes.AddRange(name);
        WriteGroups(bytes, content.Entries);
        text = Finish(bytes);
        return text.Length <= MaxLength;
    }

    // A side's entries, grouped by kind.
    private static void WriteGroups(List<byte> bytes, IReadOnlyList<LineEntry> entries)
    {
        var groups = entries
            .GroupBy(e => (e.Kind, Single: e.Kind == ChangeKind.Star && !e.StarMask))
            .OrderBy(g => g.Key.Kind)
            .ThenBy(g => g.Key.Single)
            .ToList();
        if (groups.Count > MaxGroups || groups.Any(g => g.Count() > MaxEntries))
            throw new ArgumentException("too many entries for a line", nameof(entries));
        bytes.Add((byte)groups.Count);
        foreach (var group in groups)
        {
            var full = group.Any(e => e.Subject.Full != null || e.Item.Full != null);
            bytes.Add((byte)(HeadOf(group.Key.Kind) | (group.Key.Single ? SingleStars : 0) | (full ? FullIds : 0)));
            bytes.Add((byte)group.Count());
            foreach (var entry in group)
                WriteEntry(bytes, entry, full);
        }
    }

    // The check value after the payload, then the text.
    private static string Finish(List<byte> bytes)
    {
        var check = Check(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes));
        bytes.Add((byte)(check >> 8));
        bytes.Add((byte)check);
        return Prefix + Base62.Encode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes));
    }

    /// <summary>The line for <paramref name="content"/>. Throws when it would be longer than a line may be.</summary>
    public static string Write(LineContent content) =>
        TryWrite(content, out var text)
            ? text
            : throw new InvalidOperationException(
                $"the line would be {text.Length} characters, over {LengthOf(content.Kind)}"
            );

    /// <summary>The longest a line of <paramref name="kind"/> may be.</summary>
    public static int LengthOf(LineKind kind) => kind == LineKind.Simulation ? SimulationMaxLength : MaxLength;

    private static void WriteEntry(List<byte> bytes, LineEntry entry, bool full)
    {
        var shape = Shape(entry.Kind, entry.StarMask);
        if (shape.Subject > 0)
            WriteId(bytes, entry.Subject, shape.Subject, full);
        if (shape.Item)
            WriteId(bytes, entry.Item, 3, full);
        switch (shape.Number)
        {
            case NumberForm.Byte:
                bytes.Add((byte)entry.Number);
                break;
            case NumberForm.Varint:
                Varint(bytes, (ulong)entry.Number);
                break;
            case NumberForm.Mask:
                bytes.Add((byte)(entry.Number >> 16));
                bytes.Add((byte)(entry.Number >> 8));
                bytes.Add((byte)entry.Number);
                break;
            default:
                break;
        }
    }

    private static void WriteId(List<byte> bytes, LineId id, int size, bool full)
    {
        if (full)
        {
            // In a group written in full, an id without its text is none.
            var text = Encoding.UTF8.GetBytes(id.Full ?? "");
            if (text.Length > MaxIdBytes)
                throw new ArgumentException($"an id is at most {MaxIdBytes} bytes");
            bytes.Add((byte)text.Length);
            bytes.AddRange(text);
            return;
        }
        for (var shift = (size - 1) * 8; shift >= 0; shift -= 8)
            bytes.Add((byte)(id.Hash >> shift));
    }

    private enum NumberForm
    {
        None,
        Byte,
        Varint,
        Mask,
    }

    // What an entry of a kind holds: the size of its subject's hash (0: no subject), whether it sets an item, and
    // how its number is written.
    private static (int Subject, bool Item, NumberForm Number) Shape(ChangeKind kind, bool starMask) =>
        kind switch
        {
            ChangeKind.Level => (2, false, NumberForm.Varint),
            ChangeKind.Quest => (3, false, NumberForm.Byte),
            ChangeKind.Shop => (3, false, NumberForm.Varint),
            ChangeKind.Guild => (2, false, NumberForm.Byte),
            ChangeKind.Star => starMask ? (2, false, NumberForm.Mask) : (3, false, NumberForm.None),
            ChangeKind.Equip or ChangeKind.Toolbelt => (2, true, NumberForm.None),
            ChangeKind.Prayer => (3, false, NumberForm.Byte),
            // One entry per enemy blocked, or one with no enemy for none.
            ChangeKind.Blocks => (2, true, NumberForm.None),
            // One entry per monster (the master, the enemy, the loadout slot's hash), or one with no enemy for none.
            ChangeKind.Loadouts => (2, true, NumberForm.Varint),
            ChangeKind.SlayerTask => (0, false, NumberForm.Byte),
            // The monster's area and enemy; 0 off, 1 on, 2 as the save.
            ChangeKind.SlayerTaskFor => (2, true, NumberForm.Byte),
            // 0 taken away, 1 added.
            ChangeKind.Pet => (2, false, NumberForm.Byte),
            // The tier, or none when the bonfire is out.
            ChangeKind.Bonfire => (0, true, NumberForm.None),
            ChangeKind.Technique => (2, false, NumberForm.Varint),
            _ => (0, true, NumberForm.None),
        };

    private static void Varint(List<byte> bytes, ulong value)
    {
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
    }

    // CRC-16 (CCITT) over the bytes: a mistyped or missing character changes it.
    private static ushort Check(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}
