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
            Refs = ["INC0041233", "chat thread 4 Oct"],
            RelatedTo = ["01K0000000000000000000000A", "01K0000000000000000000000B"],
        };
        Assert.Equal(
            "2026-08-03T11:05:00.0000000-05:00,2026-08-03T11:05:06.0000000-05:00,Asked for staging access,,asked staging-access,j-doe a-smith,INC0041233; chat thread 4 Oct,01K0000000000000000000000A 01K0000000000000000000000B,,01K0000000000000000000000B",
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
    public void ReasonWithBareLineFeedsIsQuotedAndStaysOneRow()
    {
        // The app stores a multi-line reason with "\n" alone, so this is the usual case, not the CRLF one.
        Entry entry = SampleEntries.FirstAsk with { Reason = "first line\nsecond line" };
        string[] rows = Rows([entry]);
        Assert.Equal(3, rows.Length); // header, the one row, and the empty piece after the last CRLF
        Assert.Contains(",Asked for staging access,\"first line\nsecond line\",asked staging-access,", rows[1], StringComparison.Ordinal);
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

    // Formula-looking text cells: spreadsheet apps run a cell starting with = + - @ as a formula.
    [Theory]
    [InlineData("=SUM(1) in the sheet")]
    [InlineData("+1 for that")]
    [InlineData("-negative first")]
    [InlineData("@j-doe said so")]
    public void SummaryStartingWithAFormulaCharacterGetsAnApostrophe(string summary)
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = summary };
        Assert.Contains(",'" + summary + ",", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=")]
    [InlineData("-")]
    public void SummaryThatIsOnlyAFormulaCharacterGetsAnApostrophe(string summary)
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = summary };
        Assert.Contains(",'" + summary + ",", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@a-smith")]
    public void ReasonStartingWithAFormulaCharacterGetsAnApostrophe(string reason)
    {
        Entry entry = SampleEntries.FirstAsk with { Reason = reason };
        Assert.Contains(",Asked for staging access,'" + reason + ",asked staging-access,", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(1)")]
    [InlineData("+44")]
    [InlineData("-x")]
    [InlineData("@thread")]
    public void RefsStartingWithAFormulaCharacterGetAnApostrophe(string firstRef)
    {
        Entry entry = SampleEntries.FirstAsk with { Refs = [firstRef, "INC0041233"] };
        Assert.Contains(",'" + firstRef + "; INC0041233,", Rows([entry])[1], StringComparison.Ordinal);
    }

    // Tags and people: the normaliser allows a leading - or +, so the joined cell can start with one.
    [Theory]
    [InlineData("-urgent")]
    [InlineData("+1")]
    [InlineData("=x")] // typed in the additional-tags box, which does not parse tokens
    public void TagsStartingWithAFormulaCharacterGetAnApostrophe(string firstTag)
    {
        Entry entry = SampleEntries.FirstAsk with { Tags = [firstTag, "asked"] };
        Assert.Contains(",Asked for staging access,,'" + firstTag + " asked,j-doe,", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-j-doe")]
    [InlineData("+a-smith")]
    public void PeopleStartingWithAFormulaCharacterGetAnApostrophe(string firstPerson)
    {
        Entry entry = SampleEntries.FirstAsk with { People = [firstPerson, "a-smith"] };
        Assert.Contains(",asked staging-access,'" + firstPerson + " a-smith,,", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFirstTagDecidesTheTagsCell()
    {
        Entry entry = SampleEntries.FirstAsk with { Tags = ["asked", "-urgent"] };
        Assert.Contains(",asked -urgent,j-doe,", Rows([entry])[1], StringComparison.Ordinal);
        Assert.DoesNotContain("'", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFirstPersonDecidesThePeopleCell()
    {
        Entry entry = SampleEntries.FirstAsk with { People = ["j-doe", "+a-smith"] };
        Assert.Contains(",asked staging-access,j-doe +a-smith,,", Rows([entry])[1], StringComparison.Ordinal);
        Assert.DoesNotContain("'", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Asked again")]
    [InlineData("'already quoted")]
    [InlineData("'")]
    [InlineData(" =leading space")]
    [InlineData("#tag-like")]
    [InlineData("1+1")]
    public void TextCellStartingWithAnyOtherCharacterIsUnchanged(string text)
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = text, Reason = text, Refs = [text] };
        Assert.Equal(
            "2026-08-03T11:05:00.0000000-05:00,2026-08-03T11:05:06.0000000-05:00," + text + "," + text + ",asked staging-access,j-doe," + text + ",,,01K0000000000000000000000B",
            Rows([entry])[1]);
    }

    [Theory]
    [InlineData("a=b")]
    [InlineData("a+b")]
    [InlineData("a-b")]
    [InlineData("a@b")]
    public void FormulaCharacterInSecondPlaceIsUnchanged(string text)
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = text, Reason = text, Refs = [text] };
        Assert.DoesNotContain("'", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ApostropheSitsInsideTheQuotesWhenTheCellIsAlsoQuoted()
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = "=\"x\",y" };
        Assert.Contains(",\"'=\"\"x\"\",y\",", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFirstRefDecidesTheRefsCell()
    {
        Entry entry = SampleEntries.FirstAsk with { Refs = ["INC0041233", "=later"] };
        Assert.Contains(",INC0041233; =later,", Rows([entry])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTextCellsAreNotPrefixed()
    {
        Entry entry = SampleEntries.FirstAsk with { Reason = null, Refs = [] };
        Assert.Equal(
            "2026-08-03T11:05:00.0000000-05:00,2026-08-03T11:05:06.0000000-05:00,Asked for staging access,,asked staging-access,j-doe,,,,01K0000000000000000000000B",
            Rows([entry])[1]);
    }

    [Fact]
    public void FormulaPrefixLeavesRelatedToRemindAtIdAndTimestampsVerbatim()
    {
        Entry entry = SampleEntries.FirstAsk with
        {
            Summary = "=a",
            Reason = "+b",
            Refs = ["-c"],
            Tags = ["-d"],
            People = ["+e"],
            RelatedTo = ["01K0000000000000000000000A"],
            RemindAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(-5)),
        };
        Assert.Equal(
            "2026-08-03T11:05:00.0000000-05:00,2026-08-03T11:05:06.0000000-05:00,'=a,'+b,'-d,'+e,'-c,01K0000000000000000000000A,2026-08-10T09:00:00.0000000-05:00,01K0000000000000000000000B",
            Rows([entry])[1]);
    }

    [Fact]
    public void HeaderRowIsUnchangedByTheFormulaRule()
    {
        Entry entry = SampleEntries.FirstAsk with { Summary = "=a" };
        Assert.Equal(Header, Rows([entry])[0]);
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
