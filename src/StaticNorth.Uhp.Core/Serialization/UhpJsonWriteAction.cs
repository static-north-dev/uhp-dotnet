using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core.Serialization;

[JsonConverter(typeof(UhpJsonWriteActionConverter))]
internal readonly record struct UhpJsonWriteAction(Action<Utf8JsonWriter> Write);
