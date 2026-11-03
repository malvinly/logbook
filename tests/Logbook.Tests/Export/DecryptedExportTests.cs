using Logbook.Core;
using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>Which records Export decrypted writes for each setting of its history checkbox (docs/export.md).</summary>
public sealed class DecryptedExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

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
}
