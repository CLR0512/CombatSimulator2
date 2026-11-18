using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A count past what a long holds, as its decimal digits: a search's sets can be trillions of trillions.</summary>
public sealed class BigIntegerJson : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        BigInteger.Parse(reader.GetString() ?? "0", CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
