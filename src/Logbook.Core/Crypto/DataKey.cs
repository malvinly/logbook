using System.Security.Cryptography;

namespace Logbook.Core.Crypto;

/// <summary>
/// The random 256-bit key that encrypts every record. It is generated once when the file is created and stored in the
/// header wrapped under the key-encryption key, using the same framing as a record line (docs/file-format.md).
/// </summary>
public static class DataKey
{
    public static byte[] Generate() => RandomNumberGenerator.GetBytes(LineCipher.KeySize);

    public static string Wrap(byte[] dataKey, byte[] keyEncryptionKey) => LineCipher.Encrypt(keyEncryptionKey, dataKey);

    /// <summary>
    /// A wrong passphrase derives a wrong key-encryption key, and the GCM tag fails here with
    /// <see cref="CryptographicException"/>. The wrap is the passphrase verifier; there is no separate field.
    /// </summary>
    public static byte[] Unwrap(string wrappedKey, byte[] keyEncryptionKey)
    {
        byte[] dataKey = LineCipher.Decrypt(keyEncryptionKey, wrappedKey);
        if (dataKey.Length != LineCipher.KeySize)
        {
            throw new CryptographicException("Unwrapped data key has the wrong length.");
        }

        return dataKey;
    }
}
