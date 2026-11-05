using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core.Serialization;

[JsonConverter(typeof(UhpRequestPayloadJsonConverter))]
internal readonly record struct UhpRequestPayload(UhpTaskRequest Request, bool Stream, bool Background = false);
