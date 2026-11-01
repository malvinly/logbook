using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Logbook.Core.Crypto;

namespace Logbook.Core.Storage;

/// <summary>
/// The one data file: a plaintext header line followed by one encrypted record per line (DESIGN.md section 8).
/// Opening reads and decrypts every line into memory; appending writes one new line. Nothing here ever
/// truncates, rewrites or reorders the file.
/// </summary>
public sealed class LogFile
{
    private readonly LineAppender _appender;
    private readonly byte[] _dataKey;
    private readonly List<Entry> _revisions; // every readable revision in file order, then appends
    private readonly Dictionary<string, Entry> _latest = []; // highest rev per id

    private LogFile(string path, FileHeader header, byte[] dataKey, List<Entry> revisions, int unreadableLineCount, bool trailingLineTruncated, IReadOnlyList<TimeSpan>? retryDelays)
    {
        _appender = new LineAppender(path, retryDelays ?? LineAppender.DefaultRetryDelays);
        _dataKey = dataKey;
        _revisions = revisions;
        foreach (Entry entry in revisions)
        {
            Fold(entry);
        }

        Header = header;
        UnreadableLineCount = unreadableLineCount;
        TrailingLineTruncated = trailingLineTruncated;
    }

    public FileHeader Header { get; }

    /// <summary>Lines before the last one that failed base64, decryption or JSON. Shown to the user, never hidden.</summary>
    public int UnreadableLineCount { get; }

    /// <summary>True when the last line was incomplete or unreadable: the expected result of a crash mid-write.</summary>
    public bool TrailingLineTruncated { get; }

    /// <summary>The highest revision of every id that has not been deleted.</summary>
    public IReadOnlyList<Entry> Live => [.. _latest.Values.Where(e => !e.Deleted)];

    /// <summary>Every readable revision, tombstones included, in file order.</summary>
    public IReadOnlyList<Entry> Revisions => [.. _revisions];

    public IReadOnlyList<Entry> RevisionsOf(string id) => [.. _revisions.Where(e => e.Id == id)];

    /// <summary>Creates a new file with a fresh data key. Refuses to overwrite an existing file.</summary>
    public static LogFile Create(string path, string passphrase, DateTimeOffset now, IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        byte[] salt = KeyDerivation.NewSalt();
        byte[] dataKey = DataKey.Generate();
        byte[] kek = KeyDerivation.Derive(passphrase, salt, KeyDerivation.DefaultIterations);
        FileHeader header = new(
            FileHeader.CurrentFormat,
            Ulid.Generate(now),
            now,
            new KdfParameters(FileHeader.Pbkdf2HmacSha256, Convert.ToBase64String(salt), KeyDerivation.DefaultIterations),
            DataKey.Wrap(dataKey, kek));

        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough))
        {
            stream.Write(Encoding.UTF8.GetBytes(LogbookJson.Serialize(header) + "\n"));
            stream.Flush();
        }

        return new LogFile(path, header, dataKey, [], 0, false, retryDelays);
    }

    /// <summary>
    /// Throws <see cref="InvalidDataException"/> for a missing, malformed or unknown-format header and
    /// <see cref="CryptographicException"/> for a wrong passphrase.
    /// </summary>
    public static LogFile Open(string path, string passphrase, IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        int headerEnd = text.IndexOf('\n', StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            throw new InvalidDataException("The file has no complete header line.");
        }

        FileHeader header = ParseHeader(text[..headerEnd]);
        byte[] kek = KeyDerivation.Derive(passphrase, Convert.FromBase64String(header.Kdf.Salt), header.Kdf.Iterations);
        byte[] dataKey = DataKey.Unwrap(header.WrappedKey, kek); // the wrap is the passphrase verifier

        // A terminating newline leaves an empty final segment, which is not a line. Without one, the final
        // segment is a line that was cut off mid-write.
        string[] lines = text[(headerEnd + 1)..].Split('\n');
        bool terminated = lines[^1].Length == 0;
        int lineCount = terminated ? lines.Length - 1 : lines.Length;

        List<Entry> revisions = [];
        int unreadable = 0;
        bool trailingTruncated = false;
        for (int i = 0; i < lineCount; i++)
        {
            bool isLast = i == lineCount - 1;
            if (isLast && !terminated)
            {
                trailingTruncated = true;
            }
            else if (TryReadRecord(dataKey, lines[i], out Entry? entry))
            {
                revisions.Add(entry);
            }
            else if (isLast)
            {
                trailingTruncated = true;
            }
            else
            {
                unreadable++;
            }
        }

        return new LogFile(path, header, dataKey, revisions, unreadable, trailingTruncated, retryDelays);
    }

    /// <summary>Encrypts and appends one revision. Throws <see cref="IOException"/> when every write attempt failed.</summary>
    public void Append(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string line = LineCipher.Encrypt(_dataKey, Encoding.UTF8.GetBytes(LogbookJson.Serialize(entry)));
        _appender.Append(line);
        _revisions.Add(entry);
        Fold(entry);
    }

    private void Fold(Entry entry)
    {
        if (!_latest.TryGetValue(entry.Id, out Entry? current) || entry.Rev >= current.Rev)
        {
            _latest[entry.Id] = entry;
        }
    }

    private static FileHeader ParseHeader(string line)
    {
        FileHeader header;
        try
        {
            header = LogbookJson.DeserializeHeader(line);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The header line is not valid JSON.", e);
        }

        if (header.Format != FileHeader.CurrentFormat)
        {
            throw new InvalidDataException($"Unknown file format '{header.Format}'.");
        }

        if (header.Kdf?.Alg != FileHeader.Pbkdf2HmacSha256 || header.Kdf.Salt is null || header.WrappedKey is null)
        {
            throw new InvalidDataException("The header is missing or has an unknown key derivation.");
        }

        return header;
    }

    private static bool TryReadRecord(byte[] dataKey, string line, out Entry entry)
    {
        try
        {
            entry = LogbookJson.DeserializeEntry(Encoding.UTF8.GetString(LineCipher.Decrypt(dataKey, line)));
            return !string.IsNullOrEmpty(entry.Id);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or JsonException)
        {
            entry = null!;
            return false;
        }
    }
}
