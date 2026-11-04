using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core;

public sealed record UhpHarnessList([property: JsonPropertyName("harnesses")] IReadOnlyList<UhpHarness> Harnesses);
