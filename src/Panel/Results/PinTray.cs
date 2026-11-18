using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>The pinned targets in the bar under the results, with the button that compares them side by side.</summary>
internal sealed class PinTray
{
    private const float Portrait = 28;

    private readonly HBoxContainer _tray;
    private readonly Label _trayText;
    private readonly HBoxContainer _trayPortraits;
    private readonly Label _waiting;
    private readonly Button _clear;
    private readonly Button _compare;

    public PinTray()
    {
        // Each part as wide with one pin as with three, so nothing in it moves as pins come and go.
        var tray = _tray = Ui.Row(10, "Cs2PinnedTray");
        tray.AddChild(Ui.Picture(Art.File(Art.PinPath), 22));
        _trayText = Ui.Label("", TextStyle.BodyStrong);
        tray.AddChild(_trayText);
        // Room for every pin's portrait; the pins not in this run are counted in the room their portraits leave.
        var slots = Ui.Add(tray, Ui.Row(6));
        slots.CustomMinimumSize = new Vector2(Preferences.MaxPins * Portrait + (Preferences.MaxPins - 1) * 6, Portrait);
        _trayPortraits = Ui.Add(slots, Ui.Row(6));
        _waiting = Ui.Add(slots, Ui.Label("", TextStyle.Label, "Cs2PinsWaiting"));
        _waiting.MouseFilter = Control.MouseFilterEnum.Stop;
        _waiting.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _clear = Ui.Add(tray, Ui.Button(Tr.Get("Clear", "pins"), Look.Button4, "Cs2PinsClear", PanelPins.ClearPins));
        _compare = Ui.Button(
            Tr.Get("Side by side ▸"),
            // Not green: the green beside it is the run button's. Small, as Return is in the same bar.
            Look.Button4,
            "Cs2PinSideBySide",
            () => PanelPins.SetCompare(true)
        );
        _clear.SizeFlagsVertical = _compare.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        tray.AddChild(_compare);
    }

    /// <summary>The pins' part of the bar under the results.</summary>
    public Control Root => _tray;

    /// <summary>The pins as portraits, shown under a single run's results.</summary>
    public void Refresh(Run? run)
    {
        var game = LiveGame.Find();
        var pins = PanelPins.ShownPins;
        var waiting = PanelPins.WaitingPins;
        // While any pin waits the tray stays, so the waiting ones can be seen and cleared.
        _tray.Visible = run != null && pins.Count + waiting.Count > 0;
        _waiting.Text = waiting.Count > 0 ? Tr.Format("{0} waiting", waiting.Count) : "";
        _waiting.Visible = waiting.Count > 0;
        _waiting.TooltipText =
            waiting.Count > 0
                ? Tr.Format(
                    "Not in this run, shown again in one that has them: {0}",
                    string.Join(", ", waiting.Select(k => PanelState.Catalog?.Find(k)?.Name ?? Ids.BriefKey(k)))
                )
                : "";
        _trayText.Text = Tr.Format("{0} of {1} pinned", [pins.Count, Preferences.MaxPins]);
        Ui.HoldWidth(_trayText, Tr.Format("{0} of {1} pinned", [Preferences.MaxPins, Preferences.MaxPins]));
        Ui.Clear(_trayPortraits);
        foreach (var key in pins)
        {
            var picture = Ui.Picture(game != null ? Art.Target(game, TargetSpec.FromKey(key)) : Art.Missing, Portrait);
            picture.TooltipText = PanelState.Catalog?.Find(key)?.Name ?? Ids.BriefKey(key);
            picture.MouseFilter = Control.MouseFilterEnum.Pass;
            _trayPortraits.AddChild(picture);
        }
        _compare.Disabled = pins.Count < 2;
        _compare.TooltipText =
            pins.Count >= 2 ? ""
            : waiting.Count > 0 ? Tr.Get("Side by side needs 2 pins in the shown run")
            : Tr.Get("Pin at least 2 targets to compare");
    }
}
