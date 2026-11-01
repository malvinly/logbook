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
    public void CreatesTheFileAndWritesLineWithNewline()
    {
        new LineAppender(_file.Path, NoDelays).Append("first");
        Assert.Equal("first\n", File.ReadAllText(_file.Path));
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
        new LineAppender(_file.Path, NoDelays).Append("café");
        byte[] bytes = File.ReadAllBytes(_file.Path);
        Assert.Equal("café\n"u8.ToArray(), bytes);
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public async Task RetriesWhileAnotherProcessHoldsTheFile()
    {
        File.WriteAllText(_file.Path, "header\n");
        FileStream held = new(_file.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        Task release = Task.Run(async () =>
        {
            await Task.Delay(100);
            await held.DisposeAsync();
        });

        new LineAppender(_file.Path, ShortDelays).Append("line");
        await release;

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
    public void DefaultScheduleIsTenAttemptsDoublingFromOneHundredMilliseconds()
    {
        Assert.Equal(9, LineAppender.DefaultRetryDelays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), LineAppender.DefaultRetryDelays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(200), LineAppender.DefaultRetryDelays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), LineAppender.DefaultRetryDelays[2]);
        for (int i = 1; i < LineAppender.DefaultRetryDelays.Count; i++)
        {
            Assert.Equal(LineAppender.DefaultRetryDelays[i - 1] * 2, LineAppender.DefaultRetryDelays[i]);
        }
    }
}
