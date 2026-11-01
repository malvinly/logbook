using System.Text.Json;
using System.Text.Json.Serialization;

namespace Logbook.Core.Storage;

// Source-generated so the app can later be published single-file without reflection warnings (DESIGN.md section 16).
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Entry))]
[JsonSerializable(typeof(FileHeader))]
internal sealed partial class LogbookJsonContext : JsonSerializerContext;

/// <summary>JSON for the record and header types. Property names are camelCase; unknown properties are ignored on load.</summary>
public static class LogbookJson
{
    public static string Serialize(Entry entry) => JsonSerializer.Serialize(entry, LogbookJsonContext.Default.Entry);

    public static string Serialize(FileHeader header) => JsonSerializer.Serialize(header, LogbookJsonContext.Default.FileHeader);

    /// <summary>Throws <see cref="JsonException"/> on malformed JSON.</summary>
    public static Entry DeserializeEntry(string json)
    {
        Entry entry = JsonSerializer.Deserialize(json, LogbookJsonContext.Default.Entry) ?? throw new JsonException("Record is null.");
        // A tombstone may omit every field but id, rev and deleted, and a line written before a field existed lacks it;
        // either way a missing property arrives as null.
        return entry with
        {
            Summary = entry.Summary ?? "",
            Tags = entry.Tags ?? [],
            People = entry.People ?? [],
            Refs = entry.Refs ?? [],
            RelatedTo = entry.RelatedTo ?? [],
        };
    }

    /// <summary>Throws <see cref="JsonException"/> on malformed JSON.</summary>
    public static FileHeader DeserializeHeader(string json)
        => JsonSerializer.Deserialize(json, LogbookJsonContext.Default.FileHeader) ?? throw new JsonException("Header is null.");
}
