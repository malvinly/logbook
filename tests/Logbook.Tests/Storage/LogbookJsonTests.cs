using System.Text.Json;
using Logbook.Core;
using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

public sealed class LogbookJsonTests
{
    private static readonly Entry SampleEntry = new(
        "01JBK3Q9W6X2Y8Z4V5T7R1N0M3",
        1,
        new DateTimeOffset(2026, 10, 6, 10, 14, 3, TimeSpan.FromHours(-5)),
        new DateTimeOffset(2026, 10, 6, 10, 14, 9, TimeSpan.FromHours(-5)),
        "Asked for access to the staging environment; told it would be looked into",
        null,
        ["asked", "staging-access"],
        ["j-doe"],
        [],
        false);

    // The section 7 sample, minified.
    private const string SampleEntryJson =
        "{\"id\":\"01JBK3Q9W6X2Y8Z4V5T7R1N0M3\",\"rev\":1,\"occurredAt\":\"2026-10-06T10:14:03-05:00\",\"recordedAt\":\"2026-10-06T10:14:09-05:00\"," +
        "\"summary\":\"Asked for access to the staging environment; told it would be looked into\",\"reason\":null," +
        "\"tags\":[\"asked\",\"staging-access\"],\"people\":[\"j-doe\"],\"refs\":[],\"deleted\":false}";

    // Record equality compares the list properties by reference, so round trips are checked field by field.
    private static void AssertSameEntry(Entry expected, Entry actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Rev, actual.Rev);
        Assert.Equal(expected.OccurredAt, actual.OccurredAt);
        Assert.Equal(expected.RecordedAt, actual.RecordedAt);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.Reason, actual.Reason);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.People, actual.People);
        Assert.Equal(expected.Refs, actual.Refs);
        Assert.Equal(expected.Deleted, actual.Deleted);
    }

    [Fact]
    public void EntrySerializesExactlyAsTheDesignSample()
    {
        Assert.Equal(SampleEntryJson, LogbookJson.Serialize(SampleEntry));
    }

    [Fact]
    public void EntryRoundTrips()
    {
        Entry entry = SampleEntry with { Reason = "line one\nline two", Refs = ["INC0041233", "https://example.com/x"] };
        AssertSameEntry(entry, LogbookJson.DeserializeEntry(LogbookJson.Serialize(entry)));
    }

    [Fact]
    public void OffsetIsPreservedNotConverted()
    {
        Entry entry = SampleEntry with { OccurredAt = new DateTimeOffset(2026, 10, 6, 10, 14, 3, TimeSpan.FromHours(9)) };
        string json = LogbookJson.Serialize(entry);
        Assert.Contains("\"occurredAt\":\"2026-10-06T10:14:03+09:00\"", json, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromHours(9), LogbookJson.DeserializeEntry(json).OccurredAt.Offset);
    }

    [Fact]
    public void FractionalSecondsSurviveRoundTrip()
    {
        Entry entry = SampleEntry with { RecordedAt = new DateTimeOffset(2026, 10, 6, 10, 14, 9, 123, TimeSpan.Zero) };
        Assert.Equal(entry.RecordedAt, LogbookJson.DeserializeEntry(LogbookJson.Serialize(entry)).RecordedAt);
    }

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        string json = SampleEntryJson.Replace("\"rev\":1,", "\"rev\":1,\"relatedTo\":[\"x\"],\"remindAt\":null,", StringComparison.Ordinal);
        AssertSameEntry(SampleEntry, LogbookJson.DeserializeEntry(json));
    }

    [Fact]
    public void TombstoneWithOmittedFieldsLoadsWithEmptyValues()
    {
        Entry entry = LogbookJson.DeserializeEntry("{\"id\":\"01JBK3Q9W6X2Y8Z4V5T7R1N0M3\",\"rev\":2,\"deleted\":true}");
        Assert.True(entry.Deleted);
        Assert.Equal(2, entry.Rev);
        Assert.Equal("", entry.Summary);
        Assert.Null(entry.Reason);
        Assert.Empty(entry.Tags);
        Assert.Empty(entry.People);
        Assert.Empty(entry.Refs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"id\":")]
    [InlineData("null")]
    public void MalformedEntryIsAJsonException(string json)
    {
        Assert.Throws<JsonException>(() => LogbookJson.DeserializeEntry(json));
    }

    [Fact]
    public void HeaderSerializesExactlyAsTheDesignSample()
    {
        FileHeader header = new(
            "logbook/1",
            "01JBK3Q9W6X2Y8Z4V5T7R1N0M3",
            new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5)),
            new KdfParameters("pbkdf2-hmac-sha256", "c2FsdHNhbHRzYWx0c2FsdA==", 600000),
            "d3JhcHBlZA==");
        const string expected =
            "{\"format\":\"logbook/1\",\"fileId\":\"01JBK3Q9W6X2Y8Z4V5T7R1N0M3\",\"createdAt\":\"2026-10-06T09:00:00-05:00\"," +
            "\"kdf\":{\"alg\":\"pbkdf2-hmac-sha256\",\"salt\":\"c2FsdHNhbHRzYWx0c2FsdA==\",\"iterations\":600000},\"wrappedKey\":\"d3JhcHBlZA==\"}";
        Assert.Equal(expected, LogbookJson.Serialize(header));
        Assert.Equal(header, LogbookJson.DeserializeHeader(expected));
    }

    [Fact]
    public void HeaderConstantsMatchTheDesign()
    {
        Assert.Equal("logbook/1", FileHeader.CurrentFormat);
        Assert.Equal("pbkdf2-hmac-sha256", FileHeader.Pbkdf2HmacSha256);
    }
}
