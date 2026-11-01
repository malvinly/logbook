using System.Text.Json.Serialization;

namespace Logbook.App.Settings;

/// <summary>The export toggles, remembered between uses (DESIGN.md section 11).</summary>
public sealed class ExportSettings
{
    public bool ShowGaps { get; set; }

    public bool GroupByMonth { get; set; }

    public bool Compact { get; set; }
}

/// <summary>
/// The plain JSON settings file beside the data file (DESIGN.md section 12). No secrets, no absolute user paths.
/// Mutable because the settings window edits it in place and then saves.
/// </summary>
public sealed class Settings
{
    public const string FileName = "logbook.settings.json";

    /// <summary>Environment variable whose value is the synced root folder.</summary>
    public string SyncRootEnvVar { get; set; } = "";

    public string DataFileRelativePath { get; set; } = "logbook.jsonl";

    public string CaptureHotkey { get; set; } = "Ctrl+Alt+L";

    public string MainWindowHotkey { get; set; } = "Ctrl+Alt+Shift+L";

    public int AutocompleteCap { get; set; } = 8;

    /// <summary><c>recent</c> or <c>frequent</c>.</summary>
    public string AutocompleteRanking { get; set; } = "recent";

    public ExportSettings Export { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
