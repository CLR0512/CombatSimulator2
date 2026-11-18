using System.Text;

namespace MelvorMods.CombatSimulator2.Model;

// Reading a line: the text's form, the check value, the version, then the structure with every bound.
public static partial class SetupLine
{
    /// <summary>
    /// Reads <paramref name="text"/> as pasted (white space, backticks, <c>||</c> and <c>&lt;&gt;</c> around it are
    /// dropped). False with the first problem; nothing is applied or looked up.
    /// </summary>
    public static bool TryRead(string? text, out LineContent? content, out LineProblem problem)
    {
        content = null;
        var line = Unwrapped(text ?? "");
        if (line.Length == 0)
            return Fail(LineProblem.Empty, out problem);
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            return Fail(LineProblem.NotALine, out problem);
        if (line.Length > SimulationMaxLength)
            return Fail(LineProblem.TooLong, out problem);
        var digits = line.AsSpan(Prefix.Length);
        foreach (var c in digits)
        {
            if (!Base62.IsDigit(c))
                return Fail(LineProblem.NotALine, out problem);
        }
        // The shortest payload: version, kind, build, name length, group count and the check value.
        if (!Base62.TryDecode(digits, SimulationMaxPayload, out var bytes) || bytes.Length < 7)
            return Fail(LineProblem.Damaged, out problem);
        var body = bytes.AsSpan(0, bytes.Length - 2);
        if (Check(body) != (bytes[^2] << 8 | bytes[^1]))
            return Fail(LineProblem.Damaged, out problem);
        if (body[0] > SimulationVersion)
            return Fail(LineProblem.NewerFormat, out problem);
        if (body[0] == Version && line.Length > MaxLength)
            return Fail(LineProblem.TooLong, out problem);
        var reader = new Reader(bytes, body.Length);
        content = body[0] switch
        {
            Version => Content(ref reader),
            SimulationVersion => SimulationContent(ref reader),
            _ => null,
        };
        if (content == null)
            return Fail(LineProblem.Malformed, out problem);
        problem = default;
        return true;
    }

    private static bool Fail(LineProblem why, out LineProblem problem)
    {
        problem = why;
        return false;
    }

    // What chat programs and people put around a pasted line: spaces, code marks, spoiler bars, link brackets.
    private static string Unwrapped(string text)
    {
        var line = text.Trim();
        for (var again = true; again; )
        {
            again = false;
            foreach (var (open, close) in new[] { ("`", "`"), ("||", "||"), ("<", ">") })
            {
                if (
                    line.Length >= open.Length + close.Length
                    && line.StartsWith(open, StringComparison.Ordinal)
                    && line.EndsWith(close, StringComparison.Ordinal)
                )
                {
                    line = line[open.Length..^close.Length].Trim();
                    again = true;
                }
            }
        }
        return line;
    }

    // The payload after its version byte; null for anything out of bounds, unknown, repeated or left over.
    private static LineContent? Content(ref Reader reader)
    {
        reader.Byte(); // the version, checked by the caller
        var kindByte = reader.Byte();
        if (!reader.Ok || kindByte > (int)LineKind.Preset)
            return null;
        var kind = (LineKind)kindByte;
        var build = reader.Varint();
        var nameLength = reader.Byte();
        if (!reader.Ok || nameLength > MaxNameBytes || (kind == LineKind.Setup && nameLength > 0))
            return null;
        string? name = null;
        if (kind == LineKind.Preset)
        {
            if (reader.Text(nameLength) is not { } raw)
                return null;
            // Shown as plain text wherever it goes; cleaned here so no caller can forget.
            name = RunHistory.CleanName(raw);
        }
        var entries = Groups(ref reader);
        return entries != null && reader.AtEnd ? new LineContent(Version, kind, build, name, entries) : null;
    }

    // A side's groups of entries; null for anything out of bounds, unknown or repeated.
    private static List<LineEntry>? Groups(ref Reader reader)
    {
        var groups = reader.Byte();
        if (!reader.Ok || groups > MaxGroups)
            return null;
        var entries = new List<LineEntry>();
        var seenGroups = new HashSet<int>();
        var seenKeys = new HashSet<(ChangeKind, uint, string?)>();
        for (var g = 0; g < groups; g++)
        {
            var head = reader.Byte();
            var count = reader.Byte();
            var changeKind = KindOf(head);
            var single = (head & SingleStars) != 0;
            var full = (head & FullIds) != 0;
            if (
                !reader.Ok
                || (head & ~(KindBits | SingleStars | FullIds | HighKind)) != 0
                || !Enum.IsDefined((ChangeKind)changeKind)
                || (single && changeKind != (int)ChangeKind.Star)
                || !seenGroups.Add(head & (KindBits | SingleStars | HighKind))
            )
                return null;
            var of = (ChangeKind)changeKind;
            var mask = of == ChangeKind.Star && !single;
            var shape = Shape(of, mask);
            // A kind without a subject is one value: the food, the stance.
            if (shape.Subject == 0 && count > 1)
                return null;
            for (var i = 0; i < count; i++)
            {
                var subject = shape.Subject > 0 ? reader.Id(shape.Subject, full) : LineId.None;
                var item = shape.Item ? reader.Id(3, full) : LineId.None;
                var number = shape.Number switch
                {
                    NumberForm.Byte => reader.Byte(),
                    NumberForm.Varint => (int)Math.Min(reader.Varint(), int.MaxValue),
                    NumberForm.Mask => reader.Byte() << 16 | reader.Byte() << 8 | reader.Byte(),
                    _ => 0,
                };
                if (
                    !reader.Ok
                    || subject == null
                    || item == null
                    || (shape.Subject > 0 && subject.IsNone)
                    || !InRange(of, number, item)
                    || !seenKeys.Add(
                        of is ChangeKind.Blocks or ChangeKind.Loadouts or ChangeKind.SlayerTaskFor
                            ? (of, subject.Hash, $"{subject.Full}/{item.Hash}/{item.Full}")
                            : (of, subject.Hash, subject.Full)
                    )
                )
                    return null;
                entries.Add(new LineEntry(of, subject, item, number, mask));
            }
        }
        return entries;
    }

    // The format's own limits; what the game allows is for the game to say.
    private static bool InRange(ChangeKind kind, int number, LineId item) =>
        kind switch
        {
            ChangeKind.Level or ChangeKind.Technique => number is >= Change.LowestLevel and <= HighestLevel,
            ChangeKind.Shop => number is >= 1 and <= Change.HighestCount,
            ChangeKind.Guild => number is >= 0 and <= Change.HighestRank,
            ChangeKind.Quest or ChangeKind.Prayer or ChangeKind.SlayerTask or ChangeKind.Pet => number is 0 or 1,
            ChangeKind.SlayerTaskFor => number is >= 0 and <= 2,
            // The game always has a stance and a spell: neither can be none.
            ChangeKind.Stance or ChangeKind.Spell => !item.IsNone,
            _ => true,
        };

    // Reads the payload's bytes in order; past the end it reads zeros and Ok turns false.
    private ref struct Reader(byte[] bytes, int length)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes.AsSpan(0, length);
        private int _at;

        public bool Ok { get; private set; } = true;

        public readonly bool AtEnd => Ok && _at == _bytes.Length;

        public int Byte()
        {
            if (_at >= _bytes.Length)
            {
                Ok = false;
                return 0;
            }
            return _bytes[_at++];
        }

        public ulong Varint()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var b = Byte();
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return value;
            }
            Ok = false;
            return 0;
        }

        // Strict UTF-8 of exactly this many bytes; null when it runs past the end or isn't text.
        public string? Text(int count)
        {
            if (_at + count > _bytes.Length)
            {
                Ok = false;
                return null;
            }
            try
            {
                var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(_bytes.Slice(_at, count));
                _at += count;
                return text;
            }
            catch (ArgumentException)
            {
                Ok = false;
                return null;
            }
        }

        public LineId? Id(int size, bool full)
        {
            if (full)
            {
                var count = Byte();
                if (!Ok || count > MaxIdBytes)
                {
                    Ok = false;
                    return null;
                }
                if (count == 0)
                    return LineId.None;
                return Text(count) is { } text ? new LineId(0, text) : null;
            }
            uint hash = 0;
            for (var i = 0; i < size; i++)
                hash = hash << 8 | (uint)Byte();
            return Ok ? new LineId(hash) : null;
        }
    }
}
