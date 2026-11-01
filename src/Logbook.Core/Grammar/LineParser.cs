using System.Globalization;
using System.Text;

namespace Logbook.Core.Grammar;

/// <summary>
/// The one grammar of DESIGN.md section 6, read two ways. In a capture, query-only tokens are summary text;
/// in a query, <c>at:</c> is a bare word. A keyword whose value does not parse is treated as text in both.
/// </summary>
public static class LineParser
{
    /// <summary>
    /// <paramref name="now"/> gives today's date; <paramref name="zone"/> gives the offset an <c>at:</c> time takes,
    /// the one in force at the date and time it names. The App passes the machine's zone; tests pass a fixed one.
    /// </summary>
    public static CaptureLine ParseCapture(string text, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zone);
        MarkedText summary = new();
        List<string> tags = [];
        List<string> people = [];
        DateTimeOffset? occurredAt = null;
        bool previousRemoved = false;

        foreach (Token token in Scanner.Scan(text, quotedPhrases: false))
        {
            bool removed = true;
            switch (token.Kind)
            {
                case TokenKind.Tag:
                    AddUnique(tags, token.Value);
                    break;
                case TokenKind.Person:
                    AddUnique(people, token.Value);
                    break;
                case TokenKind.Keyword when token.Key == "at" && DateValues.TryParseAt(token.Value, now, zone, out DateTimeOffset at):
                    occurredAt = at; // The last at: on the line wins.
                    break;
                case TokenKind.Stripped when previousRemoved:
                    // With the token gone, the space before it would be left in front of its punctuation
                    // ("asked @j-doe, then"), so it goes too: "asked, then".
                    summary.TrimEnd();
                    summary.Append(token.Raw, fromToken: true);
                    removed = false;
                    break;
                case TokenKind.Opening:
                    summary.Append(token.Raw, fromToken: true);
                    removed = false;
                    break;
                default:
                    summary.Append(token.Raw, fromToken: false);
                    removed = false;
                    break;
            }

            previousRemoved = removed;
        }

        return new CaptureLine(string.Join(' ', summary.Words()), occurredAt, tags, people);
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
        // Text since the last token the filter used, so "at:yesterday." stays one word although it scans as two tokens.
        MarkedText plain = new();
        bool previousUsed = false;

        foreach (Token token in Scanner.Scan(text, quotedPhrases: true))
        {
            bool used = true;
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
                case TokenKind.Stripped when previousUsed:
                case TokenKind.Opening:
                    plain.Append(token.Raw, fromToken: true);
                    used = false;
                    break;
                default:
                    plain.Append(token.Raw, fromToken: false);
                    used = false;
                    break;
            }

            // A token the filter used ends the word before it, as whitespace would.
            if (used)
            {
                words.AddRange(plain.Words());
                plain.Clear();
            }

            previousUsed = used;
        }

        words.AddRange(plain.Words());
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
            // Punctuation stripped from a removed token's value ("last:7d,") goes with the token.
            if ((token.Kind == TokenKind.Keyword && token.Key is "since" or "until" or "last") || (removedPrevious && token.Kind == TokenKind.Stripped))
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

    private static void AddUnique(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    /// <summary>
    /// Text left over once tokens are taken out, with a flag per character saying whether it is punctuation a token
    /// left behind (<see cref="TokenKind.Stripped"/> or <see cref="TokenKind.Opening"/>). DESIGN.md section 6 drops
    /// such punctuation when nothing but whitespace is around it: "done (#asked)" stores "done", not "done ()".
    /// </summary>
    private sealed class MarkedText
    {
        private readonly StringBuilder _text = new();
        private readonly List<bool> _fromToken = [];

        public void Append(string text, bool fromToken)
        {
            _text.Append(text);
            _fromToken.AddRange(Enumerable.Repeat(fromToken, text.Length));
        }

        public void TrimEnd()
        {
            while (_text.Length > 0 && char.IsWhiteSpace(_text[^1]))
            {
                _text.Length--;
                _fromToken.RemoveAt(_fromToken.Count - 1);
            }
        }

        public void Clear()
        {
            _text.Clear();
            _fromToken.Clear();
        }

        /// <summary>The whitespace-separated words, leaving out each word made only of punctuation a token left behind.</summary>
        public List<string> Words()
        {
            List<string> words = [];
            int i = 0;
            while (i < _text.Length)
            {
                if (char.IsWhiteSpace(_text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                bool onlyFromToken = true;
                while (i < _text.Length && !char.IsWhiteSpace(_text[i]))
                {
                    onlyFromToken &= _fromToken[i];
                    i++;
                }

                if (!onlyFromToken)
                {
                    words.Add(_text.ToString(start, i - start));
                }
            }

            return words;
        }
    }
}
