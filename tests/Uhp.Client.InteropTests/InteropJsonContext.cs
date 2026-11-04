using System.Text.Json.Serialization;

namespace Uhp.Client.InteropTests;

[JsonSerializable(typeof(InteropReport))]
internal sealed partial class InteropJsonContext : JsonSerializerContext;
