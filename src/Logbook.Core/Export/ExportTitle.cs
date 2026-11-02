namespace Logbook.Core.Export;

/// <summary>
/// What the timeline header names the set by (docs/export.md): the saved-query button's name while
/// the filter box still holds exactly the filter that button put there, otherwise the filter text as typed.
/// </summary>
public static class ExportTitle
{
    /// <param name="filterText">The filter box's text now.</param>
    /// <param name="savedQueryName">The name of the saved-query button last clicked, or null when none was.</param>
    /// <param name="savedQueryFilter">The filter that button put into the box, or null when none was.</param>
    public static string For(string filterText, string? savedQueryName, string? savedQueryFilter)
    {
        ArgumentNullException.ThrowIfNull(filterText);

        // Ordinal equality is the whole test of "not edited since": a trailing space or a case change is an edit.
        return savedQueryName is not null && savedQueryFilter is not null && string.Equals(filterText, savedQueryFilter, StringComparison.Ordinal)
            ? savedQueryName
            : filterText;
    }
}
