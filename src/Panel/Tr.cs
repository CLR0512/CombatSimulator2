using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// CS2's text in the game's language: every player-facing message the panel shows goes through here, as a string
/// literal (so <c>mm strings</c> finds it), with values in <c>{0}</c>… placeholders. Backed by the mod's
/// <see cref="ModText"/>; without it (before load) messages come back in English. Main thread.
/// </summary>
internal static class Tr
{
    private static ModText? _text;

    /// <summary>A counter raised by each language change; caches keyed by text include it.</summary>
    public static int Version { get; private set; }

    /// <summary>The game's culture, for numbers the panel formats itself.</summary>
    public static CultureInfo Culture => _text?.Culture ?? CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Takes the mod's text when it loads (null on unload).</summary>
    internal static void Use(ModText? text)
    {
        _text = text;
        Version++;
    }

    /// <summary>Notes a language change, so everything shown is looked up again.</summary>
    public static void Changed() => Version++;

    /// <summary>The language CS2's text is shown in now: its own choice, else the game's.</summary>
    public static string Locale => _text?.Locale ?? "en";

    /// <summary>The languages CS2 ships a translation for, each with the share of its text translated.</summary>
    public static IReadOnlyList<(string Locale, double Share)> Languages =>
        _text is { } text ? [.. text.Languages.Select(l => (l.Locale, l.Share))] : [];

    public static string Get(string message, string? context = null) => _text?.Get(message, context) ?? message;

    /// <summary>
    /// A message with its values. Two or more values go in one array (<c>[a, b]</c>), as Core's <c>Text.Format</c>
    /// takes them: <c>mm strings</c> reads a third argument as the message's context.
    /// </summary>
    public static string Format(string message, params object?[] args) =>
        Possessive.Mend(
            _text?.Format(message, args) ?? string.Format(CultureInfo.InvariantCulture, message, args),
            args
        );

    public static string Plural(string singular, string plural, long count, params object?[] args) =>
        Possessive.Mend(
            _text?.Plural(singular, plural, count, args)
                ?? string.Format(CultureInfo.InvariantCulture, count == 1 ? singular : plural, args),
            args
        );

    /// <summary>Marks a message that something else translates later (Core's labels); returns it unchanged.</summary>
    public static string Key(string message) => message;

    /// <summary>Kept text in the game's language: looked up unless it is verbatim (the game's own text).</summary>
    public static string Note(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var spoilers = Spoilers.Current;
        if (note.Verbatim)
            return spoilers.Name(note.Msg);
        object?[] args = [.. note.Args.Select(a => a is Model.Note n ? Note(n) : spoilers.Value(a))];
        return note.PluralMsg is { } plural ? Plural(note.Msg, plural, note.Count ?? 0, args)
            : args.Length == 0 ? Get(note.Msg, note.Ctx)
            : Format(note.Msg, args);
    }

    /// <summary>Several notes in the game's language, joined.</summary>
    public static string Notes(IEnumerable<Note> notes, string separator) => string.Join(separator, notes.Select(Note));

    /// <summary>Puts a window root in CS2's translation domain, with Godot's own translation off below it.</summary>
    public static T Adopt<T>(T root)
        where T : Node => _text?.Adopt(root) ?? root;
}
