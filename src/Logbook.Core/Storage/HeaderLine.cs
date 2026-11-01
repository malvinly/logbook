namespace Logbook.Core.Storage;

/// <summary>
/// Reads and overwrites line 1 of the data file in place, for a passphrase change (DESIGN.md section 8).
/// This is the one deliberate exception to the append-only rule, and it is fenced in: the new line must have exactly
/// the byte length of the old one, so no byte after line 1 is touched; it is written only over the bytes the caller
/// read; and it is read back afterwards, with the previous bytes written back if the read-back is wrong.
/// </summary>
internal static class HeaderLine
{
    private const string Advice = " Try the new passphrase first, then the old one; the diagnostics log has details.";

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
    /// without writing when the lengths differ or line 1 has changed, and <see cref="IOException"/> when the write or
    /// the read-back failed; the message then says whether the previous bytes were written back or which passphrase to
    /// try first when the header is in an unknown state.
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

        LineOne written = Replace(path, previous, replacement, "The new header was being written when the write failed, so the header is in an unknown state." + Advice, retryDelays);
        if (written == LineOne.WasNeither)
        {
            throw new InvalidDataException("The header changed on disk since it was read. The passphrase is unchanged.");
        }

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

        // The restore only ever undoes our own bytes: it writes the previous header only over the replacement.
        LineOne restored;
        try
        {
            restored = Replace(path, replacement, previous, "The previous header was being written back when the write failed.", retryDelays);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The new header did not read back correctly, and the previous header could not be written back, so the header is in an unknown state." + Advice, e);
        }

        if (restored == LineOne.WasNeither)
        {
            throw new IOException("The new header did not read back correctly, and line 1 now holds neither the previous header nor the new one, so it was left alone." + Advice);
        }

        throw new IOException("The new header did not read back correctly; the previous header was written back and the passphrase is unchanged.");
    }

    private enum LineOne
    {
        /// <summary>Line 1 now holds the new bytes, whether this call wrote them or an earlier attempt had.</summary>
        Replaced,

        /// <summary>Line 1 held neither the expected bytes nor the new ones, and nothing was written.</summary>
        WasNeither,
    }

    /// <summary>
    /// Writes <paramref name="to"/> over line 1 when it is <paramref name="from"/>, with retries. Throws
    /// <see cref="IOException"/> with <paramref name="unknownState"/> once an attempt has begun writing and line 1 cannot
    /// then be confirmed, since it may hold either line or a mix; any other failure leaves the file untouched.
    /// </summary>
    private static LineOne Replace(string path, byte[] from, byte[] to, string unknownState, IReadOnlyList<TimeSpan> retryDelays)
    {
        bool writeBegan = false;
        bool wasNeither;
        try
        {
            wasNeither = WithRetry(() => WriteOnce(path, from, to, ref writeBegan), retryDelays);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && writeBegan)
        {
            throw new IOException(unknownState, e);
        }

        if (wasNeither && writeBegan)
        {
            throw new IOException(unknownState);
        }

        return wasNeither ? LineOne.WasNeither : LineOne.Replaced;
    }

    // One attempt; returns true when line 1 is neither line. FileShare.Read keeps every other writer out between the
    // check and the write, so the bytes compared are the bytes replaced. FileMode.Open never creates or truncates, and
    // writing at position 0 for the old length leaves the newline after line 1 and every record line where they are.
    // Line 1 already holding the new bytes means an earlier attempt wrote them and then threw on flush or close.
    private static bool WriteOnce(string path, byte[] from, byte[] to, ref bool writeBegan)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough);
        byte[] current = new byte[from.Length + 1];
        int read = stream.ReadAtLeast(current, current.Length, throwOnEndOfStream: false);
        if (IsLineOne(current, read, to))
        {
            return false;
        }

        if (!IsLineOne(current, read, from))
        {
            return true;
        }

        stream.Position = 0;
        writeBegan = true;
        stream.Write(to);
        stream.Flush();
        return false;
    }

    // True when the bytes read are exactly the line followed by its newline, not merely a prefix of a longer line 1.
    private static bool IsLineOne(byte[] current, int read, byte[] line)
        => read == line.Length + 1 && current.AsSpan(0, line.Length).SequenceEqual(line) && current[^1] == (byte)'\n';

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
