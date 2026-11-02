using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

public sealed class DataFilePointerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void NoContentIsNoPointer(string? content)
    {
        Assert.Null(DataFilePointer.Parse(content));
    }

    [Theory]
    [InlineData("logbook.jsonl")]
    [InlineData(@"Logbook\logbook.jsonl")]
    [InlineData(@"..\Logbook\logbook.jsonl")]
    [InlineData(@"\Users\j-doe\logbook.jsonl")] // rooted but not fully qualified: no drive
    [InlineData(@"C:logbook.jsonl")] // drive-relative
    public void ARelativePathIsNoPointer(string content)
    {
        Assert.Null(DataFilePointer.Parse(content));
    }

    [Theory]
    [InlineData(@"C:\Users\j-doe\Logbook\logbook-export-2026-01-01.jsonl")] // a decrypted export
    [InlineData(@"C:\Users\j-doe\Logbook\notes.txt")]
    [InlineData(@"C:\Users\j-doe\Logbook\")]
    public void AFullPathToANonLogNameIsNoPointer(string content)
    {
        Assert.Null(DataFilePointer.Parse(content));
    }

    [Theory]
    [InlineData(@"C:\Users\j-doe\Logbook\logbook.jsonl")]
    [InlineData(@"D:\Sync\logbook-DESKTOP.jsonl")] // a conflict copy the user chose
    [InlineData(@"\\server\share\logbook.jsonl")]
    public void AFullPathToALogFileIsThePointer(string content)
    {
        Assert.Equal(content, DataFilePointer.Parse(content));
    }

    [Fact]
    public void SurroundingWhitespaceAndNewlinesAreTrimmed()
    {
        Assert.Equal(@"C:\Users\j-doe\Logbook\logbook.jsonl", DataFilePointer.Parse("  " + @"C:\Users\j-doe\Logbook\logbook.jsonl" + "\r\n"));
    }
}
