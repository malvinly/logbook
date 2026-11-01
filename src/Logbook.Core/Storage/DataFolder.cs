namespace Logbook.Core.Storage;

/// <summary>Path rules the app needs around the data file: what counts as a sibling log, and what is inside a root.</summary>
public static class DataFolder
{
    /// <summary>
    /// A file the launch check must ask about: <c>logbook*.jsonl</c>, but not a decrypted export, whose name the
    /// export dialog fixes as <c>logbook-export-YYYY-MM-DD.jsonl</c> (DESIGN.md sections 8 and 11).
    /// </summary>
    public static bool IsLogFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName.StartsWith("logbook", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("logbook-export-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the path is the root itself or anywhere below it, compared as full paths, case-insensitively.</summary>
    public static bool IsUnder(string path, string root)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(root);
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
