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
    // An unauthenticated header could name any count; this bound keeps a damaged value from hanging the unlock.
    private const int MaxIterations = 10_000_000;

    // Two locks. The state lock is held only for memory updates, so the UI can read Live and PendingCount while a
    // write is retrying. The writer lock serialises file writes, so queued lines reach the disk in capture order.
    private readonly object _state = new();
    private readonly object _writer = new();
    private readonly LineAppender _appender;
    private readonly byte[] _dataKey;
    private readonly List<Entry> _revisions; // every readable revision in file order, then appends
    private readonly Dictionary<string, Entry> _latest = []; // highest rev per id; on a tie the later line wins
    private readonly Queue<string> _pending = new(); // encrypted lines that have not reached the disk, oldest first
    private bool _needsLeadingNewline; // the file ended mid-line when opened; the next write must start on a new line

    private LogFile(string path, FileHeader header, byte[] dataKey, List<Entry> revisions, int unreadableLineCount, bool trailingLineTruncated, bool endsMidLine, IReadOnlyList<TimeSpan>? retryDelays)
    {
        _appender = new LineAppender(path, retryDelays ?? LineAppender.DefaultRetryDelays);
        _dataKey = dataKey;
        _revisions = revisions;
        _needsLeadingNewline = endsMidLine;
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

    /// <summary>Revisions captured but not yet on disk. The user must always be able to see this is not zero.</summary>
    public int PendingCount
    {
        get
        {
            lock (_state)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>The highest revision of every id that has not been deleted.</summary>
    public IReadOnlyList<Entry> Live
    {
        get
        {
            lock (_state)
            {
                return [.. _latest.Values.Where(e => !e.Deleted)];
            }
        }
    }

    /// <summary>Every readable revision, tombstones included, in file order.</summary>
    public IReadOnlyList<Entry> Revisions
    {
        get
        {
            lock (_state)
            {
                return [.. _revisions];
            }
        }
    }

    /// <summary>Every revision of one id in file order. The last element is the one <see cref="Live"/> shows.</summary>
    public IReadOnlyList<Entry> RevisionsOf(string id)
    {
        lock (_state)
        {
            return [.. _revisions.Where(e => e.Id == id)];
        }
    }

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

        return new LogFile(path, header, dataKey, [], 0, false, false, retryDelays);
    }

    /// <summary>
    /// Throws <see cref="InvalidDataException"/> for a missing, malformed or unknown-format header,
    /// <see cref="CryptographicException"/> for a wrong passphrase, and the usual <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the file cannot be read.
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

        return new LogFile(path, header, dataKey, revisions, unreadable, trailingTruncated, endsMidLine: !terminated, retryDelays);
    }

    /// <summary>
    /// Encrypts and appends one revision. The entry is visible in memory at once. Returns false when the line could
    /// not be written after every retry and sits in the pending queue instead; the caller must tell the user.
    /// Anything already pending is written first so the file keeps capture order.
    /// </summary>
    public bool Append(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string line = LineCipher.Encrypt(_dataKey, Encoding.UTF8.GetBytes(LogbookJson.Serialize(entry)));
        lock (_state)
        {
            _revisions.Add(entry);
            Fold(entry);
            if (_needsLeadingNewline)
            {
                // Otherwise this line would fuse with the cut-off one and both would be unreadable next time.
                line = "\n" + line;
                _needsLeadingNewline = false;
            }

            _pending.Enqueue(line);
        }

        return WritePending();
    }

    /// <summary>Tries the pending queue again, oldest first. Returns true when nothing is left pending.</summary>
    public bool RetryPending() => WritePending();

    private bool WritePending()
    {
        lock (_writer)
        {
            while (true)
            {
                string? next;
                lock (_state)
                {
                    next = _pending.Count > 0 ? _pending.Peek() : null;
                }

                if (next is null)
                {
                    return true;
                }

                try
                {
                    _appender.Append(next);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                lock (_state)
                {
                    _pending.Dequeue();
                }
            }
        }
    }

    private void Fold(Entry entry)
    {
        // ">=" so that on equal revs the later line wins, which keeps RevisionsOf(id)[^1] equal to the live one.
        if (!_latest.TryGetValue(entry.Id, out Entry? current) || entry.Rev >= current.Rev)
        {
            _latest[entry.Id] = entry;
        }
    }

    /// <summary>
    /// Reads only the header, without a passphrase, so several candidate files can be described before one is chosen
    /// (DESIGN.md section 8). Throws <see cref="InvalidDataException"/> as <see cref="Open"/> does.
    /// </summary>
    public static FileHeader ReadHeader(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        int headerEnd = text.IndexOf('\n', StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            throw new InvalidDataException("The file has no complete header line.");
        }

        return ParseHeader(text[..headerEnd]);
    }

    /// <summary>The number of record lines after the header, complete or not.</summary>
    public static int CountRecordLines(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        int headerEnd = text.IndexOf('\n', StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            return 0;
        }

        string[] lines = text[(headerEnd + 1)..].Split('\n');
        return lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
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

        if (header.Kdf.Iterations < 1 || header.Kdf.Iterations > MaxIterations)
        {
            throw new InvalidDataException($"The header's iteration count {header.Kdf.Iterations} is out of range.");
        }

        try
        {
            if (Convert.FromBase64String(header.Kdf.Salt).Length != KeyDerivation.SaltSize)
            {
                throw new InvalidDataException("The header's salt has the wrong length.");
            }

            Convert.FromBase64String(header.WrappedKey);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException("The header's salt or wrapped key is not valid base64.", e);
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
