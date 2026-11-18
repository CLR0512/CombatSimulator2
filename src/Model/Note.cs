using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// Player-facing text that is kept (in a run, or sent from the simulator host) and shown later: the English
/// message, an optional plural form and count, and the values for its <c>{0}</c>… placeholders. The panel shows it
/// in the language current when it is read; <see cref="English"/> gives the text as CS2 writes it in English, with
/// invariant numbers, for replies, logs and the results hash. A verbatim note is text from elsewhere (the game's
/// own, already in the run's language, or a run saved before notes) shown as it is.
/// </summary>
[JsonConverter(typeof(NoteJson))]
public sealed class Note
{
    private static readonly object[] NoArgs = [];

    private Note(string msg, string? plural, long? count, object[] args, string? ctx, bool verbatim)
    {
        Msg = msg;
        PluralMsg = plural;
        Count = count;
        Args = args;
        Ctx = ctx;
        Verbatim = verbatim;
    }

    /// <summary>The English message (the singular form when there is a plural).</summary>
    public string Msg { get; }

    /// <summary>The English plural form; set with <see cref="Count"/>.</summary>
    public string? PluralMsg { get; }

    public long? Count { get; }

    /// <summary>
    /// The values for the placeholders: strings, numbers, booleans or notes (shown in the same language).
    /// </summary>
    public IReadOnlyList<object> Args { get; }

    /// <summary>The message's context, telling apart two meanings of the same English text.</summary>
    public string? Ctx { get; }

    /// <summary>Whether the note is shown as it is, never looked up.</summary>
    public bool Verbatim { get; }

    /// <summary>A message with its placeholder values.</summary>
    public static Note Of(string message, params object[] args) => new(message, null, null, Checked(args), null, false);

    /// <summary>A message with a singular and plural form, chosen by <paramref name="count"/>.</summary>
    public static Note Plural(string singular, string plural, long count, params object[] args) =>
        new(singular, plural, count, Checked(args), null, false);

    /// <summary>Text shown as it is: the game's own, or a run saved before notes.</summary>
    public static Note Text(string text) => new(text, null, null, NoArgs, null, true);

    /// <summary>The English text: the plural form by count, placeholders filled with invariant numbers.</summary>
    public string English()
    {
        if (Verbatim)
            return Msg;
        var form = PluralMsg != null && Count != 1 ? PluralMsg : Msg;
        if (Args.Count == 0)
            return form;
        object[] values = [.. Args.Select(a => a is Note n ? n.English() : a)];
        return Possessive.Mend(string.Format(CultureInfo.InvariantCulture, form, values), values);
    }

    public override string ToString() => English();

    private static object[] Checked(object[] args)
    {
        foreach (var arg in args)
        {
            if (arg is not (string or bool or int or long or double or float or decimal or ulong or uint or Note))
                throw new ArgumentException(
                    $"a note's values are strings, numbers, booleans or notes, not {arg?.GetType()}"
                );
        }
        return args.Length == 0 ? NoArgs : args;
    }

    /// <summary>
    /// Reads and writes a note as <c>{"msg": …, "plural": …, "count": …, "args": […], "ctx": …, "verbatim": true}</c>
    /// with absent fields left out. A plain string reads as a verbatim note: runs saved before notes held their text
    /// as strings.
    /// </summary>
    internal sealed class NoteJson : JsonConverter<Note>
    {
        public override Note Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return Text(reader.GetString() ?? "");
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("a note is an object or a string");
            string msg = "";
            string? plural = null;
            string? ctx = null;
            long? count = null;
            var verbatim = false;
            var args = new List<object>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "msg":
                        msg = reader.GetString() ?? "";
                        break;
                    case "plural":
                        plural = reader.GetString();
                        break;
                    case "count":
                        count = reader.GetInt64();
                        break;
                    case "ctx":
                        ctx = reader.GetString();
                        break;
                    case "verbatim":
                        verbatim = reader.GetBoolean();
                        break;
                    case "args":
                        if (reader.TokenType != JsonTokenType.StartArray)
                            throw new JsonException("a note's args are an array");
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            args.Add(
                                reader.TokenType == JsonTokenType.StartObject
                                    ? Read(ref reader, typeToConvert, options)
                                    : Value(ref reader)
                            );
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            if (plural != null && count == null)
                throw new JsonException("a note with a plural needs a count");
            return new Note(msg, plural, count, args.Count == 0 ? NoArgs : [.. args], ctx, verbatim);
        }

        private static object Value(ref Utf8JsonReader reader) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? "",
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number => reader.TryGetInt64(out var whole) ? whole : reader.GetDouble(),
                _ => throw new JsonException("a note's values are strings, numbers, booleans or notes"),
            };

        public override void Write(Utf8JsonWriter writer, Note value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("msg", value.Msg);
            if (value.PluralMsg != null)
                writer.WriteString("plural", value.PluralMsg);
            if (value.Count is { } count)
                writer.WriteNumber("count", count);
            if (value.Args.Count > 0)
            {
                writer.WriteStartArray("args");
                foreach (var arg in value.Args)
                {
                    switch (arg)
                    {
                        case string s:
                            writer.WriteStringValue(s);
                            break;
                        case Note n:
                            Write(writer, n, options);
                            break;
                        case bool b:
                            writer.WriteBooleanValue(b);
                            break;
                        case double d:
                            writer.WriteNumberValue(d);
                            break;
                        case float f:
                            writer.WriteNumberValue(f);
                            break;
                        case decimal m:
                            writer.WriteNumberValue(m);
                            break;
                        case ulong u:
                            writer.WriteNumberValue(u);
                            break;
                        default:
                            writer.WriteNumberValue(Convert.ToInt64(arg, CultureInfo.InvariantCulture));
                            break;
                    }
                }
                writer.WriteEndArray();
            }
            if (value.Ctx != null)
                writer.WriteString("ctx", value.Ctx);
            if (value.Verbatim)
                writer.WriteBoolean("verbatim", true);
            writer.WriteEndObject();
        }
    }
}

/// <summary>Notes as English text, for replies, logs and the results hash.</summary>
public static class Notes
{
    public static string English(this IEnumerable<Note> notes, string separator) =>
        string.Join(separator, notes.Select(n => n.English()));
}
