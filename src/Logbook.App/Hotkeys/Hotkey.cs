using System.Windows.Input;

namespace Logbook.App.Hotkeys;

/// <summary>A key chord as stored in settings, for example <c>Ctrl+Alt+L</c>.</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (key != Key.None || !TryParseKey(part, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        // A global hotkey without a modifier would steal an ordinary key from every app.
        if (key == Key.None || modifiers == ModifierKeys.None)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    // Enum.TryParse also takes numbers and comma lists: "1" would be (Key)1, which is Key.Cancel, and it registers
    // fine and never fires; a silently dead hotkey is what DESIGN.md section 12 forbids. So a single digit means that
    // digit key, and anything else must be a name of a defined key: letters first, then letters or digits (F12, D1).
    private static bool TryParseKey(string part, out Key key)
    {
        key = Key.None;
        if (part.Length == 1 && char.IsAsciiDigit(part[0]))
        {
            key = Key.D0 + (part[0] - '0');
            return true;
        }

        if (!char.IsAsciiLetter(part[0]) || !part.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        return Enum.TryParse(part, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    public override string ToString()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
