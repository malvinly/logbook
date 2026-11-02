using Logbook.Core.Storage;

namespace Logbook.Core.Export;

/// <summary>
/// The default folder for a plaintext export (docs/export.md): the first candidate that is not the log's folder or
/// inside it. The app cannot tell which folders are synced, so the log's folder is the one it keeps the default out of.
/// </summary>
public static class PlaintextFolder
{
    /// <summary>The first of the existing candidates outside the log's folder, or null when every one is inside it.</summary>
    public static string? Choose(IReadOnlyList<string> existingCandidates, string logFolder)
    {
        ArgumentNullException.ThrowIfNull(existingCandidates);
        foreach (string folder in existingCandidates)
        {
            if (!DataFolder.IsUnder(folder, logFolder))
            {
                return folder;
            }
        }

        return null;
    }
}
