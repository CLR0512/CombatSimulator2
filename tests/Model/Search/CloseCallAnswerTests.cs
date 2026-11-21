using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

// A close call that changes a setup search's answer fills it from its own race: the setup as it is, the set the
// search held and a variant the search never fought, all fought longer than the search's last stage.
public sealed class CloseCallAnswerTests
{
    private static readonly ChangeList Held = ChangeList.Parse(
        "equip=Weapon:IronBattleaxe equip=Helmet:IronHelmet stance=Block"
    );

    private static readonly ChangeList Variant = ChangeList.Parse(
        "equip=Weapon:IronBattleaxe equip=Gloves:IronGloves stance=Block"
    );

    private static readonly Candidate[] RaceSets =
    [
        new(0, ChangeList.Empty, ""),
        new(1, Held, ""),
        new(2, Variant, ""),
    ];

    // The race as it settles when the variant beats the held set: the answer is filled from the race's results, the
    // variant as its best.
    private static (Answer Answer, Dictionary<int, StageResult> Results) RaceAnswer()
    {
        var goal = new Goal(Metrics.Kills);
        var results = new Dictionary<int, StageResult>
        {
            [0] = Set(0, 12, trials: 1200),
            [1] = Set(1, 10.1, trials: 1200),
            [2] = Set(2, 10, trials: 1200),
        };
        var answer = new Answer
        {
            Kind = SearchKind.BestGear,
            Key = new AnswerKey { Goal = goal },
            Target = Target,
        };
        Answers.Fill(answer, RaceSets, [results[0], results[2]], results, goal, 0.01, false, Catalogue());
        return (answer, results);
    }

    [Fact]
    public void ARaceAnswerCompletesAgainstTheRacesOwnSets()
    {
        var (answer, results) = RaceAnswer();
        SetupAnswers.FillChoices(answer, [], [], RaceSets, results, Catalogue());
        Assert.Equal(AnswerState.Found, answer.State);
        Assert.Equal(Variant, answer.Best!.Changes);
        Assert.Empty(answer.Ties);
    }
}
