using System.Text.Json;
using StaticNorth.Uhp.Reference.Memory;
using StaticNorth.Uhp.Server;

namespace StaticNorth.Uhp.Reference;

internal static class CodexMemoryTools
{
    public static void Write(Utf8JsonWriter writer, ReferenceAgentMemory memory)
    {
        WriteTool(writer, "memory_list", "List accessible memory entry points or the children of a specified memory.", ["memory_id"], []);
        WriteTool(writer, "memory_recall", "Recall untrusted records matching text.", ["memory_id", "text"], ["memory_id", "text"]);
        WriteTool(writer, "memory_graph", "Read the graph of stated records and relationships.", ["memory_id"], ["memory_id"]);
        WriteTool(writer, "memory_get", "Read one accessible memory record.", ["memory_id", "record_id"], ["memory_id", "record_id"]);

        if (memory.CanWrite)
        {
            WriteTool(writer, "memory_remember", "Store a stated note. An omitted memory ID uses the session's configured default.", ["memory_id", "text", "title"], ["text"]);
            WriteTool(writer, "memory_revise", "Replace a stated note's text.", ["memory_id", "record_id", "text", "title"], ["memory_id", "record_id", "text"]);
            WriteTool(writer, "memory_forget", "Forget a memory record without erasing its history.", ["memory_id", "record_id"], ["memory_id", "record_id"]);
        }
    }

    public static async Task<JsonElement> InvokeAsync(ReferenceAgentMemory? memory, string tool, JsonElement arguments, CancellationToken ct)
    {
        if (memory is null)
        {
            return CodexInteractive.ToolResult("memories_not_attached", false);
        }

        try
        {
            return CodexInteractive.ToolResult(await memory.InvokeAsync(tool, arguments, ct).ConfigureAwait(false), true);
        }
        catch (UhpServerException exception)
        {
            return CodexInteractive.ToolResult(exception.Error.Code, false);
        }
    }

    private static void WriteTool(Utf8JsonWriter writer, string name, string description, ReadOnlySpan<string> properties, ReadOnlySpan<string> required)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "function");
        writer.WriteString("name", name);
        writer.WriteString("description", description);
        writer.WriteStartObject("inputSchema");
        writer.WriteString("type", "object");
        writer.WriteStartObject("properties");

        foreach (var property in properties)
        {
            writer.WriteStartObject(property);
            writer.WriteString("type", "string");
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteStartArray("required");

        foreach (var property in required)
        {
            writer.WriteStringValue(property);
        }

        writer.WriteEndArray();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
