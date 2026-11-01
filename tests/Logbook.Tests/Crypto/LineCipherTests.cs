using System.Security.Cryptography;
using System.Text;
using Logbook.Core.Crypto;
using Xunit;

namespace Logbook.Tests.Crypto;

public sealed class LineCipherTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(LineCipher.KeySize);
    private static readonly byte[] Record = Encoding.UTF8.GetBytes("{\"id\":\"01JBK3Q9W6X2Y8Z4V5T7R1N0M3\",\"summary\":\"Asked for access\"}");

    [Fact]
    public void RoundTripsPlaintext()
    {
        string line = LineCipher.Encrypt(Key, Record);
        Assert.Equal(Record, LineCipher.Decrypt(Key, line));
    }

    [Fact]
    public void RoundTripsEmptyPlaintext()
    {
        string line = LineCipher.Encrypt(Key, []);
        Assert.Empty(LineCipher.Decrypt(Key, line));
    }

    [Fact]
    public void LineIsBase64OfNonceCiphertextAndTag()
    {
        string line = LineCipher.Encrypt(Key, Record);
        byte[] framed = Convert.FromBase64String(line);
        Assert.Equal(12 + Record.Length + 16, framed.Length);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void SamePlaintextEncryptsDifferentlyEachTime()
    {
        string first = LineCipher.Encrypt(Key, Record);
        string second = LineCipher.Encrypt(Key, Record);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first[..16], second[..16]); // the nonce comes first, so even the prefix differs
    }

    [Fact]
    public void WrongKeyFailsVerification()
    {
        string line = LineCipher.Encrypt(Key, Record);
        byte[] otherKey = RandomNumberGenerator.GetBytes(LineCipher.KeySize);
        Assert.ThrowsAny<CryptographicException>(() => LineCipher.Decrypt(otherKey, line));
    }

    [Theory]
    [InlineData(0)] // nonce
    [InlineData(12)] // ciphertext
    [InlineData(-1)] // tag
    public void AlteredByteFailsVerification(int index)
    {
        byte[] framed = Convert.FromBase64String(LineCipher.Encrypt(Key, Record));
        int target = index < 0 ? framed.Length - 1 : index;
        framed[target] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => LineCipher.Decrypt(Key, Convert.ToBase64String(framed)));
    }

    [Fact]
    public void TruncatedLineFailsVerification()
    {
        string line = LineCipher.Encrypt(Key, Record);
        string truncated = line[..(line.Length - 8)];
        Assert.ThrowsAny<CryptographicException>(() => LineCipher.Decrypt(Key, truncated));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")] // 3 bytes, shorter than nonce + tag
    public void MalformedLineIsAFormatError(string line)
    {
        Assert.Throws<FormatException>(() => LineCipher.Decrypt(Key, line));
    }
}
