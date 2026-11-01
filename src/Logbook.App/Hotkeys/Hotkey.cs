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
                    if (key != Key.None || !Enum.TryParse(part, ignoreCase: true, out key) || key == Key.None)
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
