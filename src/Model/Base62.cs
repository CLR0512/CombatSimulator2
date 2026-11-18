using System.Numerics;
using System.Text;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// Bytes as digits and letters only (<c>0–9</c>, <c>A–Z</c>, <c>a–z</c>): text a chat program neither formats nor
/// breaks. The bytes are one big number, most significant first; each leading zero byte is one leading <c>0</c>.
/// </summary>
public static class Base62
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static bool IsDigit(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var zeros = 0;
        while (zeros < bytes.Length && bytes[zeros] == 0)
            zeros++;
        var number = new BigInteger(bytes[zeros..], isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (!number.IsZero)
        {
            number = BigInteger.DivRem(number, 62, out var digit);
            digits.Insert(0, Digits[(int)digit]);
        }
        return new string('0', zeros) + digits;
    }

    /// <summary>
    /// Reads <see cref="Encode"/> back; false for a character that isn't a digit or for more than
    /// <paramref name="maxBytes"/> bytes.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<char> text, int maxBytes, out byte[] bytes)
    {
        bytes = [];
        var zeros = 0;
        while (zeros < text.Length && text[zeros] == '0')
            zeros++;
        var number = BigInteger.Zero;
        foreach (var c in text[zeros..])
        {
            var digit = Digits.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
                return false;
            number = number * 62 + digit;
        }
        var value = number.IsZero ? [] : number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (zeros + value.Length > maxBytes)
            return false;
        bytes = new byte[zeros + value.Length];
        value.CopyTo(bytes, zeros);
        return true;
    }
}
