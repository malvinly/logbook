namespace Logbook.Core.Grammar;

/// <summary>
/// The result of parsing a filter line. <paramref name="Since"/> and <paramref name="Until"/> are inclusive dates;
/// <c>last:</c> is already folded into <paramref name="Since"/>.
/// </summary>
public sealed record QueryLine(
    IReadOnlyList<string> Words,
    IReadOnlyList<string> Phrases,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> People,
    DateOnly? Since,
    DateOnly? Until);
