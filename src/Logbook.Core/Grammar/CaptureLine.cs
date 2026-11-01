namespace Logbook.Core.Grammar;

/// <summary>The result of parsing a capture line. <paramref name="OccurredAt"/> is null when no <c>at:</c> token was given.</summary>
public sealed record CaptureLine(
    string Summary,
    DateTimeOffset? OccurredAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> People);
