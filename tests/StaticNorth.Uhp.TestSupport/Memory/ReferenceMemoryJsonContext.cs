using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.TestSupport.Memory;

[JsonSerializable(typeof(ReferenceMemoryTextPart))]
internal sealed partial class ReferenceMemoryJsonContext : JsonSerializerContext;
