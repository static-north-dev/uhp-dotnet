using System.Text.Json;

using System.Text.Json.Serialization;
using Uhp.Core.Serialization;

namespace Uhp.Core;

[JsonConverter(typeof(UhpResponseJsonConverter))]
public sealed record UhpResponse(string Id, UhpResponseStatus Status, string Model)
{
    public long CreatedAt { get; init; }
    public IReadOnlyList<UhpOutputItem> Output { get; init; } = [];
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    public string? PreviousResponseId { get; init; }
    public bool? Store { get; init; }
    public UhpError? Error { get; init; }
    public JsonElement? IncompleteDetails { get; init; }
    public UhpUsage? Usage { get; init; }
    public JsonElement Raw { get; internal init; }

    public string? SessionId => Metadata?.GetValueOrDefault("session_id") is { ValueKind: JsonValueKind.String } id ? id.GetString() : null;
}
