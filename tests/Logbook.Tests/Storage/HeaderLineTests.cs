using System.Runtime.Versioning;
using System.Text;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

/// <summary>The in-place write of line 1 behind a passphrase change: fixed length, read back, put back on failure.</summary>
public sealed class HeaderLineTests : IDisposable
{
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly IReadOnlyList<TimeSpan> ShortDelays = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(20), 20)];

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void ReadReturnsLineOneWithoutItsNewline()
    {
        File.WriteAllText(_file.Path, "header\nrecord\n");
        Assert.Equal(Bytes("header"), HeaderLine.Read(_file.Path, NoDelays));
    }

    [Fact]
    public void ReadRefusesAFileWithoutACompleteFirstLine()
    {
        File.WriteAllText(_file.Path, "header");
        Assert.Throws<InvalidDataException>(() => HeaderLine.Read(_file.Path, NoDelays));
    }

    [Fact]
    public void OverwriteReplacesLineOneAndNothingElse()
    {
        File.WriteAllText(_file.Path, "AAAAAA\nrecord one\nrecord two\n");

        HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => true, NoDelays);

        Assert.Equal("BBBBBB\nrecord one\nrecord two\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void OverwriteRefusesADifferentLengthAndWritesNothing()
    {
        File.WriteAllText(_file.Path, "AAAAAA\nrecord\n");

        Assert.Throws<InvalidDataException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBB"), _ => true, NoDelays));

        Assert.Equal("AAAAAA\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void OverwriteRefusesWhenLineOneIsNotWhatTheCallerRead()
    {
        File.WriteAllText(_file.Path, "CCCCCC\nrecord\n");

        Assert.Throws<InvalidDataException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => true, NoDelays));

        Assert.Equal("CCCCCC\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void FailedReadBackPutsThePreviousHeaderBack()
    {
        File.WriteAllText(_file.Path, "AAAAAA\nrecord\n");

        IOException e = Assert.Throws<IOException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => false, NoDelays));

        Assert.Contains("written back", e.Message, StringComparison.Ordinal);
        Assert.Equal("AAAAAA\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void ReadBackCheckSeesTheBytesThatWereWritten()
    {
        File.WriteAllText(_file.Path, "AAAAAA\nrecord\n");
        byte[]? seen = null;

        HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), line => { seen = line; return true; }, NoDelays);

        Assert.Equal(Bytes("BBBBBB"), seen);
    }

    [Fact]
    public async Task OverwriteRetriesWhileAnotherProcessHoldsTheFile()
    {
        File.WriteAllText(_file.Path, "AAAAAA\nrecord\n");
        FileStream held = new(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        Task release = Task.Run(async () =>
        {
            await Task.Delay(100);
            await held.DisposeAsync();
        });

        HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => true, ShortDelays);
        await release;

        Assert.Equal("BBBBBB\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void LineOneThatAlreadyHoldsTheReplacementCountsAsWrittenAndIsReadBack()
    {
        // What a retry sees when the first attempt got the new bytes onto disk and then threw on flush or close.
        File.WriteAllText(_file.Path, "BBBBBB\nrecord\n");
        byte[]? seen = null;

        HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), line => { seen = line; return true; }, NoDelays);

        Assert.Equal(Bytes("BBBBBB"), seen);
        Assert.Equal("BBBBBB\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void LineOneThatOnlyStartsWithThePreviousBytesIsRefusedUnchanged()
    {
        File.WriteAllText(_file.Path, "AAAAAAX\nrecord\n");

        Assert.Throws<InvalidDataException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => true, NoDelays));

        Assert.Equal("AAAAAAX\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void FileHeldThroughEveryRetryIsAnIOExceptionAndLeftUntouched()
    {
        File.WriteAllText(_file.Path, "AAAAAA\nrecord\n");
        IReadOnlyList<TimeSpan> threeDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

        using (new FileStream(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => true, threeDelays));
        }

        Assert.Equal("AAAAAA\nrecord\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void RestoreLeavesALineOneThatIsNeitherHeaderAlone()
    {
        // Something else rewrote line 1 between our write and the read-back. The restore only ever undoes our own bytes.
        File.WriteAllText(_file.Path, "AAAAAA\nrecord\n");

        IOException e = Assert.Throws<IOException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ =>
        {
            File.WriteAllText(_file.Path, "CCCCCC\nrecord\n");
            return false;
        }, NoDelays));

        Assert.Equal("CCCCCC\nrecord\n", File.ReadAllText(_file.Path));
        Assert.DoesNotContain("unchanged", e.Message, StringComparison.Ordinal);
        Assert.Contains("new passphrase first", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ReadBackThatCannotReadTheFilePutsThePreviousHeaderBack()
    {
        // A byte-range lock deep in the record line lets the write of line 1 through (its buffered read stops at 4 KB)
        // but makes the read-back of the whole file fail.
        string content = "AAAAAA\n" + new string('r', 6000) + "\n";
        File.WriteAllText(_file.Path, content);
        IOException e;
        using (FileStream locker = new(_file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            locker.Lock(5000, 10);
            e = Assert.Throws<IOException>(() => HeaderLine.Overwrite(_file.Path, Bytes("AAAAAA"), Bytes("BBBBBB"), _ => true, NoDelays));
        }

        Assert.Contains("written back", e.Message, StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllText(_file.Path));
    }
}
