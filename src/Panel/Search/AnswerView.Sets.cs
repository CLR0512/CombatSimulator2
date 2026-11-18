using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

// What an answer over several targets adds: on how many the set helps, its gain on each, and the targets another set
// does better on alone; and for a stopped search, that it is the best so far.
internal sealed partial class AnswerView
{
    // Another tab of the answer: the same answer drawn again with its set.
    private void Tab(string tab)
    {
        _tab = tab;
        _stamp = null;
        if (_shown is { } answer)
            Show(answer, _changed);
    }

    private static void Stopped(VBoxContainer body, Answer answer)
    {
        if (answer.StoppedAtStage is not { } stage)
            return;
        // Translators: Above a gear search's answer when the search was stopped; {0} is the last stage it finished.
        var line = Ui.Label(
            Tr.Format("Stopped at stage {0} · best so far", stage),
            TextStyle.BodyStrong,
            "GearSearchStopped",
            wrap: true
        );
        line.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.BodyStrong);
        body.AddChild(line);
    }

    private static void Members(VBoxContainer body, Answer answer, SetResult best)
    {
        if (answer.Current is not { } current)
            return;
        var gains = SetScore.PerMember(current.Result, best.Result, best.Metric, PanelState.Prices());
        if (gains.Count == 0)
            return;
        var lower = Ranking.LowerIsBetter(best.Metric);
        var names = PanelState.Names();
        string Name(string key) =>
            Game.TargetSpec.FromKey(key) is var spec && spec.Enemy != null ? names(spec.Enemy)
            : Ids.IsMaster(key) ? Tr.Format("{0}'s tasks", names(Ids.MasterOf(key)))
            : names(key);
        var column = Ui.Add(body, Ui.Column(4, "GearSearchMembers"));
        column.AddChild(
            Ui.Label(
                best.Result.Kind == TargetKind.Master ? Tr.Get("On each monster") : Tr.Get("On each target"),
                TextStyle.Label
            )
        );
        // Under the heading and wrapping: the targets it is worse on can be a long list.
        var worse = SetScore.Worse(gains, lower);
        column.AddChild(
            Ui.Label(
                worse.Count == 0
                    ? Tr.Format("Helps {0} of {1}", SetScore.Helps(gains, lower), gains.Count)
                    : Tr.Format(
                        "Helps {0} of {1} · worse on {2}",
                        [SetScore.Helps(gains, lower), gains.Count, string.Join(", ", worse.Select(w => Name(w.Key)))]
                    ),
                TextStyle.Small,
                "GearSearchHelps",
                wrap: true
            )
        );
        foreach (
            var (gain, index) in gains.OrderByDescending(g => g.Gain?.Point ?? double.MinValue).Select((g, i) => (g, i))
        )
        {
            var row = Ui.Add(column, Ui.Row(8, Ui.Id("GearSearchMember", index.ToString(Tr.Culture))));
            var name = Ui.Add(row, Ui.Clipped(Name(gain.Key), TextStyle.Small));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var value = Ui.Add(row, Ui.Label(gain.Gain is { } e ? Words.Gain(e.Point) : "–", TextStyle.Small));
            // Only the gain is coloured, and only when clear of no change: better or worse beyond the range, which
            // stays plain beside it.
            if (gain.Gain is not { } clear)
                continue;
            if (clear.Low > 0 || clear.High < 0)
                value.LabelSettings = Compare.CompareText.Coloured(
                    (clear.Low > 0) != lower ? Look.Safe : Look.Deadly,
                    TextStyle.Small
                );
            // Translators: Beside a target's gain in a set search's answer: its 95% range, lowest to highest.
            row.AddChild(
                Ui.Label(Tr.Format("[{0} – {1}]", Words.Gain(clear.Low), Words.Gain(clear.High)), TextStyle.Small)
            );
        }
        if (answer.WantsOwnSet is { Count: > 0 } wants)
            column.AddChild(
                Ui.Label(
                    Tr.Format(
                        "{0}: another set does clearly better there alone. Search it on its own.",
                        string.Join(", ", wants.Select(Name))
                    ),
                    TextStyle.Small,
                    "GearSearchWantsOwnSet",
                    wrap: true
                )
            );
    }
}
