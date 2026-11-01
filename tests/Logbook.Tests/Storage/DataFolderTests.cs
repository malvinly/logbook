using Logbook.Core.Storage;
using Xunit;

namespace Logbook.Tests.Storage;

public sealed class DataFolderTests
{
    [Theory]
    [InlineData("logbook.jsonl", true)]
    [InlineData("LOGBOOK.JSONL", true)]
    [InlineData("logbook-DESKTOP.jsonl", true)] // a sync conflict copy
    [InlineData("logbook (1).jsonl", true)]
    [InlineData("logbook-export-2026-10-31.jsonl", false)] // a decrypted export, never a candidate
    [InlineData("logbook.jsonl.bak", false)]
    [InlineData("notes.jsonl", false)]
    [InlineData("logbook.settings.json", false)]
    public void LogFileNamesAreLogbookJsonlButNotExports(string name, bool expected)
    {
        Assert.Equal(expected, DataFolder.IsLogFileName(name));
    }

    [Theory]
    [InlineData(@"C:\Users\j-doe\OneDrive\logbook.jsonl", @"C:\Users\j-doe\OneDrive", true)]
    [InlineData(@"C:\Users\j-doe\OneDrive\sub\x.jsonl", @"C:\Users\j-doe\OneDrive\", true)]
    [InlineData(@"C:\Users\j-doe\onedrive", @"C:\Users\j-doe\OneDrive", true)] // the root itself
    [InlineData(@"C:\Users\j-doe\OneDrive2\x.jsonl", @"C:\Users\j-doe\OneDrive", false)] // a sibling with a longer name
    [InlineData(@"C:\Users\j-doe\Documents\x.jsonl", @"C:\Users\j-doe\OneDrive", false)]
    [InlineData(@"C:\Users\j-doe\OneDrive\..\Documents\x.jsonl", @"C:\Users\j-doe\OneDrive", false)] // normalised first
    public void IsUnderComparesNormalisedFullPaths(string path, string root, bool expected)
    {
        Assert.Equal(expected, DataFolder.IsUnder(path, root));
    }
}
