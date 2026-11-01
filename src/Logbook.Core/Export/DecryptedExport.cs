namespace Logbook.Core.Export;

/// <summary>
/// Which records Tools → Export decrypted writes (DESIGN.md section 11). The App serializes and writes them; this only
/// decides which ones, and in what order.
/// </summary>
public static class DecryptedExport
{
    /// <summary>
    /// Without history: <paramref name="live"/> (the latest revision per id, tombstoned ids already left out) ordered by
    /// id, which for ULIDs is creation order. With history: <paramref name="revisions"/> as they are, every line in file
    /// order, tombstones and superseded revisions included.
    /// </summary>
    public static IReadOnlyList<Entry> Select(IReadOnlyList<Entry> revisions, IReadOnlyList<Entry> live, bool includeHistory)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(live);
        return includeHistory ? revisions : [.. live.OrderBy(entry => entry.Id, StringComparer.Ordinal)];
    }
}
