namespace Logbook.Core.Grammar;

/// <summary>Normalises a tag or person name: lowercase, with whitespace and underscores replaced by hyphens.</summary>
public static class TokenNormalizer
{
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Span<char> chars = stackalloc char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            chars[i] = char.IsWhiteSpace(c) || c == '_' ? '-' : char.ToLowerInvariant(c);
        }

        return new string(chars);
    }
}
