namespace MelvorMods.CombatSimulator2.Model;

// The simulation kind: the Simulate page as one line, in format version 2. Setup and preset lines stay version 1,
// so a CS2 that reads only those refuses this kind as newer and reads the others as before.
public static partial class SetupLine
{
    public const int SimulationVersion = 2;
    public const int SimulationMaxLength = 2000;

    private const int SimulationMaxPayload = 1500;
    private const int TargetIdBytes = 3;

    // The settings byte.
    private const int IgnoreA = 0x01;
    private const int IgnoreB = 0x02;
    private const int Compared = 0x04;
    private const int SeedSet = 0x08;
    private const int FreshRuns = 0x10;
    private const int Stops = 0x20;
    private const int LimitsSet = 0x40;

    // Precision in ten-thousandths: ±5% is 500.
    private const double PrecisionScale = 10_000;

    private static bool TryWriteSimulation(LineContent content, out string text)
    {
        var sim =
            content.Simulation
            ?? throw new ArgumentException("a simulation line needs its simulation", nameof(content));
        if (sim.Trials is < 1 or > LineSimulation.MostTrials || sim.Ignored.Count > LineSimulation.MostIgnored)
            throw new ArgumentException("the simulation's settings are out of a line's bounds", nameof(content));
        var bytes = new List<byte> { SimulationVersion, (byte)LineKind.Simulation };
        Varint(bytes, content.GameBuild);
        var flags =
            (sim.IgnoreRequirementsA ? IgnoreA : 0)
            | (sim.IgnoreRequirementsB ? IgnoreB : 0)
            | (sim.B != null ? Compared : 0)
            | (sim.Seed != null ? SeedSet : 0)
            | (sim.Fresh ? FreshRuns : 0)
            | (sim.StopEarly ? Stops : 0)
            | (sim.Limits != null ? LimitsSet : 0);
        bytes.Add((byte)flags);
        WriteGroups(bytes, sim.A);
        if (sim.B != null)
            WriteGroups(bytes, sim.B);
        Varint(bytes, (ulong)sim.Trials);
        if (sim.Seed is { } seed)
            Varint(bytes, seed);
        Varint(bytes, (ulong)Math.Clamp(Math.Round(sim.StopPrecision * PrecisionScale), 0, PrecisionScale));
        if (sim.Limits is { } limits)
        {
            Varint(bytes, (ulong)Math.Clamp(Math.Round(limits.EnemySeconds), 1, LineSimulation.LongestLimit));
            Varint(bytes, (ulong)Math.Clamp(Math.Round(limits.DungeonSeconds), 1, LineSimulation.LongestLimit));
        }
        WriteId(bytes, sim.Metric, TargetIdBytes, full: true);
        bytes.Add((byte)sim.TimeUnit);
        var fullIgnored = sim.Ignored.Any(i => i.Full != null);
        bytes.Add((byte)(fullIgnored ? 1 : 0));
        Varint(bytes, (ulong)sim.Ignored.Count);
        foreach (var target in sim.Ignored)
            WriteId(bytes, target, TargetIdBytes, fullIgnored);
        text = Finish(bytes);
        return text.Length <= SimulationMaxLength;
    }

    // After the version byte; null for anything out of bounds, unknown or left over.
    private static LineContent? SimulationContent(ref Reader reader)
    {
        reader.Byte(); // the version, checked by the caller
        if (reader.Byte() != (int)LineKind.Simulation)
            return null;
        var build = reader.Varint();
        var flags = reader.Byte();
        if (!reader.Ok || (flags & ~(IgnoreA | IgnoreB | Compared | SeedSet | FreshRuns | Stops | LimitsSet)) != 0)
            return null;
        var a = Groups(ref reader);
        var b = (flags & Compared) != 0 ? Groups(ref reader) : null;
        if (a == null || ((flags & Compared) != 0 && b == null))
            return null;
        var trials = reader.Varint();
        ulong? seed = (flags & SeedSet) != 0 ? reader.Varint() : null;
        var precision = reader.Varint();
        TrialLimits? limits = null;
        if ((flags & LimitsSet) != 0)
        {
            var enemy = reader.Varint();
            var dungeon = reader.Varint();
            if (enemy is < 1 or > LineSimulation.LongestLimit || dungeon is < 1 or > LineSimulation.LongestLimit)
                return null;
            limits = new TrialLimits { EnemySeconds = enemy, DungeonSeconds = dungeon };
        }
        var metric = reader.Id(TargetIdBytes, full: true);
        var unit = reader.Byte();
        var fullIgnored = reader.Byte();
        var count = reader.Varint();
        if (
            !reader.Ok
            || trials is < 1 or > LineSimulation.MostTrials
            || precision > PrecisionScale
            || metric is null or { IsNone: true }
            || unit > (int)TimeUnit.Day
            || fullIgnored > 1
            || count > LineSimulation.MostIgnored
        )
            return null;
        var ignored = new List<LineId>();
        var seen = new HashSet<(uint, string?)>();
        for (var i = 0UL; i < count; i++)
        {
            if (
                reader.Id(TargetIdBytes, fullIgnored == 1) is not { IsNone: false } target
                || !seen.Add((target.Hash, target.Full))
            )
                return null;
            ignored.Add(target);
        }
        if (!reader.AtEnd)
            return null;
        var sim = new LineSimulation(
            a,
            b,
            (flags & IgnoreA) != 0,
            (flags & IgnoreB) != 0,
            ignored,
            (int)trials,
            seed,
            (flags & FreshRuns) != 0,
            (flags & Stops) != 0,
            precision / PrecisionScale,
            limits,
            metric,
            (TimeUnit)unit
        );
        return new LineContent(SimulationVersion, LineKind.Simulation, build, null, a) { Simulation = sim };
    }
}
