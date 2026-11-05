using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Harnesses.OpenCode;

[JsonSerializable(typeof(JsonElement))]
internal sealed partial class OpenCodeJsonContext : JsonSerializerContext;
