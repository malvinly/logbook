using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

public sealed class LineAppenderTests : IDisposable
{
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly IReadOnlyList<TimeSpan> ShortDelays = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(20), 20)];

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    [Fact]
    public void WritesLineWithNewline()
    {
        File.WriteAllText(_file.Path, "");
        new LineAppender(_file.Path, NoDelays).Append("first");
        Assert.Equal("first\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void MissingFileIsAnIOExceptionAfterTheRetriesAndIsNeverCreated()
    {
        // A file deleted or moved away mid-session must not come back headerless; the write fails so the user hears of it.
        IReadOnlyList<TimeSpan> threeDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];
        Assert.ThrowsAny<IOException>(() => new LineAppender(_file.Path, threeDelays).Append("line"));
        Assert.False(File.Exists(_file.Path));
    }

    [Fact]
    public void AppendsAfterExistingContentWithoutChangingIt()
    {
        File.WriteAllText(_file.Path, "header\nfirst\n");
        new LineAppender(_file.Path, NoDelays).Append("second");
        Assert.Equal("header\nfirst\nsecond\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void UsesLineFeedOnlyAndUtf8()
    {
        File.WriteAllText(_file.Path, "");
        new LineAppender(_file.Path, NoDelays).Append("café");
        byte[] bytes = File.ReadAllBytes(_file.Path);
        Assert.Equal("café\n"u8.ToArray(), bytes);
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void RetriesWhileAnotherProcessHoldsTheFile()
    {
        File.WriteAllText(_file.Path, "header\n");
        FileStream held = new(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        Thread release = Later.Run(TimeSpan.FromMilliseconds(100), held.Dispose);

        new LineAppender(_file.Path, ShortDelays).Append("line");
        release.Join();

        Assert.Equal("header\nline\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void GivesUpAfterTheLastDelayAndLeavesTheFileUntouched()
    {
        File.WriteAllText(_file.Path, "header\n");
        IReadOnlyList<TimeSpan> threeDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];
        using (new FileStream(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => new LineAppender(_file.Path, threeDelays).Append("line"));
        }

        Assert.Equal("header\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void DefaultScheduleIsExactlyTheOneInTheDesign()
    {
        // docs/file-format.md: 100, 200, 400, 800 ms, then 1 s, for ten attempts in all.
        TimeSpan[] expected =
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
        Assert.Equal(expected, LineAppender.DefaultRetryDelays);
    }

    [Fact]
    public void DefaultScheduleIsTenAttemptsStartingAtOneHundredMillisecondsAndUnderTenSeconds()
    {
        Assert.Equal(9, LineAppender.DefaultRetryDelays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), LineAppender.DefaultRetryDelays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(200), LineAppender.DefaultRetryDelays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), LineAppender.DefaultRetryDelays[2]);
        TimeSpan total = TimeSpan.Zero;
        foreach (TimeSpan delay in LineAppender.DefaultRetryDelays)
        {
            total += delay;
        }

        Assert.True(total < TimeSpan.FromSeconds(10), $"total wait {total} must stay short; the 30-second timer covers the rest");
    }

    [Fact]
    public void RetriesWhileTheFileIsReadOnly()
    {
        File.WriteAllText(_file.Path, "header\n");
        File.SetAttributes(_file.Path, FileAttributes.ReadOnly);
        Thread release = Later.Run(TimeSpan.FromMilliseconds(100), () => File.SetAttributes(_file.Path, FileAttributes.Normal));

        new LineAppender(_file.Path, ShortDelays).Append("line");
        release.Join();

        Assert.Equal("header\nline\n", File.ReadAllText(_file.Path));
    }

    [Fact]
    public void GivesUpOnAReadOnlyFileWithUnauthorizedAccess()
    {
        File.WriteAllText(_file.Path, "header\n");
        File.SetAttributes(_file.Path, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => new LineAppender(_file.Path, NoDelays).Append("line"));
        }
        finally
        {
            File.SetAttributes(_file.Path, FileAttributes.Normal);
        }
    }
}
