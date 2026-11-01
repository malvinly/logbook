using Logbook.Core;
using Xunit;

namespace Logbook.Tests;

public sealed class UlidTests
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void IsTwentySixCharacters()
    {
        Assert.Equal(26, Ulid.Generate().Length);
    }

    [Fact]
    public void UsesOnlyCrockfordAlphabet()
    {
        for (int i = 0; i < 200; i++)
        {
            string id = Ulid.Generate();
            Assert.All(id, c => Assert.Contains(c, CrockfordAlphabet));
        }
    }

    [Theory]
    [InlineData(0, "0000000000")]
    [InlineData(1, "0000000001")]
    [InlineData(31, "000000000Z")]
    [InlineData(32, "0000000010")]
    public void EncodesTimestampInFirstTenCharacters(long unixMilliseconds, string expectedPrefix)
    {
        string id = Ulid.Generate(DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds));
        Assert.StartsWith(expectedPrefix, id, StringComparison.Ordinal);
    }

    [Fact]
    public void SortsByTimestampWithOrdinalComparison()
    {
        DateTimeOffset start = new(2026, 10, 6, 10, 14, 3, TimeSpan.FromHours(-5));
        List<string> ids = [];
        for (int i = 0; i < 100; i++)
        {
            ids.Add(Ulid.Generate(start.AddMilliseconds(i * 7)));
        }

        List<string> sorted = [.. ids.Order(StringComparer.Ordinal)];
        Assert.Equal(ids, sorted);
    }

    [Fact]
    public void SameTimestampSharesPrefixButDiffersInRandomPart()
    {
        DateTimeOffset at = new(2026, 10, 6, 10, 14, 3, TimeSpan.Zero);
        string a = Ulid.Generate(at);
        string b = Ulid.Generate(at);
        Assert.Equal(a[..10], b[..10]);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GeneratedIdsAreUnique()
    {
        HashSet<string> seen = [];
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(seen.Add(Ulid.Generate()));
        }
    }

    [Fact]
    public void RejectsTimestampBeforeUnixEpoch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ulid.Generate(DateTimeOffset.FromUnixTimeMilliseconds(-1)));
    }
}
