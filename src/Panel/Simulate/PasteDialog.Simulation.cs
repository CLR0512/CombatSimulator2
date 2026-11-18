using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

// A pasted simulation: each side checked on this character as a setup line is, then the targets left out and the
// settings that change. Use sets the whole page and keeps the page before for one Undo; nothing runs.
internal sealed partial class PasteDialog
{
    private SimulationPage? _simulation;

    private void ReadSimulation(LineContent content)
    {
        _title.Text = Tr.Get("Import simulation");
        _kind = "simulation";
        _naming.Visible = false;
        if (
            LiveGame.Find() == null
            || !SimulationPage.TryRead(content, out var page, out var missingA, out var missingB, out var unknown)
        )
        {
            Refuse(Sharing.Problem(LineProblem.Malformed), "malformed");
            return;
        }
        _simulation = page!;
        var sides = page!.B == null ? Tr.Get("One side") : Tr.Get("Two sides");
        var left = Tr.Plural("{0} target left out", "{0} targets left out", page.Ignored.Count, page.Ignored.Count);
        _rows.AddChild(Ui.Label(Tr.Format("A simulation · {0} · {1}", sides, left), TextStyle.BodyStrong));
        if (content.GameBuild != LineIds.Build)
        {
            var note = Ui.Label(
                Tr.Format("Made on game build {0}: results may differ.", content.GameBuild.ToString(Tr.Culture)),
                TextStyle.Small
            );
            note.Modulate = Look.Risky;
            _rows.AddChild(note);
        }
        var checks = 0;
        var applies = 0;
        var not = missingA.Count + missingB.Count + unknown;
        void Side(string title, SideState side, List<Note> missing)
        {
            var label = Ui.Add(_rows, Ui.Label(title, TextStyle.Small));
            foreach (var note in missing)
                _rows.AddChild(Row(Tr.Note(note), Tr.Get("not in this build"), Look.Risky));
            if (side.Changes.IsEmpty)
                return;
            checks++;
            RunService.CheckSide(
                side.ToSetup(""),
                side.Changes,
                (check, error) =>
                {
                    if (_open != this)
                        return;
                    if (check == null)
                        _said.Text = error?.Message ?? Tr.Get("The check failed.");
                    else
                    {
                        var (a, refused) = Outcomes(check, label.GetIndex() + 1 + missing.Count);
                        applies += a;
                        not += refused;
                    }
                    if (--checks == 0)
                        Describe("checked", applies, not);
                }
            );
        }
        Side(Tr.Format("Side {0}", SideLine.A), page.A, missingA);
        if (page.B != null)
            Side(Tr.Format("Side {0}", SideLine.B), page.B, missingB);
        _rows.AddChild(Ui.Label(Tr.Get("Targets left out"), TextStyle.Small));
        _rows.AddChild(
            Row(
                page.Ignored.Count == 0 ? Tr.Get("None") : Targets(page.Ignored),
                unknown > 0 ? Tr.Plural("{0} not in this build", "{0} not in this build", unknown, unknown) : "",
                Look.Risky
            )
        );
        var differences = page.Differences(SimulationPage.Now());
        _rows.AddChild(Ui.Label(Tr.Get("Settings that change"), TextStyle.Small));
        if (differences.Count == 0)
            _rows.AddChild(Ui.Label(Tr.Get("None: as now"), TextStyle.Small));
        foreach (var (setting, from, to) in differences)
            _rows.AddChild(Row(Setting(setting), $"{from} → {to}", Colors.White));
        var actions = Ui.Add(_column, Ui.Row(8));
        actions.AddChild(Ui.Button(Tr.Get("Use this simulation"), Look.Action8, "Cs2PasteUseSim", UseSimulation));
        actions.AddChild(Ui.Spacer());
        actions.AddChild(Ui.Button(Tr.Get("Cancel"), Look.Button8, "Cs2PasteCancelSim", Close));
        Describe(checks == 0 ? "checked" : "checking", 0, not);
    }

    // The targets' names where this game knows them, else their keys.
    private static string Targets(IReadOnlySet<string> keys)
    {
        var names = PanelState.Catalog?.List.ToDictionary(t => t.Key, t => t.Name, StringComparer.Ordinal);
        return string.Join(
            ", ",
            keys.Order(StringComparer.Ordinal).Select(k => names?.GetValueOrDefault(k) ?? Ids.BriefKey(k))
        );
    }

    private static string Setting(string key) =>
        key switch
        {
            "trials" => Tr.Get("Trials per target"),
            "seed" => Tr.Get("Seed"),
            "fresh" => Tr.Get("Simulate fresh"),
            "stop early" => Tr.Get("Stop targets early"),
            "metric" => Tr.Get("Rank by"),
            _ => Tr.Get("Time unit"),
        };

    private void UseSimulation()
    {
        if (_simulation is { } page)
            SimulationPage.Use(page);
        Close();
        _used?.Invoke();
    }
}
