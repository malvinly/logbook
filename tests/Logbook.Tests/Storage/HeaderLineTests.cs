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
}
