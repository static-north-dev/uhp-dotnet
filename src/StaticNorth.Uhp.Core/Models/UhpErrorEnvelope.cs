using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core;

public sealed record UhpErrorEnvelope([property: JsonPropertyName("error")] UhpError Error);
