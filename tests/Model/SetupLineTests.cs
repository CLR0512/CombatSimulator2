using System.Text;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SetupLineTests
{
    private static LineId H2(string id) => new(IdHash.Of("MelvorBase:" + id, 2));

    private static LineId H3(string id) => new(IdHash.Of("MelvorBase:" + id, 3));

    private static readonly LineContent Setup = new(
        SetupLine.Version,
        LineKind.Setup,
        26316041,
        null,
        [
            new(ChangeKind.Level, H2("Attack"), LineId.None, 60),
            new(ChangeKind.Level, H2("Defence"), LineId.None, 120),
            new(ChangeKind.Quest, H3("MQ3"), LineId.None, 1),
            new(ChangeKind.Shop, H3("AutoEat2"), LineId.None, 1),
            new(ChangeKind.Guild, H2("Attack"), LineId.None, 2),
            new(ChangeKind.Star, H2("Deedree"), LineId.None, 0b1010_0000_0000_0000_0101_0001, StarMask: true),
            new(ChangeKind.Equip, H2("Weapon"), H3("Iron_Sword"), 0),
            new(ChangeKind.Equip, H2("Offhand"), LineId.None, 0),
            new(ChangeKind.Toolbelt, H2("CombatUtility1"), H3("Whetstone"), 0),
            new(ChangeKind.Stance, LineId.None, H3("Slash"), 0),
            new(ChangeKind.Spell, LineId.None, H3("AirBolt"), 0),
            new(ChangeKind.Imbuement, LineId.None, LineId.None, 0),
            new(ChangeKind.Food, LineId.None, H3("Shrimp"), 0),
            new(ChangeKind.Prayer, H3("Thick_Skin"), LineId.None, 1),
        ]
    );

    private static readonly LineContent Preset = new(
        SetupLine.Version,
        LineKind.Preset,
        26316041,
        "Mithril armour",
        [
            new(ChangeKind.Level, H2("Defence"), LineId.None, 60),
            new(ChangeKind.Star, H3("DeedreeA1"), LineId.None, 0),
            new(ChangeKind.Equip, new LineId(0, "Other:Slot"), new LineId(0, "Other:Sword"), 0),
            new(ChangeKind.Equip, new LineId(0, "MelvorBase:Weapon"), LineId.None, 0),
        ]
    );

    // Written by the encoder once: a line someone holds must read the same in every later CS2.
    private const string SetupText =
        "CS2-9eGZsTcPiCMdiSIfSDFXgCvBrnYcES2T4214E2Ip5IebrnF9hzqpQ1yLAC41tRP2NOPuoQYYu9oBtdDTvf7MurZKMKyuSSHoajh5XgcmUcwtHKw6S90r";
    private const string PresetText =
        "CS2-6cK0Rj8XekWsLHC1dYhAbETZT7iB5v4kWQGG1KAVgKuwk8c0dUMN1ApkxzXok9fz58zm7SGq6EdfyeC5Qvrnq8vaqzRrjeViF6f2MnpZ";

    private static void AssertSame(LineContent expected, LineContent? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(
            (expected.Version, expected.Kind, expected.GameBuild, expected.Name),
            (actual.Version, actual.Kind, actual.GameBuild, actual.Name)
        );
        Assert.Equal(expected.Entries, actual.Entries);
    }

    private static LineProblem ProblemOf(string? text)
    {
        Assert.False(SetupLine.TryRead(text, out var content, out var problem));
        Assert.Null(content);
        return problem;
    }

    [Fact]
    public void ASetupRoundTripsAndIsPinned()
    {
        var text = SetupLine.Write(Setup);
        Assert.StartsWith("CS2-", text, StringComparison.Ordinal);
        Assert.All(text[4..], c => Assert.True(Base62.IsDigit(c)));
        Assert.True(SetupLine.TryRead(text, out var read, out _));
        AssertSame(Setup, read);
        Assert.Equal(SetupText, text);
    }

    [Fact]
    public void APresetRoundTripsWithItsNameAndFullIdsAndIsPinned()
    {
        var text = SetupLine.Write(Preset);
        Assert.True(SetupLine.TryRead(text, out var read, out _));
        AssertSame(Preset, read);
        Assert.Equal(PresetText, text);
    }

    [Fact]
    public void AMastersBlocksAreAnEntryPerEnemyAndReadBackAsTheyWere()
    {
        var content = new LineContent(
            SetupLine.Version,
            LineKind.Setup,
            26316041,
            null,
            [
                new(ChangeKind.Blocks, H2("Kuren"), H3("JackOLantern"), 0),
                new(ChangeKind.Blocks, H2("Kuren"), H3("Scarecrow"), 0),
                new(ChangeKind.Blocks, H2("Hans"), LineId.None, 0),
            ]
        );
        Assert.True(SetupLine.TryRead(SetupLine.Write(content), out var read, out _));
        AssertSame(content, read);
    }

    [Fact]
    public void LoadoutsAndSlayerTaskChoicesReadBackAsTheyWere()
    {
        var content = new LineContent(
            SetupLine.Version,
            LineKind.Setup,
            26316041,
            null,
            [
                new(ChangeKind.Blocks, H2("Kuren"), H3("JackOLantern"), 0),
                new(ChangeKind.Loadouts, H2("Kuren"), H3("Golbin"), (int)H3("Loadout2").Hash),
                new(ChangeKind.Loadouts, H2("Kuren"), H3("Scarecrow"), (int)H3("Loadout1").Hash),
                new(ChangeKind.Loadouts, H2("Hans"), LineId.None, 0),
                new(ChangeKind.SlayerTask, LineId.None, LineId.None, 1),
                new(ChangeKind.SlayerTaskFor, H2("SlimeForest"), H3("RedSlime"), 2),
                new(ChangeKind.SlayerTaskFor, H2("SlimeForest"), H3("GreenSlime"), 0),
            ]
        );
        Assert.True(SetupLine.TryRead(SetupLine.Write(content), out var read, out _));
        AssertSame(content, read);
    }

    [Fact]
    public void PetsBonfiresAndTechniquesReadBackAsTheyWere()
    {
        var setup = new LineContent(
            SetupLine.Version,
            LineKind.Setup,
            26724394,
            null,
            [
                new(ChangeKind.Level, H2("Attack"), LineId.None, 60),
                new(ChangeKind.Pet, H2("Bruno"), LineId.None, 1),
                new(ChangeKind.Pet, H2("Fen"), LineId.None, 0),
                new(ChangeKind.Bonfire, LineId.None, H3("Oak"), 0),
                new(ChangeKind.Technique, H2("Climbing"), LineId.None, 40),
                new(ChangeKind.Technique, H2("Balancing"), LineId.None, 1),
            ]
        );
        Assert.True(SetupLine.TryRead(SetupLine.Write(setup), out var read, out _));
        AssertSame(setup, read);
        var preset = new LineContent(
            SetupLine.Version,
            LineKind.Preset,
            26724394,
            "Out, no Bruno",
            [
                new(ChangeKind.Pet, new LineId(0, "Mod:Pup"), LineId.None, 0),
                new(ChangeKind.Bonfire, LineId.None, LineId.None, 0),
                new(ChangeKind.Technique, new LineId(0, "Mod:Leaping"), LineId.None, 999),
            ]
        );
        Assert.True(SetupLine.TryRead(SetupLine.Write(preset), out read, out _));
        AssertSame(preset, read);
    }

    [Fact]
    public void AMonstersTaskChoicePastSaveIsMalformed()
    {
        var content = new LineContent(
            SetupLine.Version,
            LineKind.Setup,
            26316041,
            null,
            [new(ChangeKind.SlayerTaskFor, H2("SlimeForest"), H3("RedSlime"), 3)]
        );
        Assert.False(SetupLine.TryRead(SetupLine.Write(content), out _, out var problem));
        Assert.Equal(LineProblem.Malformed, problem);
    }

    [Fact]
    public void TheSameBlockTwiceIsMalformed()
    {
        var twice = new LineContent(
            SetupLine.Version,
            LineKind.Setup,
            26316041,
            null,
            [
                new(ChangeKind.Blocks, H2("Kuren"), H3("Scarecrow"), 0),
                new(ChangeKind.Blocks, H2("Kuren"), H3("Scarecrow"), 0),
            ]
        );
        Assert.False(SetupLine.TryRead(SetupLine.Write(twice), out _, out var problem));
        Assert.Equal(LineProblem.Malformed, problem);
    }

    [Theory]
    [InlineData("  {0}\n")]
    [InlineData("`{0}`")]
    [InlineData("||{0}||")]
    [InlineData("<{0}>")]
    [InlineData(" || `{0}` || ")]
    public void WhatChatPutsAroundALineIsDropped(string wrapping)
    {
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, wrapping, SetupLine.Write(Setup));
        Assert.True(SetupLine.TryRead(text, out var read, out _));
        AssertSame(Setup, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\t ")]
    [InlineData("``")]
    public void NothingIsEmpty(string? text) => Assert.Equal(LineProblem.Empty, ProblemOf(text));

    [Theory]
    [InlineData("hello there")]
    [InlineData("cs2-abc")]
    [InlineData("CS2 abc")]
    [InlineData("CS2-abc def")]
    [InlineData("CS2-abc_def")]
    [InlineData("CS2-abc+/=")]
    [InlineData("https://example.com/CS2-abc")]
    public void OtherTextIsNotALine(string text) => Assert.Equal(LineProblem.NotALine, ProblemOf(text));

    [Fact]
    public void OverTheLongestLineIsTooLong()
    {
        // A simulation line may reach 2,000 characters; a setup line over 1,000 is refused once read as one.
        Assert.Equal(LineProblem.TooLong, ProblemOf("CS2-" + new string('A', SetupLine.SimulationMaxLength - 3)));
        Assert.NotEqual(LineProblem.TooLong, ProblemOf("CS2-" + new string('A', SetupLine.SimulationMaxLength - 4)));
    }

    [Fact]
    public void AChangedOrCutLineIsDamaged()
    {
        var text = SetupLine.Write(Setup);
        Assert.Equal(LineProblem.Damaged, ProblemOf("CS2-"));
        Assert.Equal(LineProblem.Damaged, ProblemOf(text[..20]));
        Assert.Equal(LineProblem.Damaged, ProblemOf(text[..^5]));
        for (var at = 4; at < text.Length; at++)
        {
            var changed = text.ToCharArray();
            changed[at] = changed[at] == 'A' ? 'B' : 'A';
            Assert.Equal(LineProblem.Damaged, ProblemOf(new string(changed)));
        }
    }

    // A payload with a valid check value, as only a writer of lines could make.
    private static string Line(params byte[] body)
    {
        ushort crc = 0xFFFF;
        foreach (var b in body)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x1021 : crc << 1);
        }
        return "CS2-" + Base62.Encode([.. body, (byte)(crc >> 8), (byte)crc]);
    }

    private static readonly byte[] Attack =
    [
        (byte)(IdHash.Of("MelvorBase:Attack", 2) >> 8),
        (byte)IdHash.Of("MelvorBase:Attack", 2),
    ];

    [Fact]
    public void ALaterVersionIsANewerFormat() =>
        Assert.Equal(LineProblem.NewerFormat, ProblemOf(Line(3, 0, 1, 0, 0, 0, 0)));

    [Fact]
    public void AnEmptySetupIsALine()
    {
        Assert.True(SetupLine.TryRead(Line(1, 0, 1, 0, 0), out var content, out _));
        Assert.Empty(content!.Entries);
        Assert.Equal(1ul, content.GameBuild);
    }

    public static TheoryData<string, byte[]> MalformedPayloads =>
        new()
        {
            { "an unknown line kind", [1, 2, 1, 0, 0] },
            { "a setup with a name", [1, 0, 1, 1, (byte)'x', 0] },
            { "a name past the end", [1, 1, 1, 5, (byte)'x', 0] },
            { "a name that isn't text", [1, 1, 1, 2, 0xC3, 0x28, 0] },
            { "twenty-two groups", [1, 0, 1, 0, 22] },
            { "an unknown change kind", [1, 0, 1, 0, 1, 0x4F, 0] },
            { "single stars on another kind", [1, 0, 1, 0, 1, 0x10, 0] },
            { "an unknown group mark", [1, 0, 1, 0, 1, 0x80, 0] },
            { "an entry count past the end", [1, 0, 1, 0, 1, 0, 3, Attack[0], Attack[1], 60] },
            { "a level of 0", [1, 0, 1, 0, 1, 0, 1, Attack[0], Attack[1], 0] },
            { "a level of 1000", [1, 0, 1, 0, 1, 0, 1, Attack[0], Attack[1], 0xE8, 0x07] },
            { "a rank of 10", [1, 0, 1, 0, 1, 3, 1, Attack[0], Attack[1], 10] },
            { "a count of 0", [1, 0, 1, 0, 1, 2, 1, 1, 2, 3, 0] },
            { "a prayer that is neither on nor off", [1, 0, 1, 0, 1, 12, 1, 1, 2, 3, 2] },
            { "two entries for one key", [1, 0, 1, 0, 1, 0, 2, Attack[0], Attack[1], 60, Attack[0], Attack[1], 70] },
            { "two groups of one kind", [1, 0, 1, 0, 2, 0, 0, 0, 0] },
            { "two foods", [1, 0, 1, 0, 1, 10, 2, 1, 2, 3, 4, 5, 6] },
            { "no stance", [1, 0, 1, 0, 1, 7, 1, 0, 0, 0] },
            { "a subject that is none", [1, 0, 1, 0, 1, 0, 1, 0, 0, 60] },
            { "a full id past its limit", [1, 0, 1, 0, 1, 0x20, 1, 65] },
            { "bytes left over", [1, 0, 1, 0, 0, 9] },
            { "version 0", [0, 0, 1, 0, 0] },
            { "a pet that is neither on nor off", [1, 0, 1, 0, 1, 0x41, 1, 1, 2, 2] },
            { "a technique at level 0", [1, 0, 1, 0, 1, 0x43, 1, 1, 2, 0] },
            { "two bonfires", [1, 0, 1, 0, 1, 0x42, 2, 1, 2, 3, 4, 5, 6] },
            // An older CS2 meets a kind added after it this way: refused, never read as another kind.
            { "the kind after the last one", [1, 0, 1, 0, 1, 0x44, 1, 1, 2, 1] },
        };

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void AWholeLineThatIsNoSetupIsMalformed(string what, byte[] body)
    {
        Assert.NotEmpty(what);
        Assert.Equal(LineProblem.Malformed, ProblemOf(Line(body)));
    }

    [Fact]
    public void APresetsNameComesBackCleaned()
    {
        var name = Encoding.UTF8.GetBytes(" [b]Arm\u0007our[/b]\r\n " + new string('x', 60));
        var text = Line([1, 1, 1, (byte)name.Length, .. name, 0]);
        Assert.True(SetupLine.TryRead(text, out var content, out _));
        // Control characters gone, trimmed, bounded; markup brackets stay as the plain characters they are.
        Assert.Equal(("[b]Armour[/b] " + new string('x', 60))[..RunHistory.MaxNameLength], content!.Name);
        Assert.DoesNotContain(content.Name!, char.IsControl);
        Assert.Equal(RunHistory.MaxNameLength, content.Name!.Length);
    }

    [Fact]
    public void ALineTooLongToWriteIsRefused()
    {
        var entries = Enumerable
            .Range(0, 250)
            .Select(i => new LineEntry(ChangeKind.Prayer, new LineId((uint)i + 1), LineId.None, 1))
            .ToList();
        var content = new LineContent(SetupLine.Version, LineKind.Setup, 1, null, entries);
        Assert.False(SetupLine.TryWrite(content, out var text));
        Assert.True(text.Length > SetupLine.MaxLength);
        Assert.Throws<InvalidOperationException>(() => SetupLine.Write(content));
    }

    [Fact]
    public void RandomPayloadsNeverThrow()
    {
        var random = new Random(19);
        var read = 0;
        Assert.True(Base62.TryDecode(SetupLine.Write(Setup).AsSpan(4), 750, out var real));
        for (var i = 0; i < 10_000; i++)
        {
            byte[] body;
            if (i % 2 == 0)
            {
                // A real line's payload with a few bytes changed: the reader gets deep into it.
                body = real[..^2];
                for (var n = random.Next(0, 4); n > 0; n--)
                    body[random.Next(body.Length)] = (byte)random.Next(256);
            }
            else
            {
                body = new byte[random.Next(0, 120)];
                random.NextBytes(body);
            }
            // One in five without a check value, as a mistyped line would be.
            var text = i % 5 == 0 ? "CS2-" + Base62.Encode(body) : Line(body);
            if (SetupLine.TryRead(text, out var content, out var problem))
            {
                read++;
                Assert.NotNull(content);
                Assert.True(SetupLine.TryWrite(content, out _));
            }
            else
                Assert.Contains(problem, new[] { LineProblem.Malformed, LineProblem.Damaged, LineProblem.NewerFormat });
        }
        Assert.True(read > 0);
    }

    [Fact]
    public void RandomTextNeverThrows()
    {
        var random = new Random(23);
        const string Alphabet = "CS2-abcXYZ019 `|<>\n_+/=é";
        for (var i = 0; i < 10_000; i++)
        {
            var text = new string([
                .. Enumerable.Range(0, random.Next(0, 60)).Select(_ => Alphabet[random.Next(Alphabet.Length)]),
            ]);
            SetupLine.TryRead(i % 2 == 0 ? "CS2-" + text : text, out _, out _);
        }
    }
}
