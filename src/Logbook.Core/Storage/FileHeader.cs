namespace Logbook.Core.Storage;

/// <summary>The <c>kdf</c> object of the header: how the passphrase becomes the key-encryption key.</summary>
public sealed record KdfParameters(string Alg, string Salt, int Iterations);

/// <summary>
/// Line 1 of the data file, stored as plaintext JSON (docs/file-format.md). The salt and
/// <paramref name="WrappedKey"/> are base64.
/// </summary>
public sealed record FileHeader(
    string Format,
    string FileId,
    DateTimeOffset CreatedAt,
    KdfParameters Kdf,
    string WrappedKey)
{
    public const string CurrentFormat = "logbook/1";
    public const string Pbkdf2HmacSha256 = "pbkdf2-hmac-sha256";
}
