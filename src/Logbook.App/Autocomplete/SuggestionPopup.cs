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

    // The token is the run of non-space characters around the caret, when it starts with # or @ at a word start.
    private bool FindToken(out int start, out int end, out char sigil)
    {
        string text = _box.Text;
        int caret = _box.CaretIndex;
        start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        sigil = start < text.Length ? text[start] : '\0';
        return (sigil == '#' || sigil == '@') && (start == 0 || char.IsWhiteSpace(text[start - 1]));
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

        string replacement = _sigil + name + " ";
        _box.Text = string.Concat(_box.Text.AsSpan(0, _tokenStart), replacement, _box.Text.AsSpan(_tokenEnd));
        _box.CaretIndex = _tokenStart + replacement.Length;
        Hide();
    }

    private void Hide() => _popup.IsOpen = false;
}
