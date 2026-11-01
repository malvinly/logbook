using System.Security.Cryptography;

namespace Logbook.Core.Crypto;

/// <summary>
/// Encrypts one line of the data file as <c>base64(nonce ‖ ciphertext ‖ tag)</c>: AES-256-GCM with a fresh random
/// 96-bit nonce per line and a 128-bit tag, no additional authenticated data (DESIGN.md section 8).
/// The same framing wraps the data key in the header.
/// </summary>
public static class LineCipher
{
    public const int KeySize = 32;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Encrypt(byte[] key, ReadOnlySpan<byte> plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        byte[] framed = new byte[NonceSize + plaintext.Length + TagSize];
        Span<byte> nonce = framed.AsSpan(0, NonceSize);
        Span<byte> ciphertext = framed.AsSpan(NonceSize, plaintext.Length);
        Span<byte> tag = framed.AsSpan(NonceSize + plaintext.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using AesGcm aes = new(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Convert.ToBase64String(framed);
    }

    /// <summary>
    /// Throws <see cref="FormatException"/> when the line is not base64 or is too short to hold a nonce and a tag,
    /// and <see cref="CryptographicException"/> when the key is wrong or the line was altered.
    /// </summary>
    public static byte[] Decrypt(byte[] key, string line)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(line);
        byte[] framed = Convert.FromBase64String(line);
        if (framed.Length < NonceSize + TagSize)
        {
            throw new FormatException("Encrypted line is shorter than a nonce and a tag.");
        }

        ReadOnlySpan<byte> nonce = framed.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> ciphertext = framed.AsSpan(NonceSize, framed.Length - NonceSize - TagSize);
        ReadOnlySpan<byte> tag = framed.AsSpan(framed.Length - TagSize);

        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm aes = new(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
