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
                case TokenKind.Separator:
                    break; // Between two tokens that are both gone, so it would stand alone.
                case TokenKind.Stripped when previousRemoved && token.Value.Length == 0:
                    break; // Only a possessive 's, which went with the token: "told @j-doe's manager" stores "told manager".
                case TokenKind.Stripped when previousRemoved:
                    // With the token gone, the space before it would be left in front of its punctuation
                    // ("asked @j-doe, then"), so it goes too: "asked, then".
                    summary.TrimEnd();
                    summary.Append(token.Value, Origin.Left);
                    removed = false;
                    break;
                case TokenKind.Opening:
                    summary.Append(token.Raw, Origin.Opener);
                    removed = false;
                    break;
                default:
                    summary.Append(token.Raw, Origin.Typed);
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
                case TokenKind.Separator:
                    break; // Between two tokens, so the filter needs no literal "," for it.
                case TokenKind.Stripped when previousUsed:
                    plain.Append(token.Value, Origin.Left);
                    used = false;
                    break;
                case TokenKind.Opening:
                    // A tag or person always follows and ends the word, so the opener never finds its partner and goes.
                    plain.Append(token.Raw, Origin.Opener);
                    used = false;
                    break;
                default:
                    plain.Append(token.Raw, Origin.Typed);
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
            // Punctuation stripped from a removed token's value ("last:7d,") goes with the token, and so does the
            // separator after it ("last:7d,#printer"), which would otherwise be left in front of the tag.
            if ((token.Kind == TokenKind.Keyword && token.Key is "since" or "until" or "last") || (removedPrevious && token.Kind is TokenKind.Stripped or TokenKind.Separator))
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

    /// <summary>Where a character of <see cref="MarkedText"/> came from.</summary>
    private enum Origin
    {
        /// <summary>Typed by the user as text.</summary>
        Typed,
        /// <summary>Punctuation stripped from the end of a token (<see cref="TokenKind.Stripped"/>).</summary>
        Left,
        /// <summary>The <c>(</c> or <c>"</c> that let a token open (<see cref="TokenKind.Opening"/>).</summary>
        Opener,
    }

    /// <summary>
    /// Text left over once tokens are taken out, with the origin of each character. DESIGN.md section 6 drops punctuation
    /// a token left behind when nothing but whitespace is around it: "done (#asked)" stores "done", not "done ()". An
    /// opener is the exception: it stays when its closing partner stays, and then attaches to the word after it, so
    /// "done (#asked and more)" stores "done (and more)".
    /// </summary>
    private sealed class MarkedText
    {
        private readonly StringBuilder _text = new();
        private readonly List<Origin> _origin = [];

        public void Append(string text, Origin origin)
        {
            _text.Append(text);
            _origin.AddRange(Enumerable.Repeat(origin, text.Length));
        }

        public void TrimEnd()
        {
            while (_text.Length > 0 && char.IsWhiteSpace(_text[^1]))
            {
                _text.Length--;
                _origin.RemoveAt(_origin.Count - 1);
            }
        }

        public void Clear()
        {
            _text.Clear();
            _origin.Clear();
        }

        /// <summary>
        /// The whitespace-separated words, leaving out each word with nothing the user typed in it, except for an opener
        /// whose partner stays.
        /// </summary>
        public List<string> Words()
        {
            string text = _text.ToString();

            // First the words, and for each character the word it is in (-1 for whitespace).
            List<(int Start, int End, bool Typed)> spans = [];
            int[] wordAt = new int[text.Length];
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    wordAt[i] = -1;
                    i++;
                    continue;
                }

                int start = i;
                bool typed = false;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    typed |= _origin[i] == Origin.Typed;
                    wordAt[i] = spans.Count;
                    i++;
                }

                spans.Add((start, i, typed));
            }

            List<string> words = [];
            // An opener that stays at the end of its word waits here to be put in front of the next word.
            string pending = "";
            foreach ((int start, int end, bool typed) in spans)
            {
                string word = typed ? text[start..end] : SurvivingOpeners(text, start, end, spans, wordAt);
                if (word.Length == 0)
                {
                    continue;
                }

                word = pending + word;
                pending = "";
                if (!typed || _origin[end - 1] == Origin.Opener)
                {
                    pending = word;
                    continue;
                }

                words.Add(word);
            }

            if (pending.Length > 0)
            {
                words.Add(pending);
            }

            return words;
        }

        // From a word with nothing typed in it, the openers whose closing partner (the next ")" for "(", the next quote
        // for a quote) is in a word that stays. With no partner at all, the opener goes.
        private string SurvivingOpeners(string text, int start, int end, List<(int Start, int End, bool Typed)> spans, int[] wordAt)
        {
            StringBuilder kept = new();
            for (int i = start; i < end; i++)
            {
                if (_origin[i] != Origin.Opener)
                {
                    continue;
                }

                int partner = text.IndexOf(text[i] == '(' ? ')' : '"', i + 1);
                if (partner >= 0 && spans[wordAt[partner]].Typed)
                {
                    kept.Append(text[i]);
                }
            }

            return kept.ToString();
        }
    }
}
