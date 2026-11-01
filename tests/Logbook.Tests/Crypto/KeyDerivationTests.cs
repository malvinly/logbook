using System.Text;
using Logbook.Core.Crypto;
using Xunit;

namespace Logbook.Tests.Crypto;

public sealed class KeyDerivationTests
{
    // Low so the suite stays fast; the algorithm does not change with the count.
    private const int FastIterations = 1_000;

    private static readonly byte[] Salt = KeyDerivation.NewSalt();

    [Fact]
    public void MatchesPublishedPbkdf2HmacSha256Vector()
    {
        // Widely published vector: password "password", salt "salt", 1 iteration, 32 bytes.
        byte[] key = KeyDerivation.Derive("password", Encoding.UTF8.GetBytes("salt"), 1);
        Assert.Equal("120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b", Convert.ToHexStringLower(key));
    }

    [Fact]
    public void DerivesThirtyTwoBytes()
    {
        Assert.Equal(LineCipher.KeySize, KeyDerivation.Derive("correct horse", Salt, FastIterations).Length);
    }

    [Fact]
    public void SameInputsGiveSameKey()
    {
        byte[] first = KeyDerivation.Derive("correct horse", Salt, FastIterations);
        byte[] second = KeyDerivation.Derive("correct horse", Salt, FastIterations);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentPassphraseGivesDifferentKey()
    {
        Assert.NotEqual(
            KeyDerivation.Derive("correct horse", Salt, FastIterations),
            KeyDerivation.Derive("correct horsf", Salt, FastIterations));
    }

    [Fact]
    public void DifferentSaltGivesDifferentKey()
    {
        Assert.NotEqual(
            KeyDerivation.Derive("correct horse", Salt, FastIterations),
            KeyDerivation.Derive("correct horse", KeyDerivation.NewSalt(), FastIterations));
    }

    [Fact]
    public void DifferentIterationCountGivesDifferentKey()
    {
        Assert.NotEqual(
            KeyDerivation.Derive("correct horse", Salt, FastIterations),
            KeyDerivation.Derive("correct horse", Salt, FastIterations + 1));
    }

    [Fact]
    public void NewSaltIsSixteenRandomBytes()
    {
        byte[] first = KeyDerivation.NewSalt();
        byte[] second = KeyDerivation.NewSalt();
        Assert.Equal(16, first.Length);
        Assert.Equal(KeyDerivation.SaltSize, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DefaultIterationCountIsTheOneInTheDesign()
    {
        Assert.Equal(600_000, KeyDerivation.DefaultIterations);
    }
}
