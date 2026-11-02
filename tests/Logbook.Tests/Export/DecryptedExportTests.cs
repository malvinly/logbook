using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Storage;
using Logbook.Tests.Storage;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>Which records Export decrypted writes for each setting of its history checkbox (docs/export.md).</summary>
public sealed class DecryptedExportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempFile _file = new();

    public void Dispose() => _file.Dispose();

    // In file order: B created, A created, B edited, C created, C deleted.
    private static readonly Entry BFirst = SampleEntries.Make("01K0000000000000000000000B", Now, "b first", null, [], [], []);
    private static readonly Entry AFirst = SampleEntries.Make("01K0000000000000000000000A", Now, "a first", null, [], [], []);
    private static readonly Entry BEdited = BFirst with { Rev = 2, Summary = "b edited" };
    private static readonly Entry CFirst = SampleEntries.Make("01K0000000000000000000000C", Now, "c first", null, [], [], []);
    private static readonly Entry CDeleted = CFirst with { Rev = 2, Deleted = true };

    private static readonly IReadOnlyList<Entry> Revisions = [BFirst, AFirst, BEdited, CFirst, CDeleted];

    [Fact]
    public void WithoutHistoryWritesTheLiveEntriesOrderedById()
    {
        IReadOnlyList<Entry> live = [BEdited, AFirst]; // in no particular order, as a dictionary gives them
        Assert.Equal([AFirst, BEdited], DecryptedExport.Select(Revisions, live, includeHistory: false));
    }

    [Fact]
    public void WithHistoryWritesEveryRevisionInFileOrder()
    {
        IReadOnlyList<Entry> live = [BEdited, AFirst];
        Assert.Equal(Revisions, DecryptedExport.Select(Revisions, live, includeHistory: true));
    }

    [Fact]
    public void FromALoadedLogWithoutHistoryLeavesOutTombstonedIdsAndSupersededRevisions()
    {
        LogFile log = LoadedLog();
        IReadOnlyList<Entry> selected = DecryptedExport.Select(log.Revisions, log.Live, includeHistory: false);
        Assert.Equal([Key(AFirst), Key(BEdited)], selected.Select(Key));
        Assert.DoesNotContain(selected, entry => entry.Deleted || entry.Id == CFirst.Id);
    }

    [Fact]
    public void FromALoadedLogWithHistoryKeepsTombstonesAndSupersededRevisions()
    {
        LogFile log = LoadedLog();
        IReadOnlyList<Entry> selected = DecryptedExport.Select(log.Revisions, log.Live, includeHistory: true);
        Assert.Equal(Revisions.Select(Key), selected.Select(Key));
    }

    // Entry is a record with list members, so a loaded entry is not Equal to the one appended; compare what matters.
    private static (string Id, int Rev, string Summary, bool Deleted) Key(Entry entry) => (entry.Id, entry.Rev, entry.Summary, entry.Deleted);

    private LogFile LoadedLog()
    {
        LogFile created = LogFile.Create(_file.Path, "correct horse", Now, []);
        foreach (Entry entry in Revisions)
        {
            Assert.True(created.Append(entry));
        }

        return LogFile.Open(_file.Path, "correct horse", []);
    }
}
