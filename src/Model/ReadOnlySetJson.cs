using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Reads a read-only set back as a hash set, strings compared ordinally as the catalogue builds them.</summary>
public sealed class ReadOnlySetJson : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlySet<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return (JsonConverter)
            Activator.CreateInstance(typeof(SetConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }

    private sealed class SetConverter<T> : JsonConverter<IReadOnlySet<T>>
    {
        public override IReadOnlySet<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            var items = JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];
            return typeof(T) == typeof(string)
                ? (IReadOnlySet<T>)(object)new HashSet<string>(items.Cast<string>(), StringComparer.Ordinal)
                : new HashSet<T>(items);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToList(), options);
    }
}
