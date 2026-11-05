using System.Text.Json;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

internal static class CodexInteractive
{
    private static readonly JsonElement Schema = JsonSerializer.Deserialize("""
        {"type":"object","properties":{"kind":{"type":"string","enum":["choice","confirm","free_text","secret"]},"prompt":{"type":"string","minLength":1},"options":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"label":{"type":"string"}},"required":["id","label"],"additionalProperties":false}}},"required":["kind","prompt"],"additionalProperties":false}
        """, UhpJsonContext.Default.JsonElement);

    public static void WriteTool(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("type", "function");
        writer.WriteString("name", "ask_user"); writer.WriteString("description", "Ask the person one question and stop until an answer arrives. Use secret for sensitive credentials; its value is delivered to the harness outside the conversation.");
        writer.WritePropertyName("inputSchema"); Schema.WriteTo(writer); writer.WriteEndObject();
        writer.WriteStartObject(); writer.WriteString("type", "function"); writer.WriteString("name", "use_secret");
        writer.WriteString("description", "Consume a harness secret reference and return its SHA-256 fingerprint without revealing its value.");
        writer.WriteStartObject("inputSchema"); writer.WriteString("type", "object"); writer.WriteStartObject("properties");
        writer.WriteStartObject("reference"); writer.WriteString("type", "string"); writer.WriteEndObject(); writer.WriteEndObject();
        writer.WriteStartArray("required"); writer.WriteStringValue("reference"); writer.WriteEndArray(); writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject(); writer.WriteEndObject();
    }

    public static CodexInputQuestion Read(JsonElement parameters)
    {
        var arguments = parameters.GetProperty("arguments");
        var kind = arguments
            .GetProperty("kind")
            .GetString()!;
        var prompt = arguments
            .GetProperty("prompt")
            .GetString()!;

        if (kind is not ("choice" or "confirm" or "free_text" or "secret") || string.IsNullOrWhiteSpace(prompt) ||
            kind == "choice" && (!arguments.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array || options.GetArrayLength() < 2))
        {
            throw new UhpProtocolException("Invalid ask_user arguments.");
        }

        var id = $"inreq_{Guid.NewGuid():N}";
        var call = new UhpOutputItem(UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("type", "function_call"); writer.WriteString("id", $"fc_{Guid.NewGuid():N}");
            writer.WriteString("call_id", parameters
                .GetProperty("callId")
                .GetString()); writer.WriteString("name", "ask_user");
            writer.WriteString("arguments", arguments.GetRawText()); writer.WriteString("status", "completed"); writer.WriteEndObject();
        }));
        var question = new UhpOutputItem(UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("type", "input_request"); writer.WriteString("id", id);
            writer.WriteString("kind", kind); writer.WriteString("prompt", prompt); writer.WriteString("status", "completed");

            if (arguments.TryGetProperty("options", out var choices))
            {
                writer.WritePropertyName("options"); choices.WriteTo(writer);
            }

            writer.WriteEndObject();
        }));
        return new(prompt, kind, call, question);
    }

    public static JsonElement ToolResult(string? result) => ToolResult(result ?? "The secret reference is unavailable.", result is not null);

    public static JsonElement ToolResult(string result, bool success) => UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject(); writer.WriteBoolean("success", success); writer.WriteStartArray("contentItems");
        writer.WriteStartObject(); writer.WriteString("type", "inputText");
        writer.WriteString("text", result);
        writer.WriteEndObject(); writer.WriteEndArray(); writer.WriteEndObject();
    });
}
