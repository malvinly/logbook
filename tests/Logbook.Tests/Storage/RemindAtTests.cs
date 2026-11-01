using System.Text;
using Logbook.Core;
using Logbook.Core.Crypto;
using Logbook.Core.Export;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

/// <summary>The remindAt field (DESIGN.md section 7): stored with its own offset, folded by revision, left out of the timeline.</summary>
public sealed class RemindAtTests : IDisposable
{
    private const string Passphrase = "correct horse";
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    // Deliberately in an offset other than the entry's and the test machine's, so any conversion would show.
    private static readonly DateTimeOffset Tokyo = new(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(9));

    // A record line exactly as builds before remindAt existed wrote it.
    private const string LineWithoutRemindAt =
        "{\"id\":\"01K0000000000000000000000H\",\"rev\":1,\"occurredAt\":\"2026-10-15T14:30:00-05:00\",\"recordedAt\":\"2026-10-15T14:30:06-05:00\"," +
        "\"summary\":\"Followed up; no response to the first ask.\",\"reason\":null," +
        "\"tags\":[\"asked\",\"staging-access\"],\"people\":[\"j-doe\"],\"refs\":[],\"relatedTo\":[],\"deleted\":false}";

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    private static Entry WithReminder() => SampleEntries.SampleFollowUp with { RemindAt = Tokyo };

    private LogFile Create() => LogFile.Create(_file.Path, Passphrase, Now, NoDelays);

    private LogFile Open() => LogFile.Open(_file.Path, Passphrase, NoDelays);

    private void AppendRawLine(LogFile log, string json)
    {
        byte[] salt = Convert.FromBase64String(log.Header.Kdf.Salt);
        byte[] key = DataKey.Unwrap(log.Header.WrappedKey, KeyDerivation.Derive(Passphrase, salt, log.Header.Kdf.Iterations));
        File.AppendAllText(_file.Path, LineCipher.Encrypt(key, Encoding.UTF8.GetBytes(json)) + "\n");
    }

    [Fact]
    public void RemindAtIsWrittenWithItsOwnOffsetAfterRelatedTo()
    {
        string json = LogbookJson.Serialize(WithReminder());
        Assert.Contains("\"relatedTo\":[],\"remindAt\":\"2026-10-20T09:00:00+09:00\",\"deleted\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsetRemindAtIsWrittenAsNull()
    {
        Assert.Contains("\"remindAt\":null", LogbookJson.Serialize(SampleEntries.SampleFollowUp), StringComparison.Ordinal);
    }

    [Fact]
    public void RemindAtRoundTripsThroughJsonWithItsOffset()
    {
        Entry loaded = LogbookJson.DeserializeEntry(LogbookJson.Serialize(WithReminder()));
        Assert.Equal(Tokyo, loaded.RemindAt);
        Assert.Equal(TimeSpan.FromHours(9), loaded.RemindAt!.Value.Offset);
    }

    [Fact]
    public void UnsetRemindAtRoundTripsAsNull()
    {
        Assert.Null(LogbookJson.DeserializeEntry(LogbookJson.Serialize(SampleEntries.SampleFollowUp)).RemindAt);
    }

    [Fact]
    public void LineWrittenBeforeRemindAtExistedLoadsAsNull()
    {
        Entry entry = LogbookJson.DeserializeEntry(LineWithoutRemindAt);
        Assert.Null(entry.RemindAt);
        Assert.Equal("Followed up; no response to the first ask.", entry.Summary);
    }

    [Fact]
    public void RemindAtRoundTripsThroughEncryptAppendAndLoad()
    {
        LogFile log = Create();
        log.Append(WithReminder());
        log.Append(SampleEntries.SampleAsk);

        LogFile reopened = Open();
        Entry loaded = reopened.Live.Single(e => e.Id == WithReminder().Id);
        Assert.Equal(Tokyo, loaded.RemindAt);
        Assert.Equal(TimeSpan.FromHours(9), loaded.RemindAt!.Value.Offset);
        Assert.Null(reopened.Live.Single(e => e.Id == SampleEntries.SampleAsk.Id).RemindAt);
    }

    [Fact]
    public void OldEncryptedLineWithoutRemindAtStillOpens()
    {
        LogFile log = Create();
        AppendRawLine(log, LineWithoutRemindAt);

        LogFile reopened = Open();
        Assert.Equal(0, reopened.UnreadableLineCount);
        Assert.Null(Assert.Single(reopened.Live).RemindAt);
    }

    [Fact]
    public void ARevisionSetsTheReminderAndALaterOneClearsIt()
    {
        LogFile log = Create();
        Entry first = SampleEntries.SampleFollowUp;
        log.Append(first);
        log.Append(first with { Rev = 2, RemindAt = Tokyo });
        Assert.Equal(Tokyo, Assert.Single(Open().Live).RemindAt);

        log.Append(first with { Rev = 3, RemindAt = null });
        LogFile reopened = Open();
        Assert.Null(Assert.Single(reopened.Live).RemindAt);
        Assert.Equal([null, Tokyo, null], reopened.RevisionsOf(first.Id).Select(r => r.RemindAt));
    }

    [Fact]
    public void AnEditThatLeavesTheReminderAloneKeepsIt()
    {
        LogFile log = Create();
        log.Append(WithReminder());
        log.Append(WithReminder() with { Rev = 2, Summary = "edited" });

        Assert.Equal(Tokyo, Assert.Single(Open().Live).RemindAt);
    }

    [Fact]
    public void TombstoneHidesAnEntryWithAReminder()
    {
        LogFile log = Create();
        Entry first = WithReminder();
        log.Append(first);
        AppendRawLine(log, "{\"id\":\"" + first.Id + "\",\"rev\":2,\"deleted\":true}");

        LogFile reopened = Open();
        Assert.Empty(reopened.Live);
        Assert.Equal([Tokyo, null], reopened.RevisionsOf(first.Id).Select(r => r.RemindAt));
    }

    [Fact]
    public void DecryptedExportWritesRemindAt()
    {
        LogFile log = Create();
        log.Append(WithReminder());

        // The decrypted export writes each record with LogbookJson.Serialize.
        Assert.Contains("\"remindAt\":\"2026-10-20T09:00:00+09:00\"", LogbookJson.Serialize(Assert.Single(Open().Live)), StringComparison.Ordinal);
    }

    [Fact]
    public void TimelineExportIsUnchangedByRemindAt()
    {
        ExportOptions all = new(ShowGaps: true, GroupByMonth: true, Compact: false, TagsAndPeople: true);
        List<Entry> reminded = [.. SampleEntries.Sample.Select(e => e with { RemindAt = Tokyo })];

        Assert.Equal(TimelineExport.Render("#staging-access", SampleEntries.Sample, all), TimelineExport.Render("#staging-access", reminded, all));
        Assert.Equal(TimelineExport.Render("", SampleEntries.Sample, ExportOptions.None), TimelineExport.Render("", reminded, ExportOptions.None));
    }
}
