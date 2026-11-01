using System.Text;
using Logbook.Core;
using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>The CSV export: RFC 4180 quoting, CRLF rows, a header row, stored offsets kept, UTF-8 with a BOM.</summary>
public sealed class CsvExportTests
{
    private const string Header = "occurredAt,recordedAt,summary,reason,tags,people,refs,relatedTo,remindAt,id";

    private static string[] Rows(IEnumerable<Entry> entries) => CsvExport.Render(entries).Split("\r\n");

    [Fact]
    public void FirstRowIsTheHeader()
    {
        Assert.Equal(Header, Rows(SampleEntries.Sample)[0]);
    }

    [Fact]
    public void EmptySetIsOnlyTheHeaderRow()
    {
        Assert.Equal(Header + "\r\n", CsvExport.Render([]));
    }

    [Fact]
    public void OneRowPerEntryEachEndingInCrlf()
    {
        string csv = CsvExport.Render(SampleEntries.All);
        Assert.EndsWith("\r\n", csv, StringComparison.Ordinal);
        Assert.Equal(SampleEntries.All.Count + 2, csv.Split("\r\n").Length); // header, rows, and the empty piece after the last CRLF
        Assert.DoesNotContain("\n", csv.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void RowHasEveryColumnInOrder()
    {
        Assert.Equal(
            "2026-07-14T09:00:00.0000000-05:00,2026-07-14T09:00:06.0000000-05:00,Reported the printer outage to facilities,,reported printer,a-smith,INC0040001,,,01K0000000000000000000000A",
            Rows([SampleEntries.PrinterOutage])[1]);
    }

    [Fact]
    public void RowsAreOldestFirstWhateverTheInputOrder()
    {
        string[] rows = Rows(SampleEntries.Sample.Reverse());
        Assert.EndsWith(SampleEntries.SampleAsk.Id, rows[1], StringComparison.Ordinal);
        Assert.EndsWith(SampleEntries.SampleFollowUp.Id, rows[2], StringComparison.Ordinal);
        Assert.Contains(SampleEntries.SampleStandup.Id, rows[3], StringComparison.Ordinal);
    }

    [Fact]
    public void TimestampsKeepTheirStoredOffset()
    {
        string row = Rows([SampleEntries.TokyoMidnight])[1];
        Assert.StartsWith("2026-10-01T00:30:00.0000000+09:00,2026-10-01T00:30:06.0000000+09:00,", row, StringComparison.Ordinal);
    }

    [Fact]
    public void RemindAtKeepsItsOwnOffset()
    {
        Entry entry = SampleEntries.PrinterOutage with { RemindAt = new DateTimeOffset(2026, 7, 20, 8, 15, 0, TimeSpan.FromHours(2)) };
        Assert.EndsWith(",2026-07-20T08:15:00.0000000+02:00,01K0000000000000000000000A", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void NullsAreEmptyFields()
    {
        // No reason, no related entries, no reminder.
        string row = Rows([SampleEntries.Escalated])[1];
        Assert.Equal("2026-08-28T08:30:00.0000000-05:00,2026-08-28T08:30:06.0000000-05:00,Decided to escalate the printer issue,,decided escalated printer,,,,,01K0000000000000000000000D", row);
    }

    [Fact]
    public void ListsAreJoinedTagsPeopleAndIdsBySpaceRefsBySemicolon()
    {
        Entry entry = SampleEntries.FirstAsk with
        {
            People = ["j-doe", "a-smith"],
            Refs = ["INC0041233", "Teams thread 4 Oct"],
            RelatedTo = ["01K0000000000000000000000A", "01K0000000000000000000000B"],
        };
        Assert.Equal(
            "2026-08-03T11:05:00.0000000-05:00,2026-08-03T11:05:06.0000000-05:00,Asked for staging access,,asked staging-access,j-doe a-smith,INC0041233; Teams thread 4 Oct,01K0000000000000000000000A 01K0000000000000000000000B,,01K0000000000000000000000B",
            Rows([entry])[1]);
    }

    [Fact]
    public void FieldWithACommaIsQuoted()
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = "Asked again, politely" };
        Assert.Contains(",\"Asked again, politely\",", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FieldWithAQuoteIsQuotedAndTheQuoteDoubled()
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = "Told \"soon\" again" };
        Assert.Contains(",\"Told \"\"soon\"\" again\",", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FieldWithANewlineIsQuotedAndKeepsIt()
    {
        Entry entry = SampleEntries.FirstAsk with { Reason = "first line\r\nsecond line\nthird" };
        string csv = CsvExport.Render([entry]);
        Assert.Contains(",Asked for staging access,\"first line\r\nsecond line\nthird\",asked staging-access,", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldWithALoneCarriageReturnIsQuoted()
    {
        Entry entry = SampleEntries.FirstAsk with { Reason = "a\rb" };
        Assert.Contains(",\"a\rb\",", CsvExport.Render([entry]), StringComparison.Ordinal);
    }

    [Fact]
    public void RefWithACommaQuotesTheWholeField()
    {
        Entry entry = SampleEntries.FirstAsk with { Refs = ["a,b", "c"] };
        Assert.Contains(",\"a,b; c\",", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void PlainFieldsAreNotQuoted()
    {
        Assert.DoesNotContain("\"", CsvExport.Render(SampleEntries.All), StringComparison.Ordinal);
    }

    [Fact]
    public void FileBytesAreUtf8WithABom()
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = "Café — naïve" };
        byte[] bytes = CsvExport.FileBytes([entry]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(CsvExport.Render([entry]), Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        Assert.Equal((byte)'o', bytes[3]); // the header follows the BOM directly
    }
}
