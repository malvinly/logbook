using System.Security.Cryptography;

namespace Logbook.Core;

/// <summary>
/// Generates ULIDs: a 48-bit millisecond timestamp followed by 80 random bits, encoded as 26 Crockford base32 characters.
/// Ids generated at later timestamps sort after earlier ones with an ordinal string comparison.
/// There is no monotonic counter; two captures in the same millisecond is not a real case (DESIGN.md section 16).
/// </summary>
public static class Ulid
{
    // Crockford base32: digits then letters, skipping I, L, O and U.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate() => Generate(DateTimeOffset.UtcNow);

    public static string Generate(DateTimeOffset timestamp)
    {
        // DateTimeOffset.MaxValue (year 9999) is below 2^48 milliseconds, so only the low end needs a check.
        long milliseconds = timestamp.ToUnixTimeMilliseconds();
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp, "Timestamp must not be before the Unix epoch.");
        }

        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);

        // 10 chars hold 50 bits, enough for the 48-bit timestamp; each 5 random bytes (40 bits) is exactly 8 chars.
        Span<char> chars = stackalloc char[26];
        Encode((ulong)milliseconds, chars[..10]);
        Encode(ReadBigEndian(random[..5]), chars[10..18]);
        Encode(ReadBigEndian(random[5..]), chars[18..]);
        return new string(chars);
    }

    // Writes the low (5 * output.Length) bits of value into output, most significant group first.
    private static void Encode(ulong value, Span<char> output)
    {
        for (int i = output.Length - 1; i >= 0; i--)
        {
            output[i] = Alphabet[(int)(value & 0x1F)];
            value >>= 5;
        }
    }

    private static ulong ReadBigEndian(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        return value;
    }
}
