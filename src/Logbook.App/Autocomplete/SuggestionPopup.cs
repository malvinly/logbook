using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Logbook.App.Chips;

namespace Logbook.App.Autocomplete;

/// <summary>
/// Attaches a suggestion popup to a text box. When the caret sits in a <c>#</c> or <c>@</c> token, the popup lists
/// matching names; Up/Down move, Enter or Tab accept, Escape closes. Plain event handlers, no behaviors package.
/// </summary>
public sealed class SuggestionPopup
{
    private static readonly string[] Keywords = ["at", "since", "until", "last"];

    private readonly TextBox _box;
    private readonly Func<Suggestions> _suggestions;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private int _tokenStart = -1;
    private int _tokenEnd = -1;
    private char _sigil;

    private SuggestionPopup(TextBox box, Func<Suggestions> suggestions)
    {
        _box = box;
        _suggestions = suggestions;
        // No background of its own: the list sits on the flyout surface, which carries the theme colors and fonts.
        _list = new ListBox { MaxHeight = 220, MinWidth = 200, Background = null };
        _list.PreviewMouseLeftButtonUp += (_, _) => Accept();
        Border surface = new() { Child = _list };
        surface.SetResourceReference(FrameworkElement.StyleProperty, "Flyout");
        _popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true,
            Child = surface,
        };
        box.TextChanged += (_, _) => Refresh();
        box.SelectionChanged += (_, _) => Refresh();
        box.PreviewKeyDown += OnPreviewKeyDown;
        box.LostKeyboardFocus += (_, _) => Hide();
    }

    /// <summary>True while the list is showing, so a window-level key handler can leave Enter and Escape to it.</summary>
    public bool IsOpen => _popup.IsOpen;

    /// <param name="box">The text box to watch.</param>
    /// <param name="suggestions">Called each time the list is refreshed, so a rebuilt index is picked up.</param>
    public static SuggestionPopup Attach(TextBox box, Func<Suggestions> suggestions)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(suggestions);
        return new SuggestionPopup(box, suggestions);
    }

    private void Refresh()
    {
        if (!_box.IsKeyboardFocused || !FindToken(out int start, out int end, out char sigil))
        {
            Hide();
            return;
        }

        string prefix = _box.Text[(start + 1)..end];
        IReadOnlyList<string> names = _suggestions().For(sigil == '@' ? ChipKind.Person : ChipKind.Tag, prefix);
        if (names.Count == 0 || (names.Count == 1 && string.Equals(names[0], prefix, StringComparison.Ordinal)))
        {
            Hide();
            return;
        }

        _tokenStart = start;
        _tokenEnd = end;
        _sigil = sigil;
        _list.ItemsSource = names;
        _list.SelectedIndex = 0;
        _popup.IsOpen = true;
    }

    // The token is the run around the caret up to whitespace, "," or ";", when it starts with # or @ at a word start
    // (or just after an opening paren or quote, or after a "," or ";" that ended another token). Its end leaves out
    // trailing punctuation, which the grammar strips (DESIGN.md section 6), so "@j-d?" suggests for "j-d" and keeps the "?".
    // It suggests only while the caret is inside the name: after "asked @j-doe." the popup hides, because an Enter
    // typed there is meant to submit the line and must not accept a suggestion.
    private bool FindToken(out int start, out int end, out char sigil)
    {
        string text = _box.Text;
        int caret = _box.CaretIndex;
        start = caret;
        while (start > 0 && !IsTokenEnd(text[start - 1]))
        {
            start--;
        }

        end = caret;
        while (end < text.Length && !IsTokenEnd(text[end]))
        {
            end++;
        }

        // As in the grammar, a sigil may follow an opening paren or quote: "(#as" suggests tags too.
        while (start < end && text[start] is '(' or '"')
        {
            start++;
        }

        end = StripTrailing(text, start, end);
        sigil = start < text.Length ? text[start] : '\0';
        bool mayStart = start == 0 || char.IsWhiteSpace(text[start - 1]) || text[start - 1] is '(' or '"' || FollowsTokenSeparator(text, start);
        return (sigil == '#' || sigil == '@') && mayStart && caret <= end;
    }

    // The end once the trailing punctuation the grammar strips is left out, keeping at least the character at start
    // (the sigil, or a keyword's colon).
    private static int StripTrailing(string text, int start, int end)
    {
        while (end > start + 1)
        {
            if (text[end - 1] is ',' or '.' or ';' or ':' or ')' or '"' or '?' or '!')
            {
                end--;
            }
            else if (end - 2 > start && text[end - 2] == '\'' && text[end - 1] == 's')
            {
                end -= 2; // a possessive 's
            }
            else
            {
                break;
            }
        }

        return end;
    }

    private static bool IsTokenEnd(char c) => char.IsWhiteSpace(c) || c is ',' or ';';

    // "@j-doe,@a" suggests for "a": the comma ended a token. This only checks that the piece before the comma starts
    // with # or @ and a letter, as the grammar requires ("ticket #12345,#a" suggests nothing); the parser has the final
    // say on longer chains.
    private static bool FollowsTokenSeparator(string text, int start)
    {
        if (start == 0 || text[start - 1] is not (',' or ';'))
        {
            return false;
        }

        // "last:7d,#pr" suggests too: a keyword value ends at a separator before a tag or person. The value may hold
        // commas of its own ("at:yesterday,3pm,#pr"), so the keyword is looked for at the start of the whole word.
        int wordStart = start - 1;
        while (wordStart > 0 && !char.IsWhiteSpace(text[wordStart - 1]))
        {
            wordStart--;
        }

        foreach (string keyword in Keywords)
        {
            int colon = wordStart + keyword.Length;
            if (text.AsSpan(wordStart).StartsWith(keyword + ":", StringComparison.Ordinal) && StripTrailing(text, colon, start - 1) > colon + 1)
            {
                return true;
            }
        }

        int previous = start - 1;
        while (previous > 0 && !IsTokenEnd(text[previous - 1]))
        {
            previous--;
        }

        while (previous < start - 1 && text[previous] is '(' or '"')
        {
            previous++;
        }

        return text[previous] is '#' or '@' && char.IsLetter(text[previous + 1]);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void Accept()
    {
        if (_list.SelectedItem is not string name)
        {
            return;
        }

        // No space when punctuation or another token already follows: "@j-d?" becomes "@j-doe?", not "@j-doe ?".
        bool followedByText = _tokenEnd < _box.Text.Length && !char.IsWhiteSpace(_box.Text[_tokenEnd]);
        string replacement = _sigil + name + (followedByText ? "" : " ");
        _box.Text = string.Concat(_box.Text.AsSpan(0, _tokenStart), replacement, _box.Text.AsSpan(_tokenEnd));
        _box.CaretIndex = _tokenStart + replacement.Length;
        Hide();
    }

    private void Hide() => _popup.IsOpen = false;
}
