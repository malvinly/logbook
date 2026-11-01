using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Logbook.Core.Crypto;

namespace Logbook.Core.Storage;

/// <summary>
/// The one data file: a plaintext header line followed by one encrypted record per line (DESIGN.md section 8).
/// Opening reads and decrypts every line into memory; appending writes one new line. Nothing here ever
/// truncates or reorders the file; the only bytes ever rewritten are line 1's, in place, on a passphrase change.
/// </summary>
public sealed class LogFile
{
    // An unauthenticated header could name any count; this bound keeps a damaged value from hanging the unlock.
    private const int MaxIterations = 10_000_000;

    // Enough wraps, at a few microseconds each, to match even a rare old header with eight escaped '+' in its wrapped
    // key (about one draw in 25,000). It only bounds the search; a header of another form is refused before it starts.
    private const int MaxWrapAttempts = 1_000_000;

    // Two locks. The state lock is held only for memory updates, so the UI can read Live and PendingCount while a
    // write is retrying. The writer lock serialises file writes, so queued lines reach the disk in capture order.
    private readonly object _state = new();
    private readonly object _writer = new();
    private readonly string _path;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly LineAppender _appender;
    private readonly byte[] _dataKey;
    private readonly List<Entry> _revisions; // every readable revision in file order, then appends
    private readonly Dictionary<string, Entry> _latest = []; // highest rev per id; on a tie the later line wins
    private readonly Queue<string> _pending = new(); // encrypted lines that have not reached the disk, oldest first
    private bool _needsLeadingNewline; // the file ended mid-line when opened; the next write must start on a new line

    private LogFile(string path, FileHeader header, byte[] dataKey, List<Entry> revisions, int unreadableLineCount, bool trailingLineTruncated, bool endsMidLine, IReadOnlyList<TimeSpan>? retryDelays)
    {
        _path = path;
        _retryDelays = retryDelays ?? LineAppender.DefaultRetryDelays;
        _appender = new LineAppender(path, _retryDelays);
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

    /// <summary>Line 1 as last read or written. Replaced by <see cref="ChangePassphrase"/>.</summary>
    public FileHeader Header { get; private set; }

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

    /// <summary>
    /// Re-wraps the same data key under a new passphrase by rewriting line 1 in place; no record line changes
    /// (DESIGN.md section 8). The salt is new; format, file id, creation time and iteration count are kept, and the new
    /// header is built to the old one's exact byte length so it fits over it. Throws <see cref="ArgumentException"/> for an
    /// empty new passphrase, <see cref="InvalidOperationException"/> while captures are pending,
    /// <see cref="CryptographicException"/> for a wrong current passphrase, <see cref="InvalidDataException"/> when the
    /// header on disk is not one this app can replace in place (or belongs to another data key), and
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the file cannot be read or written.
    /// In every failure case the file is left as it was.
    /// </summary>
    public void ChangePassphrase(string currentPassphrase, string newPassphrase)
    {
        ArgumentNullException.ThrowIfNull(currentPassphrase);
        ArgumentException.ThrowIfNullOrEmpty(newPassphrase);

        // The writer lock keeps appends out for the whole change, so a header write never races a record write.
        lock (_writer)
        {
            if (PendingCount > 0)
            {
                throw new InvalidOperationException("Entries are still waiting to be written; change the passphrase once they are on disk.");
            }

            // Work from the header on disk, not the one in memory: that is what will be overwritten, and the current
            // passphrase is verified against it exactly as unlocking would.
            byte[] previous = HeaderLine.Read(_path, _retryDelays);
            FileHeader onDisk = ParseHeader(Encoding.UTF8.GetString(previous));
            byte[] currentKek = KeyDerivation.Derive(currentPassphrase, Convert.FromBase64String(onDisk.Kdf.Salt), onDisk.Kdf.Iterations);
            byte[] diskKey;
            try
            {
                diskKey = DataKey.Unwrap(onDisk.WrappedKey, currentKek); // a wrong current passphrase throws here
            }
            finally
            {
                CryptographicOperations.ZeroMemory(currentKek);
            }

            bool sameKey = CryptographicOperations.FixedTimeEquals(diskKey, _dataKey);
            CryptographicOperations.ZeroMemory(diskKey);
            if (!sameKey)
            {
                throw new InvalidDataException("The file on disk is not the log that is open (its key differs). The passphrase is unchanged.");
            }

            byte[] replacement = BuildSameLengthHeader(onDisk, newPassphrase, previous.Length, out FileHeader changed);
            if (!UnwrapsToDataKey(replacement, newPassphrase))
            {
                throw new CryptographicException("The new header did not verify. The passphrase is unchanged.");
            }

            HeaderLine.Overwrite(_path, previous, replacement, line => UnwrapsToDataKey(line, newPassphrase), _retryDelays);
            Header = changed;
        }
    }

    /// <summary>
    /// The header re-wrapped under the new passphrase, serialized to exactly <paramref name="length"/> UTF-8 bytes.
    /// Throws <see cref="InvalidDataException"/> when that length cannot come out, which means the old header was not
    /// written in this app's form.
    /// </summary>
    private byte[] BuildSameLengthHeader(FileHeader onDisk, string newPassphrase, int length, out FileHeader changed)
    {
        // System.Text.Json escapes '+' as the six characters \u002B, so a header's byte length depends on how many
        // '+' its random salt and wrapped key happen to contain. To fit over the old line exactly, the new salt is drawn
        // until it needs no escaping (about seven draws in ten); that fixes every byte of the line except the wrapped
        // key, so the length the wrapped key must escape to is known. The key is then wrapped again, each time with a fresh random nonce
        // under the same key-encryption key, until one escapes to that length; only that one is kept. The JSON form
        // itself is exactly what Create writes.
        string salt;
        do
        {
            salt = Convert.ToBase64String(KeyDerivation.NewSalt());
        }
        while (EscapedLength(salt) != salt.Length);

        byte[] newKek = KeyDerivation.Derive(newPassphrase, Convert.FromBase64String(salt), onDisk.Kdf.Iterations);
        try
        {
            string wrapped = DataKey.Wrap(_dataKey, newKek);
            FileHeader template = onDisk with { Kdf = onDisk.Kdf with { Salt = salt }, WrappedKey = new string('A', wrapped.Length) };
            int wanted = length - (Encoding.UTF8.GetByteCount(LogbookJson.Serialize(template)) - wrapped.Length);

            // Each '+' becomes the six bytes \u002B, five more than itself; any other length cannot come out.
            int extra = wanted - wrapped.Length;
            if (extra >= 0 && extra % 5 == 0)
            {
                for (int attempt = 0; attempt < MaxWrapAttempts; attempt++)
                {
                    if (EscapedLength(wrapped) == wanted)
                    {
                        changed = template with { WrappedKey = wrapped };
                        return Encoding.UTF8.GetBytes(LogbookJson.Serialize(changed));
                    }

                    wrapped = DataKey.Wrap(_dataKey, newKek);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newKek);
        }

        throw new InvalidDataException("The header was not written by Logbook in its usual form, so it cannot be replaced in place. The passphrase is unchanged.");
    }

    private static int EscapedLength(string value) => JsonEncodedText.Encode(value).EncodedUtf8Bytes.Length;

    // True when this header line parses and the passphrase unwraps this log's data key from it.
    private bool UnwrapsToDataKey(byte[] headerLine, string passphrase)
    {
        byte[]? kek = null;
        byte[]? key = null;
        try
        {
            FileHeader header = ParseHeader(Encoding.UTF8.GetString(headerLine));
            kek = KeyDerivation.Derive(passphrase, Convert.FromBase64String(header.Kdf.Salt), header.Kdf.Iterations);
            key = DataKey.Unwrap(header.WrappedKey, kek);
            return CryptographicOperations.FixedTimeEquals(key, _dataKey);
        }
        catch (Exception e) when (e is InvalidDataException or CryptographicException or FormatException)
        {
            return false;
        }
        finally
        {
            if (kek is not null)
            {
                CryptographicOperations.ZeroMemory(kek);
            }

            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
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
