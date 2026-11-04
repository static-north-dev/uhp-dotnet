using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed class UhpOutputItem
{
    public UhpOutputItem(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString()))
        {
            throw new UhpProtocolException("An output item requires a string type.");
        }

        Raw = value.Clone();
        Type = type.GetString()!;

        if (Type == "message" && Raw.TryGetProperty("content", out var content) &&
            (content.ValueKind != JsonValueKind.Array || content.EnumerateArray().Any(part => part.ValueKind != JsonValueKind.Object)))
        {
            throw new UhpProtocolException("Message content must be an array of objects.");
        }
    }

    public string Type { get; }
    public JsonElement Raw { get; }
    public string? Id => GetString("id");
    public string? CallId => GetString("call_id");
    public string? Name => GetString("name");
    public string? Arguments => GetString("arguments");
    public string? Output => GetString("output");
    public string? Status => GetString("status");
    public string? Role => GetString("role");

    public string? Text
    {
        get
        {
            if (!Raw.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return string.Concat(content.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String).Select(part => part.GetProperty("text").GetString()));
        }
    }

    public static UhpOutputItem TextMessage(string id, string text) => new(UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("id", id);
        writer.WriteString("role", "assistant");
        writer.WriteString("status", "completed");
        writer.WriteStartArray("content");
        writer.WriteStartObject();
        writer.WriteString("type", "output_text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }));

    private string? GetString(string name) => Raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
