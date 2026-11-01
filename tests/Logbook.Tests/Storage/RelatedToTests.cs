using System.Text;
using Logbook.Core;
using Logbook.Core.Crypto;
using Logbook.Core.Export;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

/// <summary>The relatedTo field (DESIGN.md section 7): stored, loaded, folded and exported like every other field.</summary>
public sealed class RelatedToTests : IDisposable
{
    private const string Passphrase = "correct horse";
    private const string OriginalId = "01K0000000000000000000000G";
    private static readonly IReadOnlyList<TimeSpan> NoDelays = [];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    // A record line exactly as builds before relatedTo existed wrote it.
    private const string LineWithoutRelatedTo =
        "{\"id\":\"01K0000000000000000000000H\",\"rev\":1,\"occurredAt\":\"2026-10-15T14:30:00-05:00\",\"recordedAt\":\"2026-10-15T14:30:06-05:00\"," +
        "\"summary\":\"Followed up; no response to the first ask.\",\"reason\":null," +
        "\"tags\":[\"asked\",\"staging-access\"],\"people\":[\"j-doe\"],\"refs\":[],\"deleted\":false}";

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    private static Entry FollowUp() => SampleEntries.SampleFollowUp with { RelatedTo = [OriginalId] };

    private LogFile Create() => LogFile.Create(_file.Path, Passphrase, Now, NoDelays);

    private LogFile Open() => LogFile.Open(_file.Path, Passphrase, NoDelays);

    private void AppendRawLine(LogFile log, string json)
    {
        byte[] salt = Convert.FromBase64String(log.Header.Kdf.Salt);
        byte[] key = DataKey.Unwrap(log.Header.WrappedKey, KeyDerivation.Derive(Passphrase, salt, log.Header.Kdf.Iterations));
        File.AppendAllText(_file.Path, LineCipher.Encrypt(key, Encoding.UTF8.GetBytes(json)) + "\n");
    }

    [Fact]
    public void RelatedToIsWrittenAsACamelCaseListAfterRefs()
    {
        string json = LogbookJson.Serialize(FollowUp());
        Assert.Contains("\"refs\":[],\"relatedTo\":[\"01K0000000000000000000000G\"],", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RelatedToRoundTripsThroughJson()
    {
        Entry entry = FollowUp() with { RelatedTo = [OriginalId, "01K0000000000000000000000E"] };
        Assert.Equal(entry.RelatedTo, LogbookJson.DeserializeEntry(LogbookJson.Serialize(entry)).RelatedTo);
    }

    [Fact]
    public void LineWrittenBeforeRelatedToExistedLoadsWithAnEmptyList()
    {
        Entry entry = LogbookJson.DeserializeEntry(LineWithoutRelatedTo);
        Assert.NotNull(entry.RelatedTo);
        Assert.Empty(entry.RelatedTo);
        Assert.Equal("Followed up; no response to the first ask.", entry.Summary);
    }

    [Fact]
    public void NullRelatedToLoadsAsAnEmptyList()
    {
        Entry entry = LogbookJson.DeserializeEntry(LineWithoutRelatedTo.Replace("\"refs\":[],", "\"refs\":[],\"relatedTo\":null,", StringComparison.Ordinal));
        Assert.Empty(entry.RelatedTo);
    }

    [Fact]
    public void RelatedToRoundTripsThroughEncryptAppendAndLoad()
    {
        Create().Append(FollowUp());
        Assert.Equal([OriginalId], Assert.Single(Open().Live).RelatedTo);
    }

    [Fact]
    public void OldEncryptedLineWithoutRelatedToStillOpens()
    {
        LogFile log = Create();
        AppendRawLine(log, LineWithoutRelatedTo);
        log.Append(FollowUp() with { Id = "01K0000000000000000000000J" });

        LogFile reopened = Open();
        Assert.Equal(0, reopened.UnreadableLineCount);
        Assert.Empty(reopened.Live.Single(e => e.Id == "01K0000000000000000000000H").RelatedTo);
        Assert.Equal([OriginalId], reopened.Live.Single(e => e.Id == "01K0000000000000000000000J").RelatedTo);
    }

    [Fact]
    public void AnEditPreservesRelatedTo()
    {
        LogFile log = Create();
        Entry first = FollowUp();
        log.Append(first);
        log.Append(first with { Rev = 2, Summary = "Followed up again" });

        Entry live = Assert.Single(Open().Live);
        Assert.Equal(2, live.Rev);
        Assert.Equal("Followed up again", live.Summary);
        Assert.Equal([OriginalId], live.RelatedTo);
    }

    [Fact]
    public void TombstoneStillFoldsAndKeepsRelatedToInTheHistory()
    {
        LogFile log = Create();
        Entry first = FollowUp();
        log.Append(first);
        AppendRawLine(log, "{\"id\":\"" + first.Id + "\",\"rev\":2,\"deleted\":true}");

        LogFile reopened = Open();
        Assert.Empty(reopened.Live);
        IReadOnlyList<Entry> history = reopened.RevisionsOf(first.Id);
        Assert.Equal([OriginalId], history[0].RelatedTo);
        Assert.Empty(history[1].RelatedTo);
    }

    [Fact]
    public void DeletingTheOriginalLeavesTheFollowUpPointingAtIt()
    {
        LogFile log = Create();
        log.Append(SampleEntries.SampleAsk);
        log.Append(FollowUp());
        log.Append(SampleEntries.SampleAsk with { Rev = 2, Deleted = true });

        Entry live = Assert.Single(Open().Live);
        Assert.Equal([OriginalId], live.RelatedTo);
    }

    [Fact]
    public void DecryptedExportWritesRelatedTo()
    {
        LogFile log = Create();
        log.Append(FollowUp());
        log.Append(FollowUp() with { Rev = 2, Summary = "edited" });

        // The decrypted export writes each record with LogbookJson.Serialize, latest revisions or the full history.
        LogFile reopened = Open();
        Assert.Contains("\"relatedTo\":[\"01K0000000000000000000000G\"]", LogbookJson.Serialize(Assert.Single(reopened.Live)), StringComparison.Ordinal);
        Assert.All(reopened.Revisions, r => Assert.Contains("\"relatedTo\":[\"01K0000000000000000000000G\"]", LogbookJson.Serialize(r), StringComparison.Ordinal));
    }

    [Fact]
    public void TimelineExportIsUnchangedByRelatedTo()
    {
        ExportOptions all = new(ShowGaps: true, GroupByMonth: true, Compact: false, TagsAndPeople: true);
        List<Entry> linked = [SampleEntries.SampleAsk, FollowUp(), SampleEntries.SampleStandup with { RelatedTo = [OriginalId, FollowUp().Id] }];

        Assert.Equal(TimelineExport.Render("#staging-access", SampleEntries.Sample, all), TimelineExport.Render("#staging-access", linked, all));
        Assert.Equal(TimelineExport.Render("", SampleEntries.Sample, ExportOptions.None), TimelineExport.Render("", linked, ExportOptions.None));
    }
}
