using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soluna.Shared;

/// <summary>Content the editors change, and how it is written back to the game's files.</summary>
public enum ContentKind : byte
{
    Items = 1,
    Npcs = 2,
}

public static class ContentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Never drop zeros: a property left out reads back as the class's default, which is not always 0
        // (an NPC's attack defaults to 4), so a save would quietly change what nobody edited.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A JSON array with one entry per line, as the hand-made files were, so changes read well in a diff.</summary>
    public static string Array<T>(IEnumerable<T> entries) =>
        "[\n" + string.Join(",\n", entries.Select(e => "  " + JsonSerializer.Serialize(e, Options))) + "\n]\n";
}
