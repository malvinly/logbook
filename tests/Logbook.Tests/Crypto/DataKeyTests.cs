using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void FullHierarchyRoundTripsARecord()
    {
        // Passphrase -> KEK -> data key -> record line, the way the file store will use it.
        byte[] salt = KeyDerivation.NewSalt();
        byte[] dataKey = DataKey.Generate();
        string wrappedKey = DataKey.Wrap(dataKey, KeyDerivation.Derive("correct horse", salt, FastIterations));
        byte[] record = Encoding.UTF8.GetBytes("{\"id\":\"01JBK3Q9W6X2Y8Z4V5T7R1N0M3\",\"rev\":1}");
        string line = LineCipher.Encrypt(dataKey, record);

        byte[] unlockedKey = DataKey.Unwrap(wrappedKey, KeyDerivation.Derive("correct horse", salt, FastIterations));
        Assert.Equal(record, LineCipher.Decrypt(unlockedKey, line));
    }
}
