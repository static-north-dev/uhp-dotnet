using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Client.InteropTests;

[JsonSerializable(typeof(InteropReport))]
internal sealed partial class InteropJsonContext : JsonSerializerContext;
