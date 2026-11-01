namespace Logbook.Core.Export;

/// <summary>The three timeline toggles of DESIGN.md section 11, remembered between uses by the app.</summary>
public sealed record ExportOptions(bool ShowGaps, bool GroupByMonth, bool Compact)
{
    public static readonly ExportOptions None = new(false, false, false);
}
