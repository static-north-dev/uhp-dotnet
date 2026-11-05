using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

public static class UhpJson
{
    public static Task WriteAsync(Stream stream, JsonElement value, CancellationToken ct = default) => JsonSerializer.SerializeAsync(stream, value, WireJsonContext.Default.JsonElement, ct);

    public static JsonElement CreateElement(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        return JsonSerializer.SerializeToElement(new UhpJsonWriteAction(write), WireJsonContext.Default.UhpJsonWriteAction);
    }

    public static UhpDiscovery ParseDiscovery(JsonElement value)
    {
        value = value.Clone();
        var wire = Read(value, WireJsonContext.Default.DiscoveryWire);

        if (wire.Object != UhpProtocol.DiscoveryObject || wire.Protocol != UhpProtocol.Name || wire.Versions.Length == 0 || wire.Versions.Any(string.IsNullOrWhiteSpace) || !wire.Versions.Contains(wire.DefaultVersion, StringComparer.Ordinal))
        {
            throw new UhpProtocolException("Invalid UHP discovery document.");
        }

        var capabilities = new Dictionary<string, bool>(StringComparer.Ordinal);
        var flags = value.GetProperty(UhpJsonFields.Capabilities);

        foreach (var flag in flags.EnumerateObject())
        {
            if (flag.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new UhpProtocolException("Discovery capabilities must be booleans.");
            }

            capabilities.Add(flag.Name, flag.Value.GetBoolean());
        }

        return new UhpDiscovery(Array.AsReadOnly(wire.Versions), wire.DefaultVersion, wire.ConformanceClass, new UhpCapabilities(capabilities))
        {
            ImplementationName = wire.Implementation?.Name,
            ImplementationVersion = wire.Implementation?.Version,
            PluginSchemas = wire.PluginSchemas is { } schemas ? Array.AsReadOnly(schemas) : [],
            Raw = value
        };
    }

    public static UhpHarness ParseHarness(JsonElement value)
    {
        value = value.Clone();
        var wire = Read(value, WireJsonContext.Default.HarnessWire);
        RequireText(wire.Id, "harness.id");

        return new UhpHarness(wire.Id, wire.Name, wire.Base)
        {
            BaseLabel = wire.BaseLabel,
            DefaultModel = wire.DefaultModel,
            SystemPrompt = wire.SystemPrompt,
            MaxStep = wire.MaxStep,
            TimeoutSeconds = wire.TimeoutSeconds,
            CreatedAt = wire.CreatedAt,
            Raw = value
        };
    }

    public static IReadOnlyList<UhpHarness> ParseHarnessList(JsonElement value)
    {
        value = value.Clone();

        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("harnesses", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new UhpProtocolException("Expected a harnesses array.");
        }

        var harnesses = new UhpHarness[list.GetArrayLength()];

        for (var index = 0; index < harnesses.Length; index++)
        {
            harnesses[index] = ParseHarness(list[index]);
        }

        return Array.AsReadOnly(harnesses);
    }

    public static UhpModelCatalog ParseModelCatalog(JsonElement value)
    {
        value = value.Clone();
        var wire = Read(value, WireJsonContext.Default.ModelCatalogWire);

        return new UhpModelCatalog(wire.Backends.ToFrozenDictionary(pair => pair.Key, pair => pair.Value is null ? throw new UhpProtocolException("A model backend cannot be null.") : new UhpModelBackend(pair.Value.Default, Array.AsReadOnly(pair.Value.Models.Select(FromModel).ToArray())), StringComparer.Ordinal))
        {
            Raw = value
        };
    }

    public static UhpHarnessModels ParseHarnessModels(JsonElement value)
    {
        value = value.Clone();
        var wire = Read(value, WireJsonContext.Default.HarnessModelsWire);

        return new UhpHarnessModels(Array.AsReadOnly(wire.Models.Select(FromModel).ToArray()))
        {
            HarnessId = wire.HarnessId,
            Backend = wire.Backend,
            Default = wire.Default,
            Fallback = wire.Fallback,
            Raw = value
        };
    }

    public static UhpResponse ParseResponse(JsonElement value)
    {
        value = value.Clone();
        var wire = Read(value, WireJsonContext.Default.ResponseWire);
        RequireText(wire.Id, "response.id");
        RequireText(wire.Status, "response.status");

        if (wire.Object != UhpProtocol.ResponseObject)
        {
            throw new UhpProtocolException("Expected a response object.");
        }

        return new UhpResponse(wire.Id, new UhpResponseStatus(wire.Status), wire.Model)
        {
            CreatedAt = wire.CreatedAt,
            Output = Array.AsReadOnly(value.GetProperty(UhpJsonFields.Output).EnumerateArray().Select(item => new UhpOutputItem(item)).ToArray()),
            Metadata = Own(wire.Metadata),
            PreviousResponseId = wire.PreviousResponseId,
            Store = wire.Store,
            Error = wire.Error is null ? null : FromError(wire.Error),
            IncompleteDetails = value.TryGetProperty(UhpJsonFields.IncompleteDetails, out var details) && details.ValueKind != JsonValueKind.Null ? details.Clone() : null,
            Usage = wire.Usage is null ? null : new UhpUsage
            {
                InputTokens = wire.Usage.InputTokens,
                OutputTokens = wire.Usage.OutputTokens,
                TotalTokens = wire.Usage.TotalTokens,
                CacheReadTokens = wire.Usage.CacheReadTokens,
                CacheWriteTokens = wire.Usage.CacheWriteTokens,
                Raw = value.GetProperty(UhpJsonFields.Usage).Clone()
            },
            Raw = value
        };
    }

    public static UhpError ParseError(JsonElement value) => FromError(Read(value, WireJsonContext.Default.ErrorEnvelopeWire).Error);
    public static UhpError ParseErrorValue(JsonElement value) => FromError(Read(value, WireJsonContext.Default.ErrorWire));
    public static JsonElement SerializeErrorValue(UhpError error) => JsonSerializer.SerializeToElement(error, UhpJsonContext.Default.UhpError);
    public static UhpSessionCancelResult ParseSessionCancelResult(JsonElement value)
    {
        value = value.Clone();

        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(UhpJsonFields.Id, out var id) || id.ValueKind != JsonValueKind.String || !value.TryGetProperty(UhpJsonFields.Status, out var status) || status.ValueKind != JsonValueKind.String)
        {
            throw new UhpProtocolException("Invalid session cancellation acknowledgement.");
        }

        RequireText(id.GetString(), "session.id");
        RequireText(status.GetString(), "session.status");

        return new UhpSessionCancelResult(id.GetString()!, status.GetString()!)
        {
            Raw = value
        };
    }

    public static JsonElement SerializeSessionCancelResult(UhpSessionCancelResult result) => JsonSerializer.SerializeToElement(result, UhpJsonContext.Default.UhpSessionCancelResult);
    public static JsonElement SerializeRequest(UhpTaskRequest request, bool stream) => SerializeRequest(request, stream, false);

    public static JsonElement SerializeRequest(UhpTaskRequest request, bool stream, bool background)
    {
        ArgumentNullException.ThrowIfNull(request);
        UhpInputValidation.ValidateContinuation(request.InputFeatures, request.PreviousResponseId);

        if (request.MaxStep < 0 || request.MaxOutputTokens < 0 || request.TimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Task budgets cannot be negative.");
        }

        return JsonSerializer.SerializeToElement(new UhpRequestPayload(request, stream, background), WireJsonContext.Default.UhpRequestPayload);
    }

    public static UhpTaskRequest ParseRequest(JsonElement value, string? idempotencyKey = null)
    {
        value = value.Clone();
        var wire = Read(value, WireJsonContext.Default.CreateResponseRequestWire);
        var request = new UhpTaskRequest(wire.Input)
        {
            Model = wire.Model,
            Metadata = Own(wire.Metadata),
            PreviousResponseId = wire.PreviousResponseId,
            Instructions = wire.Instructions,
            Store = wire.Store,
            MaxStep = wire.MaxStep,
            MaxOutputTokens = wire.MaxOutputTokens,
            TimeoutSeconds = wire.TimeoutSeconds,
            IdempotencyKey = idempotencyKey
        };
        UhpInputValidation.ValidateContinuation(request.InputFeatures, request.PreviousResponseId);
        return request;
    }

    public static JsonElement SerializeResponse(UhpResponse response) => JsonSerializer.SerializeToElement(response, UhpJsonContext.Default.UhpResponse);
    public static JsonElement SerializeError(UhpError error) => JsonSerializer.SerializeToElement(new UhpErrorEnvelope(error), UhpJsonContext.Default.UhpErrorEnvelope);
    public static JsonElement SerializeDiscovery(UhpDiscovery discovery) => JsonSerializer.SerializeToElement(discovery, UhpJsonContext.Default.UhpDiscovery);
    public static JsonElement SerializeHarness(UhpHarness harness) => JsonSerializer.SerializeToElement(harness, UhpJsonContext.Default.UhpHarness);
    public static JsonElement SerializeHarnessList(IReadOnlyList<UhpHarness> harnesses) => JsonSerializer.SerializeToElement(new UhpHarnessList(harnesses), UhpJsonContext.Default.UhpHarnessList);
    public static JsonElement SerializeModelCatalog(UhpModelCatalog catalog) => JsonSerializer.SerializeToElement(catalog, UhpJsonContext.Default.UhpModelCatalog);
    public static JsonElement SerializeHarnessModels(UhpHarnessModels models) => JsonSerializer.SerializeToElement(models, UhpJsonContext.Default.UhpHarnessModels);
    internal static T Read<T>(JsonElement value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return value.Deserialize(typeInfo) ?? throw new UhpProtocolException("A UHP object cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new UhpProtocolException("Malformed UHP JSON object.", exception);
        }
    }

    internal static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UhpProtocolException($"Missing or empty {name}.");
        }
    }

    private static FrozenDictionary<string, JsonElement>? Own(Dictionary<string, JsonElement>? values) => values?.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    private static UhpError FromError(ErrorWire error) => new(error.Type, error.Code, error.Message)
    {
        Param = error.Param,
        Detail = error.Detail is null ? null : JsonSerializer.SerializeToElement(error.Detail, WireJsonContext.Default.DictionaryStringJsonElement),
        Raw = JsonSerializer.SerializeToElement(error, WireJsonContext.Default.ErrorWire)
    };
    private static UhpModel FromModel(ModelWire? model)
    {
        if (model is null)
        {
            throw new UhpProtocolException("A model cannot be null.");
        }

        RequireText(model.Id, "model.id");

        return new UhpModel(model.Id, model.Available)
        {
            Label = model.Label,
            Backend = model.Backend,
            IsDefault = model.Default,
            Raw = JsonSerializer.SerializeToElement(model, WireJsonContext.Default.ModelWire)
        };
    }
}
