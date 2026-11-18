using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// Whether the chosen setup can be simulated: every target checked on a check copy for it, and the setup-error
/// banner when it can't.
/// </summary>
internal sealed class SetupCheck(Control root)
{
    private readonly Control _root = root;
    private string? _checkingFor;
    private string? _setupError;

    /// <summary>The setup error shown, in English; null when there is none.</summary>
    public string? Error => _setupError;

    /// <summary>Clears the setup's error after a change, with the banner if it shows one.</summary>
    public void ClearError()
    {
        _setupError = null;
        if (PanelState.Banner is BannerState.SetupError)
            PanelState.SetBanner(null);
    }

    /// <summary>The setup chosen in the setup bar.</summary>
    public static Setup Current() => PanelState.SetupA();

    // Checks every target for the chosen setup on a check copy, unless the latest catalog already is for it; a check
    // that fails says why through failed.
    public void Check(Action<Note> failed)
    {
        var setup = Current();
        setup.IgnoreRequirements = false; // the list's reasons don't depend on ignoring them
        if (LiveGame.Find() is not { } game)
        {
            failed(LiveGame.NoGame);
            return;
        }
        setup.Character = LiveGame.CharacterName(game);
        // A kill or an unlock since the list was built can show a monster or place it masked.
        if (SpoilerGuard.Refresh(game))
            PanelState.Catalog = null;
        if (PanelState.Catalog is { } cached && cached.Setup == setup.Identity)
            return;
        if (_checkingFor == setup.Identity)
            return;
        _checkingFor = setup.Identity;
        var slot = setup.Loadout == Setup.Current ? null : setup.Loadout;
        TargetCatalog.Check(
            game,
            setup,
            slot,
            (catalog, error) =>
            {
                if (_checkingFor != setup.Identity || !GodotObject.IsInstanceValid(_root))
                    return;
                _checkingFor = null;
                if (catalog == null)
                {
                    var reason = error?.Message is { } message ? Note.Text(message) : Note.Of("unknown error");
                    failed(reason);
                    // The error is English text from the code; it goes into the note as it is.
                    SetupError(CantSimulate(setup, reason.English()));
                    return;
                }
                PanelState.Catalog = catalog;
                var known = catalog.List.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
                var areas = catalog.List.Select(t => t.Spec.Area).ToHashSet(StringComparer.Ordinal);
                if (PanelState.Prefs.DropUnknown(known, areas))
                    PanelState.Save();
                if (_setupError != null)
                    ClearError();
                // Every view counts targets from the catalogue: the Setup tab's run control as well as Simulate.
                PanelState.Notify();
            }
        );
    }

    private static Note CantSimulate(Setup setup, string reason) =>
        setup.Loadout == Setup.Current
            ? Note.Of("The character as is can't be simulated: {0}", reason)
            : Note.Of("Loadout {0} can't be simulated: {1}", Ids.Brief(setup.Loadout), reason);

    /// <summary>Shows the setup-error banner for <paramref name="reason"/>.</summary>
    public void SetupError(Note reason)
    {
        _setupError = reason.English();
        PanelState.SetBanner(new BannerState.SetupError(reason));
    }
}
