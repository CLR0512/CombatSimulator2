using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PlanReplyTests
{
    // The host sends a plan as the run files' JSON: what comes back must plan and climb as what went.
    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, RunFile.Options), RunFile.Options)!;

    [Fact]
    public void APlanAndItsCatalogueComeBackAsTheyWent()
    {
        var catalogue = Catalogue();
        var request = SearchRequestTests.Request() with
        {
            Locks = new HashSet<string> { Id("Weapon"), Id("Ammo") },
        };
        var plan = Planner.Plan(catalogue, request);
        var back = RoundTrip(new PlanReply(catalogue) { Plan = plan });
        Assert.Equal(catalogue.Hash, back.Catalogue.Hash);
        Assert.Equal(catalogue.Items.Count, back.Catalogue.Items.Count);
        Assert.Equal(plan.Possible, back.Plan!.Possible);
        Assert.Equal(plan.RuledOut, back.Plan.RuledOut);
        Assert.Equal(plan.Climb, back.Plan.Climb);
        Assert.Equal(plan.Candidates.Select(c => c.Changes), back.Plan.Candidates.Select(c => c.Changes));
        Assert.Equal(plan.Branches.Select(b => b.Count), back.Plan.Branches.Select(b => b.Count));
        // What is planned from the catalogue that came back is the same plan.
        Assert.Equal(plan.Possible, Planner.Plan(back.Catalogue, request).Possible);
    }

    [Fact]
    public void DroptimizersPlanAndAProblemComeBack()
    {
        var catalogue = Catalogue();
        var request = SearchRequestTests.Request() with { Kind = SearchKind.Droptimizer };
        var drops = Droptimizer.Plan(catalogue, request);
        var back = RoundTrip(new PlanReply(catalogue) { Drops = drops, Problem = Note.Of("No slot {0}", "Ring") });
        Assert.Equal(drops.Candidates.Count, back.Drops!.Candidates.Count);
        Assert.Equal(drops.Items.Keys, back.Drops.Items.Keys);
        // Each candidate's item and slot come back too: the answer reads them.
        Assert.All(drops.Items, pair => Assert.Equal(pair.Value, back.Drops.Items[pair.Key]));
        Assert.Equal("No slot Ring", back.Problem!.English());
    }
}
