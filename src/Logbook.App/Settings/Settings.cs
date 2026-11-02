using System.Text.Json.Serialization;

namespace Logbook.App.Settings;

/// <summary>The export toggles, remembered between uses (docs/export.md).</summary>
public sealed class ExportSettings
{
    public bool ShowGaps { get; set; }

    public bool GroupByMonth { get; set; }

    public bool Compact { get; set; }

    public bool TagsAndPeople { get; set; }
}

/// <summary>A filter string saved under a name, shown as a button above the results (docs/main-window.md).</summary>
public sealed class SavedQuery
{
    public string Name { get; set; } = "";

    /// <summary>The filter text exactly as typed; clicking the button puts it back into the filter bar.</summary>
    public string Filter { get; set; } = "";
}

/// <summary>
/// The plain JSON settings file beside the data file (docs/settings.md). No secrets, no absolute user paths.
/// Mutable because the settings window edits it in place and then saves.
/// </summary>
public sealed class Settings
{
    public const string FileName = "logbook.settings.json";

    public string CaptureHotkey { get; set; } = "Ctrl+Alt+L";

    public string MainWindowHotkey { get; set; } = "Ctrl+Alt+Shift+L";

    public int AutocompleteCap { get; set; } = 8;

    /// <summary><c>recent</c> or <c>frequent</c>.</summary>
    public string AutocompleteRanking { get; set; } = "recent";

    /// <summary>
    /// Lock after this many minutes without any input to Windows (docs/locking.md); 0 never locks. A settings
    /// file written before this existed has no such property and gets the default.
    /// </summary>
    public int IdleLockMinutes { get; set; } = 60;

    /// <summary>
    /// <c>system</c> follows the Windows light or dark setting; <c>light</c> or <c>dark</c> forces one. A settings file
    /// written before this existed has no such property and follows Windows.
    /// </summary>
    public string Theme { get; set; } = "system";

    public ExportSettings Export { get; set; } = new();

    /// <summary>In the order they were saved, which is the order of the buttons.</summary>
    public List<SavedQuery> SavedQueries { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
