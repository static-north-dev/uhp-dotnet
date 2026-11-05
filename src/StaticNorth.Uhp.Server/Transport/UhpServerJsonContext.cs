using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Server.Transport;

[JsonSerializable(typeof(UhpSharedSessionView))]
internal sealed partial class UhpServerJsonContext : JsonSerializerContext;
