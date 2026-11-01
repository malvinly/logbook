using System.Windows;
using System.Windows.Input;
using Logbook.App.Autocomplete;
using Logbook.App.Chips;
using Logbook.App.Interop;
using Logbook.Core;
using Logbook.Core.Grammar;

namespace Logbook.App.Capture;

/// <summary>
/// The launcher-style capture box (DESIGN.md section 5). The line is parsed on every keystroke and the recognised
/// tokens show as chips, so what will be stored is visible before Enter. The window remembers nothing: a follow-up's
/// pre-fill comes from the explicit Follow up button and lives only as long as this window, so Escape drops it.
/// </summary>
public sealed partial class CaptureWindow : Window
{
    private readonly Rect _workArea;
    private readonly SuggestionPopup[] _popups;
    private readonly Entry? _followUpOf;

    /// <param name="workArea">The monitor work area to centre on.</param>
    /// <param name="suggestions">Autocomplete source.</param>
    /// <param name="followUpOf">The entry this one follows up on, from the main window's Follow up button; null for a plain capture.</param>
    public CaptureWindow(Rect workArea, Func<Suggestions> suggestions, Entry? followUpOf = null)
    {
        InitializeComponent();
        _workArea = workArea;
        _popups = [SuggestionPopup.Attach(Line, suggestions), SuggestionPopup.Attach(ExtraTags, suggestions), SuggestionPopup.Attach(ExtraPeople, suggestions)];
        Backdrop.UseAcrylic(this);
        // Centred and high, as a launcher sits; the box grows downwards when "more" opens.
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + workArea.Height * 0.25;
        _followUpOf = followUpOf;
        if (followUpOf is not null)
        {
            PrefillFollowUp(followUpOf);
        }

        Loaded += (_, _) => Line.Focus();
    }

    /// <summary>The work area the box was shown on, so the toast can land on the same monitor.</summary>
    public Rect WorkArea => _workArea;

    /// <summary>Raised once with the new entry when the user confirms. The window closes itself afterwards.</summary>
    public event Action<Entry>? Captured;

    // The original's tags and people go into the line as typed tokens, so the chips show them and they stay editable.
    // No tag is added for the thread itself: relatedTo carries it (DESIGN.md section 7).
    private void PrefillFollowUp(Entry original)
    {
        List<string> tokens = [];
        List<string> extraTags = [];
        List<string> extraPeople = [];
        foreach (string tag in original.Tags)
        {
            // A stored name that would not parse back as a token (one starting with a digit, say) goes to the "more" box.
            if (ParsesBackAs("#" + tag, tag, line => line.Tags))
            {
                tokens.Add("#" + tag);
            }
            else
            {
                extraTags.Add(tag);
            }
        }

        foreach (string person in original.People)
        {
            if (ParsesBackAs("@" + person, person, line => line.People))
            {
                tokens.Add("@" + person);
            }
            else
            {
                extraPeople.Add(person);
            }
        }

        // A trailing space so typing starts a new word rather than extending the last token.
        Line.Text = tokens.Count > 0 ? string.Join(' ', tokens) + " " : "";
        Line.CaretIndex = Line.Text.Length;
        ExtraTags.Text = string.Join(' ', extraTags);
        ExtraPeople.Text = string.Join(' ', extraPeople);
        if (extraTags.Count > 0 || extraPeople.Count > 0)
        {
            ToggleMore();
        }

        FollowUpNote.Text = $"Follows up on: {Format.DateTime(original.OccurredAt)} — {original.Summary}";
        FollowUpNote.Visibility = Visibility.Visible;
    }

    private static bool ParsesBackAs(string token, string name, Func<CaptureLine, IReadOnlyList<string>> pick)
    {
        IReadOnlyList<string> names = pick(LineParser.ParseCapture(token, DateTimeOffset.Now));
        return names.Count == 1 && names[0] == name;
    }

    private void OnLineChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Placeholder.Visibility = Line.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Chips.ItemsSource = Chip.For(LineParser.ParseCapture(Line.Text, DateTimeOffset.Now));
        Problem.Visibility = Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Preview events reach the window before the text box. While a suggestion list is open, Enter accepts a
        // name and Escape closes the list; the window must not submit or close on top of that.
        if (_popups.Any(p => p.IsOpen))
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ToggleMore();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && ReferenceEquals(e.OriginalSource, Line))
        {
            e.Handled = true;
            Submit();
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e) => ToggleMore();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnLogClick(object sender, RoutedEventArgs e) => Submit();

    private void ToggleMore()
    {
        bool open = More.Visibility != Visibility.Visible;
        More.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MoreText.Text = open ? "Less" : "More";
        MoreGlyph.Text = open ? "\uE70E" : "\uE70D"; // chevron up, chevron down
        if (open)
        {
            Reason.Focus();
        }
        else
        {
            Line.Focus();
        }
    }

    private void Submit()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        CaptureLine line = LineParser.ParseCapture(Line.Text, now);
        if (line.Summary.Length == 0)
        {
            Problem.Text = "Type what happened; tags and people alone are not an entry.";
            Problem.Visibility = Visibility.Visible;
            Line.Focus();
            return;
        }

        Entry entry = new(
            Ulid.Generate(now),
            1,
            line.OccurredAt ?? now,
            now,
            line.Summary,
            BlankToNull(Reason.Text),
            TokenNormalizer.NormalizeList(ExtraTags.Text, existing: line.Tags),
            TokenNormalizer.NormalizeList(ExtraPeople.Text, existing: line.People),
            Lines(Refs.Text),
            _followUpOf is null ? [] : [_followUpOf.Id],
            false);

        Captured?.Invoke(entry);
        Close();
    }

    private static string? BlankToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static IReadOnlyList<string> Lines(string text)
        => [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
