using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uhp.Core;

[JsonSerializable(typeof(UhpDiscovery))]
[JsonSerializable(typeof(UhpHarness))]
[JsonSerializable(typeof(UhpHarnessList))]
[JsonSerializable(typeof(UhpModelCatalog))]
[JsonSerializable(typeof(UhpHarnessModels))]
[JsonSerializable(typeof(UhpResponse))]
[JsonSerializable(typeof(UhpSessionCancelResult))]
[JsonSerializable(typeof(UhpErrorEnvelope))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class UhpJsonContext : JsonSerializerContext;
