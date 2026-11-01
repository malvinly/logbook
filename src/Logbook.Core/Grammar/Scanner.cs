using System.Text;

namespace Logbook.Core.Grammar;

/// <summary>
/// Splits a line into tokens using the rules of DESIGN.md section 6. The scanner knows nothing about dates;
/// it only decides where a token starts and ends. <see cref="LineParser"/> gives the tokens meaning.
/// </summary>
internal static class Scanner
{
    private static readonly string[] Keywords = ["at", "since", "until", "last"];

    public static List<Token> Scan(string text, bool quotedPhrases)
    {
        List<Token> tokens = [];
        StringBuilder plain = new();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            // "#" and "@" (and keywords) only start a token at the start of the line or after whitespace.
            bool atWordStart = i == 0 || char.IsWhiteSpace(text[i - 1]);

            if ((c == '#' || c == '@') && atWordStart && i + 1 < text.Length && char.IsLetter(text[i + 1]))
            {
                int end = EndOfWord(text, i);
                Flush(tokens, plain);
                tokens.Add(new Token(c == '#' ? TokenKind.Tag : TokenKind.Person, text[i..end], TokenNormalizer.Normalize(text[(i + 1)..end])));
                i = end;
                continue;
            }

            if (c == '"' && quotedPhrases && atWordStart)
            {
                // An unterminated quote runs to the end of the line, so a phrase works while it is still being typed.
                int close = text.IndexOf('"', i + 1);
                int end = close < 0 ? text.Length : close + 1;
                string inner = close < 0 ? text[(i + 1)..] : text[(i + 1)..close];
                Flush(tokens, plain);
                tokens.Add(new Token(TokenKind.Phrase, text[i..end], inner));
                i = end;
                continue;
            }

            if (atWordStart && TryMatchKeyword(text, i, out string key))
            {
                // The value must follow the colon immediately; "at: 3pm" is plain text.
                int valueStart = i + key.Length + 1;
                if (valueStart < text.Length && !char.IsWhiteSpace(text[valueStart]))
                {
                    int end = EndOfWord(text, valueStart);
                    Flush(tokens, plain);
                    tokens.Add(new Token(TokenKind.Keyword, text[i..end], text[valueStart..end], key));
                    i = end;
                    continue;
                }
            }

            plain.Append(c);
            i++;
        }

        Flush(tokens, plain);
        return tokens;
    }

    private static bool TryMatchKeyword(string text, int start, out string key)
    {
        foreach (string keyword in Keywords)
        {
            if (text.AsSpan(start).StartsWith(keyword, StringComparison.Ordinal) && start + keyword.Length < text.Length && text[start + keyword.Length] == ':')
            {
                key = keyword;
                return true;
            }
        }

        key = "";
        return false;
    }

    private static int EndOfWord(string text, int start)
    {
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return end;
    }

    private static void Flush(List<Token> tokens, StringBuilder plain)
    {
        if (plain.Length > 0)
        {
            string text = plain.ToString();
            tokens.Add(new Token(TokenKind.Text, text, text));
            plain.Clear();
        }
    }
}
