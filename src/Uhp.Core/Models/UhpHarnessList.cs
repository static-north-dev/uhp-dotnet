using System.Text.Json.Serialization;

namespace Uhp.Core;

public sealed record UhpHarnessList([property: JsonPropertyName("harnesses")] IReadOnlyList<UhpHarness> Harnesses);
