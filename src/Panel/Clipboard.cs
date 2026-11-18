using Godot;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>The system clipboard, for a setup line copied out or pasted in. Main thread.</summary>
internal static class Clipboard
{
    public static void Set(string text) => DisplayServer.ClipboardSet(text);

    public static string Get() => DisplayServer.ClipboardGet();
}
