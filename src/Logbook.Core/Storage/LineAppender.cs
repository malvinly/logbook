using System.Text;

namespace Logbook.Core.Storage;

/// <summary>
/// Appends one line to the data file per the write rules of docs/file-format.md: open the existing file with
/// write-through, seek to its end, write the line and its newline in one call, flush, close. The file is never held open between writes.
/// A sync client may briefly lock the file during upload, so a failed open is retried with backoff.
/// </summary>
public sealed class LineAppender
{
    /// <summary>
    /// 100 ms doubling to 800 ms, then one second, nine waits for ten attempts: about 6.5 s in all.
    /// Longer outages are the 30-second timer's job; the user must hear about an unsaved capture sooner than that.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
    ];

    private readonly string _path;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public LineAppender(string path, IReadOnlyList<TimeSpan> retryDelays)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(retryDelays);
        _path = path;
        _retryDelays = retryDelays;
    }

    /// <summary>
    /// Throws the last <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> once every attempt has
    /// failed. Access denied is retried too: a sync client or scanner replacing the file reports it that way for a moment.
    /// </summary>
    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                WriteOnce(bytes);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < _retryDelays.Count)
            {
                Thread.Sleep(_retryDelays[attempt]);
            }
        }
    }

    private void WriteOnce(byte[] bytes)
    {
        // FileMode.Open, not Append: Append would create a missing file, and a data file deleted or moved away mid-session
        // must never come back without its header. The FileNotFoundException is an IOException, so it is retried and
        // then queued like any other failure, and the user hears about it.
        using FileStream stream = new(_path, FileMode.Open, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough);
        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes);
        stream.Flush();
    }
}
