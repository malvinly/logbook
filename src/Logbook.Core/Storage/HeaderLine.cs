namespace Logbook.Core.Storage;

/// <summary>
/// Reads and overwrites line 1 of the data file in place, for a passphrase change (DESIGN.md section 8).
/// This is the one deliberate exception to the append-only rule, and it is fenced in: the new line must have exactly
/// the byte length of the old one, so no byte after line 1 is touched; it is written only over the bytes the caller
/// read; and it is read back afterwards, with the previous bytes written back if the read-back is wrong.
/// </summary>
internal static class HeaderLine
{
    /// <summary>The UTF-8 bytes of line 1 without its newline. Throws <see cref="InvalidDataException"/> when there is no complete line.</summary>
    public static byte[] Read(string path, IReadOnlyList<TimeSpan> retryDelays)
    {
        byte[] bytes = WithRetry(() => File.ReadAllBytes(path), retryDelays);
        int end = Array.IndexOf(bytes, (byte)'\n');
        if (end < 0)
        {
            throw new InvalidDataException("The file has no complete header line.");
        }

        return bytes[..end];
    }

    /// <summary>
    /// Writes <paramref name="replacement"/> over line 1, which must still be <paramref name="previous"/>, then reads it
    /// back and asks <paramref name="readBackIsValid"/> whether it is usable. Throws <see cref="InvalidDataException"/>
    /// without writing when the lengths differ or line 1 has changed, and <see cref="IOException"/> when the read-back
    /// failed, after writing the previous bytes back.
    /// </summary>
    public static void Overwrite(string path, byte[] previous, byte[] replacement, Func<byte[], bool> readBackIsValid, IReadOnlyList<TimeSpan> retryDelays)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(readBackIsValid);
        if (replacement.Length != previous.Length)
        {
            throw new InvalidDataException("The header was not written by Logbook in its usual form, so it cannot be replaced in place. The passphrase is unchanged.");
        }

        WithRetry(() => WriteAtStart(path, replacement, expected: previous), retryDelays);

        bool verified;
        try
        {
            byte[] back = Read(path, retryDelays);
            verified = back.AsSpan().SequenceEqual(replacement) && readBackIsValid(back);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            verified = false;
        }

        if (verified)
        {
            return;
        }

        try
        {
            WithRetry(() => WriteAtStart(path, previous, expected: null), retryDelays);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The new header did not read back correctly, and the previous header could not be written back. The file may need the old header restored by hand.", e);
        }

        throw new IOException("The new header did not read back correctly; the previous header was written back and the passphrase is unchanged.");
    }

    // FileShare.Read keeps every other writer out between the check and the write, so the bytes compared are the bytes
    // replaced. FileMode.Open never creates or truncates, and writing at position 0 for the old length leaves the newline
    // after line 1 and every record line where they are.
    private static void WriteAtStart(string path, byte[] bytes, byte[]? expected)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough);
        if (expected is not null)
        {
            byte[] current = new byte[expected.Length + 1];
            int read = stream.ReadAtLeast(current, current.Length, throwOnEndOfStream: false);
            if (read != current.Length || !current.AsSpan(0, expected.Length).SequenceEqual(expected) || current[^1] != (byte)'\n')
            {
                throw new InvalidDataException("The header changed on disk since it was read. The passphrase is unchanged.");
            }

            stream.Position = 0;
        }

        stream.Write(bytes);
        stream.Flush();
    }

    private static void WithRetry(Action action, IReadOnlyList<TimeSpan> retryDelays)
        => WithRetry(() =>
        {
            action();
            return true;
        }, retryDelays);

    // The same schedule as appends: a sync client or scanner may hold the file for a moment.
    private static T WithRetry<T>(Func<T> action, IReadOnlyList<TimeSpan> retryDelays)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < retryDelays.Count)
            {
                Thread.Sleep(retryDelays[attempt]);
            }
        }
    }
}
