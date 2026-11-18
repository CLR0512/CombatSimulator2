using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class IdHashTests
{
    // Lines already handed out hold these numbers: the hash of an id may never change.
    [Theory]
    [InlineData("MelvorBase:Attack", 2, 0x4B36u)]
    [InlineData("MelvorBase:Attack", 3, 0x5F0321u)]
    [InlineData("MelvorBase:Weapon", 2, 0xE1EFu)]
    [InlineData("MelvorBase:Iron_Sword", 3, 0xF54380u)]
    public void AnIdsHashIsPinned(string id, int bytes, uint expected) => Assert.Equal(expected, IdHash.Of(id, bytes));

    [Fact]
    public void AHashFitsItsBytesAndIsNeverZero()
    {
        var random = new Random(7);
        for (var i = 0; i < 20_000; i++)
        {
            var id = "MelvorBase:Item" + random.Next().ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(IdHash.Of(id, 2), 1u, 0xFFFFu);
            Assert.InRange(IdHash.Of(id, 3), 1u, 0xFFFFFFu);
        }
    }

    [Fact]
    public void OnlyTwoOrThreeBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IdHash.Of("a", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IdHash.Of("a", 4));
    }

    [Fact]
    public void Base62RoundTripsWithLeadingZeros()
    {
        var random = new Random(11);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(0, 80)];
            random.NextBytes(bytes);
            if (bytes.Length > 2 && i % 3 == 0)
                bytes[0] = bytes[1] = 0;
            var text = Base62.Encode(bytes);
            Assert.All(text, c => Assert.True(Base62.IsDigit(c)));
            Assert.True(Base62.TryDecode(text, 80, out var back));
            Assert.Equal(bytes, back);
        }
        Assert.False(Base62.TryDecode("ab-c", 80, out _));
        Assert.False(
            Base62.TryDecode(
                Base62.Encode(
                    new byte[81]
                        .Select(_ => (byte)9)
                        .ToArray()
                ),
                80,
                out _
            )
        );
    }
}
