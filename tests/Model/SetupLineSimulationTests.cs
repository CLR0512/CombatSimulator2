using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SetupLineSimulationTests
{
    private static LineId H2(string id) => new(IdHash.Of("MelvorBase:" + id, 2));

    private static LineId H3(string id) => new(IdHash.Of("MelvorBase:" + id, 3));

    private static LineId Target(string key) => new(IdHash.Of(key, 3));

    private static readonly LineEntry[] SideA =
    [
        new(ChangeKind.Level, H2("Attack"), LineId.None, 60),
        new(ChangeKind.Equip, H2("Weapon"), H3("Iron_Sword"), 0),
        new(ChangeKind.Stance, LineId.None, H3("Slash"), 0),
    ];

    private static readonly LineEntry[] SideB =
    [
        new(ChangeKind.Level, H2("Attack"), LineId.None, 70),
        new(ChangeKind.Equip, H2("Weapon"), H3("Steel_Sword"), 0),
        new(ChangeKind.Food, LineId.None, H3("Shrimp"), 0),
    ];

    private static readonly string[] Skills =
    [
        "Attack",
        "Strength",
        "Defence",
        "Hitpoints",
        "Ranged",
        "Magic",
        "Prayer",
        "Slayer",
    ];

    private static LineContent Line(LineSimulation sim) =>
        new(SetupLine.SimulationVersion, LineKind.Simulation, 26316041, null, sim.A) { Simulation = sim };

    private static LineSimulation Compared(bool seed = true, TrialLimits? limits = null) =>
        new(
            SideA,
            SideB,
            IgnoreRequirementsA: false,
            IgnoreRequirementsB: true,
            [Target("MelvorBase:Farmlands/MelvorBase:Cow"), Target("MelvorBase:Farmlands/MelvorBase:Chicken")],
            500,
            seed ? 42UL : null,
            Fresh: true,
            StopEarly: true,
            0.05,
            limits,
            new LineId(0, "xp:main"),
            TimeUnit.Hour
        );

    private static LineSimulation Read(string text)
    {
        Assert.True(SetupLine.TryRead(text, out var content, out var problem), problem.ToString());
        Assert.Equal(LineKind.Simulation, content!.Kind);
        Assert.Equal(SetupLine.SimulationVersion, content.Version);
        return content.Simulation!;
    }

    private static void AssertSame(LineSimulation expected, LineSimulation actual)
    {
        Assert.Equal(Grouped(expected.A), actual.A);
        Assert.Equal(expected.B == null ? null : Grouped(expected.B), actual.B);
        Assert.Equal(expected.Ignored, actual.Ignored);
        Assert.Equal(
            (
                expected.IgnoreRequirementsA,
                expected.IgnoreRequirementsB,
                expected.Trials,
                expected.Seed,
                expected.Fresh,
                expected.StopEarly,
                expected.StopPrecision,
                expected.Metric,
                expected.TimeUnit
            ),
            (
                actual.IgnoreRequirementsA,
                actual.IgnoreRequirementsB,
                actual.Trials,
                actual.Seed,
                actual.Fresh,
                actual.StopEarly,
                actual.StopPrecision,
                actual.Metric,
                actual.TimeUnit
            )
        );
        Assert.Equal(expected.Limits?.EnemySeconds, actual.Limits?.EnemySeconds);
        Assert.Equal(expected.Limits?.DungeonSeconds, actual.Limits?.DungeonSeconds);
    }

    [Fact]
    public void BothSidesAndTheSettingsRoundTrip()
    {
        var sim = Compared(limits: new TrialLimits { EnemySeconds = 120, DungeonSeconds = 900 });

        AssertSame(sim, Read(SetupLine.Write(Line(sim))));
    }

    [Fact]
    public void OneSideWithANewSeedEachRunRoundTrips()
    {
        var sim = Compared(seed: false) with { B = null, Ignored = [], StopEarly = false, TimeUnit = TimeUnit.Kill };

        var back = Read(SetupLine.Write(Line(sim)));

        AssertSame(sim, back);
        Assert.Null(back.B);
        Assert.Null(back.Seed);
        Assert.Null(back.Limits);
    }

    [Fact]
    public void IgnoredTargetsInFullRoundTrip()
    {
        var sim = Compared() with
        {
            Ignored = [new LineId(0, "Other:Area/Other:Enemy"), new LineId(0, "MelvorBase:A/MelvorBase:B")],
        };

        Assert.Equal(sim.Ignored, Read(SetupLine.Write(Line(sim))).Ignored);
    }

    [Fact]
    public void TwoFullSidesWithIgnoredTargetsStayUnderTheLimit()
    {
        LineEntry[] Full(int level) =>
            [
                .. Skills.Select(s => new LineEntry(ChangeKind.Level, H2(s), LineId.None, level)),
                .. Enumerable
                    .Range(0, 14)
                    .Select(i => new LineEntry(ChangeKind.Equip, H2("Slot" + i), H3("Item" + i + level), 0)),
                .. Enumerable
                    .Range(0, 12)
                    .Select(i => new LineEntry(ChangeKind.Shop, H3("Purchase" + i), LineId.None, 1)),
                .. Enumerable
                    .Range(0, 10)
                    .Select(i => new LineEntry(ChangeKind.Quest, H3("Quest" + i), LineId.None, 1)),
                new(ChangeKind.Stance, LineId.None, H3("Slash"), 0),
                new(ChangeKind.Spell, LineId.None, H3("AirBolt"), 0),
                new(ChangeKind.Food, LineId.None, H3("Shrimp"), 0),
                new(ChangeKind.Prayer, H3("Thick_Skin"), LineId.None, 1),
            ];
        var sim = Compared() with
        {
            A = Full(60),
            B = Full(99),
            Ignored = [.. Enumerable.Range(0, 40).Select(i => Target($"MelvorBase:Area{i}/MelvorBase:Enemy{i}"))],
        };

        Assert.True(SetupLine.TryWrite(Line(sim), out var text), $"{text.Length} characters");
        Assert.True(text.Length <= SetupLine.SimulationMaxLength);
        AssertSame(sim, Read(text));
    }

    [Fact]
    public void ASetupLineIsStillWrittenAndReadAsFormatOne()
    {
        var setup = new LineContent(SetupLine.Version, LineKind.Setup, 26316041, null, SideA);

        var text = SetupLine.Write(setup);

        Assert.Equal(SetupLine.Version, Payload(text)[0]);
        Assert.True(SetupLine.TryRead(text, out var back, out _));
        Assert.Equal(LineKind.Setup, back!.Kind);
        Assert.Null(back.Simulation);
    }

    [Fact]
    public void ASimulationLineIsFormatTwoWhichAFormatOneReaderRefusesAsNewer()
    {
        var text = SetupLine.Write(Line(Compared()));

        // A reader of format 1 refuses any first byte above 1 as newer.
        Assert.Equal(SetupLine.SimulationVersion, Payload(text)[0]);
        Assert.Equal(LineProblem.NewerFormat, ProblemOf(WithVersion(text, 3)));
    }

    [Fact]
    public void EveryRefusalOfASetupLine()
    {
        var text = SetupLine.Write(Line(Compared()));

        Assert.Equal(LineProblem.Empty, ProblemOf("  "));
        Assert.Equal(LineProblem.NotALine, ProblemOf("hello"));
        Assert.Equal(LineProblem.NotALine, ProblemOf(text + "!"));
        Assert.Equal(LineProblem.TooLong, ProblemOf("CS2-" + new string('a', SetupLine.SimulationMaxLength)));
        Assert.Equal(LineProblem.Damaged, ProblemOf(text[..^3]));
        Assert.Equal(LineProblem.Damaged, ProblemOf(text[..20] + (text[20] == 'a' ? 'b' : 'a') + text[21..]));
        Assert.Equal(LineProblem.Malformed, ProblemOf(WithBody(text, body => body.Add(0))));
        Assert.Equal(LineProblem.Malformed, ProblemOf(WithBody(text, body => body[1] = (byte)LineKind.Setup)));
    }

    [Fact]
    public void AFormatOneLineOverAThousandCharactersIsTooLong()
    {
        var setup = new LineContent(SetupLine.Version, LineKind.Setup, 26316041, null, SideA);
        var text = WithBody(SetupLine.Write(setup), body => body.AddRange(new byte[800]));

        Assert.True(text.Length is > SetupLine.MaxLength and <= SetupLine.SimulationMaxLength);
        Assert.Equal(LineProblem.TooLong, ProblemOf(text));
    }

    [Fact]
    public void UnknownSettingsAndOutOfBoundsTrialsAreRefused()
    {
        var text = SetupLine.Write(Line(Compared()));
        // After the version, the kind and the four bytes of the build's varint: the settings byte.
        Assert.Equal(LineProblem.Malformed, ProblemOf(WithBody(text, body => body[6] |= 0x80)));
        Assert.Throws<ArgumentException>(() => SetupLine.Write(Line(Compared() with { Trials = 100_001 })));
    }

    // A line keeps a side's entries grouped by kind, each kind's in the order given.
    private static List<LineEntry> Grouped(IEnumerable<LineEntry> entries) =>
        [.. entries.OrderBy(e => e.Kind).ThenBy(e => e.Kind == ChangeKind.Star && !e.StarMask)];

    private static LineProblem ProblemOf(string text)
    {
        Assert.False(SetupLine.TryRead(text, out var content, out var problem));
        Assert.Null(content);
        return problem;
    }

    private static byte[] Payload(string text)
    {
        Assert.True(Base62.TryDecode(text.AsSpan(SetupLine.Prefix.Length), 4000, out var bytes));
        return bytes;
    }

    private static string WithVersion(string text, byte version) => WithBody(text, body => body[0] = version);

    // The line with its payload changed and its check value made right again, so only the change is wrong.
    private static string WithBody(string text, Action<List<byte>> change)
    {
        var payload = Payload(text);
        var body = payload[..^2].ToList();
        change(body);
        ushort crc = 0xFFFF;
        foreach (var b in body)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x1021 : crc << 1);
        }
        body.Add((byte)(crc >> 8));
        body.Add((byte)crc);
        return SetupLine.Prefix + Base62.Encode(body.ToArray());
    }
}
