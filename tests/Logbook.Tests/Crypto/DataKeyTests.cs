using System.Security.Cryptography;
using Logbook.Core.Crypto;
using Xunit;

namespace Logbook.Tests.Crypto;

public sealed class DataKeyTests
{
    private const int FastIterations = 1_000;

    [Fact]
    public void GeneratesThirtyTwoRandomBytes()
    {
        byte[] first = DataKey.Generate();
        byte[] second = DataKey.Generate();
        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void WrapAndUnwrapRoundTrip()
    {
        byte[] dataKey = DataKey.Generate();
        byte[] kek = KeyDerivation.Derive("correct horse", KeyDerivation.NewSalt(), FastIterations);
        string wrapped = DataKey.Wrap(dataKey, kek);
        Assert.Equal(dataKey, DataKey.Unwrap(wrapped, kek));
    }

    [Fact]
    public void WrappedKeyIsSixtyBytesOfBase64()
    {
        byte[] kek = KeyDerivation.Derive("correct horse", KeyDerivation.NewSalt(), FastIterations);
        string wrapped = DataKey.Wrap(DataKey.Generate(), kek);
        Assert.Equal(12 + 32 + 16, Convert.FromBase64String(wrapped).Length);
    }

    [Fact]
    public void WrongPassphraseFailsToUnwrap()
    {
        byte[] salt = KeyDerivation.NewSalt();
        byte[] dataKey = DataKey.Generate();
        string wrapped = DataKey.Wrap(dataKey, KeyDerivation.Derive("correct horse", salt, FastIterations));

        byte[] wrongKek = KeyDerivation.Derive("correct horsf", salt, FastIterations);
        Assert.ThrowsAny<CryptographicException>(() => DataKey.Unwrap(wrapped, wrongKek));
    }

    [Fact]
    public void WrappedKeyOfWrongLengthIsRejected()
    {
        byte[] kek = KeyDerivation.Derive("correct horse", KeyDerivation.NewSalt(), FastIterations);
        string notAKey = LineCipher.Encrypt(kek, new byte[16]);
        Assert.ThrowsAny<CryptographicException>(() => DataKey.Unwrap(notAKey, kek));
    }
}
