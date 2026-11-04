using System.Text.Json.Serialization;

namespace Uhp.Core;

public sealed record UhpErrorEnvelope([property: JsonPropertyName("error")] UhpError Error);
