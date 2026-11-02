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
    [InlineData(@"C:\Users\j-doe\Sync\logbook.jsonl", @"C:\Users\j-doe\Sync", true)]
    [InlineData(@"C:\Users\j-doe\Sync\sub\x.jsonl", @"C:\Users\j-doe\Sync\", true)]
    [InlineData(@"C:\Users\j-doe\sync", @"C:\Users\j-doe\Sync", true)] // the root itself
    [InlineData(@"C:\Users\j-doe\Sync2\x.jsonl", @"C:\Users\j-doe\Sync", false)] // a sibling with a longer name
    [InlineData(@"C:\Users\j-doe\Documents\x.jsonl", @"C:\Users\j-doe\Sync", false)]
    [InlineData(@"C:\Users\j-doe\Sync\..\Documents\x.jsonl", @"C:\Users\j-doe\Sync", false)] // normalised first
    [InlineData(@"D:\x\logbook.jsonl", @"D:\", true)] // a drive root holds everything on the drive
    [InlineData(@"C:\x", @"D:\", false)]
    [InlineData(@"\\server\share\sub", @"\\server\share", true)]
    public void IsUnderComparesNormalisedFullPaths(string path, string root, bool expected)
    {
        Assert.Equal(expected, DataFolder.IsUnder(path, root));
    }
}
