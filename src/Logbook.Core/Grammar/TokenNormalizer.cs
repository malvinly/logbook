namespace Logbook.Core.Grammar;

/// <summary>Normalises a tag or person name: lowercase, with whitespace and underscores replaced by hyphens.</summary>
public static class TokenNormalizer
{
    private static readonly char[] ListSeparators = [' ', ',', ';', '\t'];

    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Span<char> chars = name.Length <= 256 ? stackalloc char[name.Length] : new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            chars[i] = char.IsWhiteSpace(c) || c == '_' ? '-' : char.ToLowerInvariant(c);
        }

        return new string(chars);
    }

    /// <summary>
    /// The "additional tags" and "additional people" boxes: names separated by spaces, commas or semicolons, with an
    /// optional leading <c>#</c> or <c>@</c>, normalised and without duplicates, in the order first seen.
    /// </summary>
    public static List<string> NormalizeList(string text, IEnumerable<string>? existing = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> names = existing is null ? [] : [.. existing];
        foreach (string raw in text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = Normalize(raw.TrimStart('#', '@'));
            if (name.Length > 0 && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
