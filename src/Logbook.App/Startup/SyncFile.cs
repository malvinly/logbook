using System.IO;
using Logbook.App.Diagnostics;

namespace Logbook.App.Startup;

/// <summary>The sync-service checks of DESIGN.md section 8: placeholder hydration and sibling log files.</summary>
internal static class SyncFile
{
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: the cloud filter keeps only a placeholder until the file is read.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>True when the file is a dehydrated placeholder. Reading it (which opening does) hydrates it.</summary>
    public static bool IsPlaceholder(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Offline) || attributes.HasFlag(RecallOnDataAccess) || attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Could not read file attributes", e);
            return false;
        }
    }

    /// <summary>Every <c>logbook*.jsonl</c> beside the data file, the data file included, sorted by name.</summary>
    public static IReadOnlyList<string> Siblings(string dataFilePath)
    {
        string folder = Path.GetDirectoryName(dataFilePath) ?? ".";
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.GetFiles(folder, "logbook*.jsonl").Order(StringComparer.OrdinalIgnoreCase)];
    }
}
