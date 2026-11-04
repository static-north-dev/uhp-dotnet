using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpTaskRequest
{
    public UhpTaskRequest(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        Input = UhpJson.CreateElement(writer => writer.WriteStringValue(input));
    }

    public UhpTaskRequest(IReadOnlyList<UhpUserTextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one user text message is required.", nameof(messages));
        }

        Input = UhpJson.CreateElement(writer =>
        {
            writer.WriteStartArray();

            foreach (var message in messages)
            {
                ArgumentNullException.ThrowIfNull(message);
                ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteStartArray("content");
                writer.WriteStartObject();
                writer.WriteString("type", "input_text");
                writer.WriteString("text", message.Text);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    internal UhpTaskRequest(JsonElement input) => Input = input.Clone();

    public JsonElement Input { get; }
    public string? HarnessId { get; init; }
    public string? Model { get; init; }
    public string? PreviousResponseId { get; init; }
    public string? Instructions { get; init; }
    public bool? Store { get; init; }
    public long? MaxStep { get; init; }
    public long? MaxOutputTokens { get; init; }
    public long? TimeoutSeconds { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    public string? IdempotencyKey { get; init; }
}
