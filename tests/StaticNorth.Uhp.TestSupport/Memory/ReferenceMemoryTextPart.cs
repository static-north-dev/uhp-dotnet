using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.TestSupport.Memory;

internal readonly record struct ReferenceMemoryTextPart([property: JsonPropertyName(ReferenceMemoryValues.Text)] string? Text)
{
    [JsonPropertyName(ReferenceMemoryValues.Type)]
    public string Type => ReferenceMemoryValues.Text;
}
