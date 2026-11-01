using Logbook.Core;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

/// <summary>A capture that cannot reach the disk is kept, shown as pending, and written on retry (DESIGN.md section 8).</summary>
public sealed class PendingQueueTests : IDisposable
{
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempFile _file = new();
    private readonly LogFile _log;

    public PendingQueueTests()
    {
        _log = LogFile.Create(_file.Path, "correct horse", Now, NoDelays);
    }

    public void Dispose() => _file.Dispose();

    private static Entry NewEntry(string summary) => new(
        Ulid.Generate(), 1, Now, Now, summary, null, [], [], [], [], false);

    private FileStream HoldFile() => new(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None);

    [Fact]
    public void SuccessfulAppendLeavesNothingPending()
    {
        Assert.True(_log.Append(NewEntry("first")));
        Assert.Equal(0, _log.PendingCount);
    }

    [Fact]
    public void FailedAppendIsReportedKeptInMemoryAndPending()
    {
        bool saved;
        using (HoldFile())
        {
            saved = _log.Append(NewEntry("first"));
        }

        Assert.False(saved);
        Assert.Equal(1, _log.PendingCount);
        Assert.Equal("first", Assert.Single(_log.Live).Summary);
        Assert.Empty(LogFile.Open(_file.Path, "correct horse", NoDelays).Live);
    }

    [Fact]
    public void RetryWritesThePendingLine()
    {
        using (HoldFile())
        {
            _log.Append(NewEntry("first"));
        }

        Assert.True(_log.RetryPending());
        Assert.Equal(0, _log.PendingCount);
        Assert.Equal("first", Assert.Single(LogFile.Open(_file.Path, "correct horse", NoDelays).Live).Summary);
    }

    [Fact]
    public void RetryWhileStillLockedKeepsEverythingPending()
    {
        using (HoldFile())
        {
            _log.Append(NewEntry("first"));
            _log.Append(NewEntry("second"));
            Assert.False(_log.RetryPending());
            Assert.Equal(2, _log.PendingCount);
        }
    }

    [Fact]
    public void NextCaptureWritesPendingLinesFirstInCaptureOrder()
    {
        using (HoldFile())
        {
            _log.Append(NewEntry("first"));
            _log.Append(NewEntry("second"));
        }

        Assert.True(_log.Append(NewEntry("third")));
        Assert.Equal(0, _log.PendingCount);
        Assert.Equal(["first", "second", "third"], LogFile.Open(_file.Path, "correct horse", NoDelays).Revisions.Select(e => e.Summary));
    }

    [Fact]
    public void RetryWithNothingPendingSucceeds()
    {
        Assert.True(_log.RetryPending());
    }
}
