using System.Globalization;
using System.Text;

namespace Logbook.Core.Grammar;

/// <summary>
/// The one grammar of DESIGN.md section 6, read two ways. In a capture, query-only tokens are summary text;
/// in a query, <c>at:</c> is a bare word. A keyword whose value does not parse is treated as text in both.
/// </summary>
public static class LineParser
{
    public static CaptureLine ParseCapture(string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder summary = new();
        List<string> tags = [];
        List<string> people = [];
        DateTimeOffset? occurredAt = null;
        TokenKind previous = TokenKind.Text;

        foreach (Token token in Scanner.Scan(text, quotedPhrases: false))
        {
            switch (token.Kind)
            {
                case TokenKind.Tag:
                    AddUnique(tags, token.Value);
                    break;
                case TokenKind.Person:
                    AddUnique(people, token.Value);
                    break;
                case TokenKind.Keyword when token.Key == "at" && DateValues.TryParseAt(token.Value, now, out DateTimeOffset at):
                    occurredAt = at; // The last at: on the line wins.
                    break;
                default:
                    // Text that touches a tag or person is the punctuation stripped from it ("asked @j-doe, then"). With the
                    // token gone, the space before it would be left in front of the comma, so it goes too: "asked, then".
                    if (previous is TokenKind.Tag or TokenKind.Person && token.Raw.Length > 0 && !char.IsWhiteSpace(token.Raw[0]))
                    {
                        TrimEnd(summary);
                    }

                    summary.Append(token.Raw);
                    break;
            }

            previous = token.Kind;
        }

        return new CaptureLine(CollapseWhitespace(summary.ToString()), occurredAt, tags, people);
    }

    public static QueryLine ParseQuery(string text, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> words = [];
        List<string> phrases = [];
        List<string> tags = [];
        List<string> people = [];
        DateOnly? since = null;
        DateOnly? until = null;

        foreach (Token token in Scanner.Scan(text, quotedPhrases: true))
        {
            switch (token.Kind)
            {
                case TokenKind.Tag:
                    AddUnique(tags, token.Value);
                    break;
                case TokenKind.Person:
                    AddUnique(people, token.Value);
                    break;
                case TokenKind.Phrase:
                    if (token.Value.Length > 0)
                    {
                        phrases.Add(token.Value);
                    }

                    break;
                case TokenKind.Keyword when token.Key == "since" && DateValues.TryParseQueryDate(token.Value, today, out DateOnly sinceDate):
                    since = sinceDate;
                    break;
                case TokenKind.Keyword when token.Key == "until" && DateValues.TryParseQueryDate(token.Value, today, out DateOnly untilDate):
                    until = untilDate;
                    break;
                case TokenKind.Keyword when token.Key == "last" && DateValues.TryParseLast(token.Value, today, out DateOnly lastDate):
                    since = lastDate;
                    break;
                default:
                    words.AddRange(token.Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    break;
            }
        }

        return new QueryLine(words, phrases, tags, people, since, until);
    }

    /// <summary>
    /// The date-range picker's rewrite (DESIGN.md section 10): the same filter text with every <c>since:</c>,
    /// <c>until:</c> and <c>last:</c> token removed and the given bounds appended. Phrases and other tokens are untouched.
    /// </summary>
    public static string WithDateRange(string text, DateOnly? since, DateOnly? until)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder rest = new();
        bool removedPrevious = false;
        foreach (Token token in Scanner.Scan(text, quotedPhrases: true))
        {
            if (token.Kind == TokenKind.Keyword && token.Key is "since" or "until" or "last")
            {
                removedPrevious = true;
                continue;
            }

            // The whitespace that separated a removed token from what follows goes with it.
            string raw = removedPrevious && token.Kind == TokenKind.Text ? token.Raw.TrimStart() : token.Raw;
            removedPrevious = false;
            rest.Append(raw);
        }

        StringBuilder result = new(rest.ToString().Trim());
        if (since is { } from)
        {
            AppendToken(result, "since:" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (until is { } to)
        {
            AppendToken(result, "until:" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }

    private static void AppendToken(StringBuilder text, string token)
    {
        if (text.Length > 0)
        {
            text.Append(' ');
        }

        text.Append(token);
    }

    private static void TrimEnd(StringBuilder text)
    {
        while (text.Length > 0 && char.IsWhiteSpace(text[^1]))
        {
            text.Length--;
        }
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    // Removing tokens leaves double spaces behind; the summary is what the user would have typed without them.
    private static string CollapseWhitespace(string text)
    {
        StringBuilder result = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = result.Length > 0;
            }
            else
            {
                if (pendingSpace)
                {
                    result.Append(' ');
                    pendingSpace = false;
                }

                result.Append(c);
            }
        }

        return result.ToString();
    }
}
