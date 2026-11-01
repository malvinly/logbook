using Logbook.Core;
using Logbook.Core.Grammar;

namespace Logbook.App.Chips;

public enum ChipKind
{
    Tag,
    Person,
    Date,
}

/// <summary>A small colored label for a tag, a person, or a parsed <c>at:</c> date. Styled by kind in Theme.xaml.</summary>
public sealed record Chip(ChipKind Kind, string Text)
{
    public static Chip Tag(string name) => new(ChipKind.Tag, "#" + name);

    public static Chip Person(string name) => new(ChipKind.Person, "@" + name);

    public static IReadOnlyList<Chip> For(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return [.. entry.Tags.Select(Tag), .. entry.People.Select(Person)];
    }

    public static IReadOnlyList<Chip> For(CaptureLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        List<Chip> chips = [.. line.Tags.Select(Tag), .. line.People.Select(Person)];
        if (line.OccurredAt is { } at)
        {
            chips.Add(new Chip(ChipKind.Date, Format.DateTime(at)));
        }

        return chips;
    }
}
