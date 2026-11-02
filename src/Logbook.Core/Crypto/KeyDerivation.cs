using System.Security.Cryptography;

namespace Logbook.Core.Crypto;

/// <summary>
/// Turns the passphrase into the 256-bit key-encryption key with PBKDF2-HMAC-SHA256, using the salt and
/// iteration count stored in the file header (docs/file-format.md).
/// </summary>
public static class KeyDerivation
{
    public const int DefaultIterations = 600_000;
    public const int SaltSize = 16;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] Derive(string passphrase, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, LineCipher.KeySize);
}
