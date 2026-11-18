using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Compare;
using MelvorMods.CombatSimulator2.Panel.Results;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

internal sealed partial class SimulateView
{
    /// <summary>Shows the setup-error state as a real error would, for <c>cs2dev banner</c>.</summary>
    public void ShowSetupError(string reason) => _check.SetupError(Note.Text(reason));

    public Details Details => _details;

    public CompareDetails CompareDetails => _compareDetails;
}
