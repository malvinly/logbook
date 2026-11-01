using Logbook.Core;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

public sealed class LogFileHeaderTests : IDisposable
{
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    private static Entry NewEntry(string summary) => new(Ulid.Generate(), 1, Now, Now, summary, null, [], [], [], [], null, false);

    [Fact]
    public void ReadHeaderNeedsNoPassphraseAndMatchesCreate()
    {
        LogFile log = LogFile.Create(_file.Path, "correct horse", Now, NoDelays);
        Assert.Equal(log.Header, LogFile.ReadHeader(_file.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"format\":\"logbook/1\"")]
    [InlineData("not json\n")]
    public void ReadHeaderRefusesBadFiles(string content)
    {
        File.WriteAllText(_file.Path, content);
        Assert.Throws<InvalidDataException>(() => LogFile.ReadHeader(_file.Path));
    }

    [Fact]
    public void CountRecordLinesCountsEverythingAfterTheHeader()
    {
        LogFile log = LogFile.Create(_file.Path, "correct horse", Now, NoDelays);
        Assert.Equal(0, LogFile.CountRecordLines(_file.Path));
        log.Append(NewEntry("first"));
        log.Append(NewEntry("second"));
        Assert.Equal(2, LogFile.CountRecordLines(_file.Path));
        File.AppendAllText(_file.Path, "partial");
        Assert.Equal(3, LogFile.CountRecordLines(_file.Path));
    }
}
