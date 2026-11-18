using Godot;
using HarmonyLib;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The game's plural texts, such as a combat effect's "for 3 turns" that a setup's pages describe on CS2's check
/// threads, come from Godot's translation server. Its plural rules keep one cache and one expression per language with
/// no lock, and even a cached answer reorders the cache: two threads asking at once can corrupt it, which gives a wrong
/// form at best and stops the whole process at worst. So every call, the game's own on the main thread included, goes
/// one at a time; each is a lookup of a few microseconds.
/// </summary>
[HarmonyPatch(typeof(TranslationServer), nameof(TranslationServer.TranslatePlural))]
internal static class SerializePlurals
{
    private static readonly Lock _lock = new();

    private static void Prefix(out bool __state)
    {
        _lock.Enter();
        __state = true;
    }

    // Only a lock this thread holds: a finalizer left behind by an earlier build of the mod is handed this build's
    // state too, as Harmony shares it by the patch's name.
    private static void Finalizer(bool __state)
    {
        if (__state && _lock.IsHeldByCurrentThread)
            _lock.Exit();
    }
}
