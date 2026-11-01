namespace Logbook.Core.Grammar;

internal enum TokenKind
{
    /// <summary>Plain text between tokens, whitespace included.</summary>
    Text,
    Tag,
    Person,
    /// <summary>One of <c>at:</c>, <c>since:</c>, <c>until:</c>, <c>last:</c>. <see cref="Token.Key"/> is the word before the colon.</summary>
    Keyword,
    /// <summary>A <c>"quoted phrase"</c>; only produced when scanning a query.</summary>
    Phrase,
    /// <summary>
    /// Punctuation stripped from the end of a keyword's value (<c>at:yesterday.</c>). It is text, but kept apart so a
    /// reader knows it belongs to the token before it.
    /// </summary>
    Stripped,
}

/// <summary>
/// One piece of a scanned line. <paramref name="Raw"/> is the exact source slice so that a token one mode does not use
/// (for example <c>since:</c> in a capture) can be put back into the text unchanged.
/// </summary>
internal sealed record Token(TokenKind Kind, string Raw, string Value, string Key = "");
