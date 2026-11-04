using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Uhp.Core.Serialization;

namespace Uhp.Core;

public static class UhpJson
{
    public static Task WriteAsync(Stream stream, JsonElement value, CancellationToken ct = default) => JsonSerializer.SerializeAsync(stream, value, WireJsonContext.Default.JsonElement, ct);

    public static JsonElement CreateElement(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
        return document.RootElement.Clone();
    }

    public static UhpDiscovery ParseDiscovery(JsonElement value)
    {
        var wire = Read(value, WireJsonContext.Default.DiscoveryWire);

        if (wire.Object != "uhp.discovery" || wire.Protocol != "uhp" || wire.Versions.Length == 0 ||
            wire.Versions.Any(string.IsNullOrWhiteSpace) || !wire.Versions.Contains(wire.DefaultVersion, StringComparer.Ordinal))
        {
            throw new UhpProtocolException("Invalid UHP discovery document.");
        }

        var capabilities = new Dictionary<string, bool>(StringComparer.Ordinal);
        var flags = value.GetProperty("capabilities");

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
            Raw = value.Clone()
        };
    }

    public static UhpHarness ParseHarness(JsonElement value)
    {
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
            Raw = value.Clone()
        };
    }

    public static IReadOnlyList<UhpHarness> ParseHarnessList(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("harnesses", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new UhpProtocolException("Expected a harnesses array.");
        }

        return Array.AsReadOnly(list.EnumerateArray().Select(ParseHarness).ToArray());
    }

    public static UhpModelCatalog ParseModelCatalog(JsonElement value)
    {
        var wire = Read(value, WireJsonContext.Default.ModelCatalogWire);
        return new UhpModelCatalog(wire.Backends.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value is null ? throw new UhpProtocolException("A model backend cannot be null.") : new UhpModelBackend(pair.Value.Default, Array.AsReadOnly(pair.Value.Models.Select(FromModel).ToArray())),
            StringComparer.Ordinal))
        { Raw = value.Clone() };
    }

    public static UhpHarnessModels ParseHarnessModels(JsonElement value)
    {
        var wire = Read(value, WireJsonContext.Default.HarnessModelsWire);
        return new UhpHarnessModels(Array.AsReadOnly(wire.Models.Select(FromModel).ToArray()))
        {
            HarnessId = wire.HarnessId,
            Backend = wire.Backend,
            Default = wire.Default,
            Fallback = wire.Fallback,
            Raw = value.Clone()
        };
    }

    public static UhpResponse ParseResponse(JsonElement value)
    {
        var wire = Read(value, WireJsonContext.Default.ResponseWire);
        RequireText(wire.Id, "response.id");
        RequireText(wire.Status, "response.status");

        if (wire.Object != "response")
        {
            throw new UhpProtocolException("Expected a response object.");
        }

        return new UhpResponse(wire.Id, new UhpResponseStatus(wire.Status), wire.Model)
        {
            CreatedAt = wire.CreatedAt,
            Output = Array.AsReadOnly(value.GetProperty("output").EnumerateArray().Select(item => new UhpOutputItem(item)).ToArray()),
            Metadata = Own(wire.Metadata),
            PreviousResponseId = wire.PreviousResponseId,
            Store = wire.Store,
            Error = wire.Error is null ? null : FromError(wire.Error),
            IncompleteDetails = value.TryGetProperty("incomplete_details", out var details) && details.ValueKind != JsonValueKind.Null ? details.Clone() : null,
            Usage = wire.Usage is null ? null : new UhpUsage
            {
                InputTokens = wire.Usage.InputTokens,
                OutputTokens = wire.Usage.OutputTokens,
                TotalTokens = wire.Usage.TotalTokens,
                CacheReadTokens = wire.Usage.CacheReadTokens,
                CacheWriteTokens = wire.Usage.CacheWriteTokens,
                Raw = value.GetProperty("usage").Clone()
            },
            Raw = value.Clone()
        };
    }

    public static UhpError ParseError(JsonElement value) => FromError(Read(value, WireJsonContext.Default.ErrorEnvelopeWire).Error);

    public static UhpError ParseErrorValue(JsonElement value) => FromError(Read(value, WireJsonContext.Default.ErrorWire));

    public static JsonElement SerializeErrorValue(UhpError error) => JsonSerializer.SerializeToElement(ToError(error), WireJsonContext.Default.ErrorWire);

    public static UhpSessionCancelResult ParseSessionCancelResult(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            !value.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
        {
            throw new UhpProtocolException("Invalid session cancellation acknowledgement.");
        }

        RequireText(id.GetString(), "session.id");
        RequireText(status.GetString(), "session.status");
        return new UhpSessionCancelResult(id.GetString()!, status.GetString()!) { Raw = value.Clone() };
    }

    public static JsonElement SerializeSessionCancelResult(UhpSessionCancelResult result) => CreateElement(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("id", result.Id);
        writer.WriteString("status", result.Status);
        writer.WriteEndObject();
    });

    public static JsonElement SerializeRequest(UhpTaskRequest request, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxStep < 0 || request.MaxOutputTokens < 0 || request.TimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Task budgets cannot be negative.");
        }

        var metadata = request.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

        if (request.HarnessId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.HarnessId);
            metadata ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            if (metadata.TryGetValue("harness_id", out var rawHarness) && (rawHarness.ValueKind != JsonValueKind.String || rawHarness.GetString() != request.HarnessId))
            {
                throw new ArgumentException("HarnessId conflicts with metadata.harness_id.", nameof(request));
            }

            metadata["harness_id"] = CreateElement(writer => writer.WriteStringValue(request.HarnessId));
        }

        var wire = new CreateResponseRequestWire
        {
            Input = request.Input,
            Model = request.Model,
            Metadata = metadata,
            Stream = stream,
            PreviousResponseId = request.PreviousResponseId,
            Instructions = request.Instructions,
            Store = request.Store,
            MaxStep = request.MaxStep,
            MaxOutputTokens = request.MaxOutputTokens,
            TimeoutSeconds = request.TimeoutSeconds
        };

        return JsonSerializer.SerializeToElement(wire, WireJsonContext.Default.CreateResponseRequestWire);
    }

    public static UhpTaskRequest ParseRequest(JsonElement value, string? idempotencyKey = null)
    {
        var wire = Read(value, WireJsonContext.Default.CreateResponseRequestWire);
        ValidateTextInput(wire.Input);

        if (wire.Background == true)
        {
            throw new UhpProtocolException("Background execution is outside this preview.");
        }

        return new UhpTaskRequest(wire.Input)
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
    }

    public static JsonElement SerializeResponse(UhpResponse response)
    {
        var original = response.Raw.ValueKind == JsonValueKind.Object ? Read(response.Raw, WireJsonContext.Default.ResponseWire) : null;
        var wire = new ResponseWire
        {
            Id = response.Id,
            Object = "response",
            CreatedAt = response.CreatedAt,
            Status = response.Status.Value,
            Model = response.Model,
            Output = response.Output.Select(item => Read(item.Raw, WireJsonContext.Default.OutputItemWire)).ToArray(),
            Metadata = response.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value),
            PreviousResponseId = response.PreviousResponseId,
            Store = response.Store,
            Error = response.Error is null ? null : ToError(response.Error),
            IncompleteDetails = response.IncompleteDetails is { } details ? Read(details, WireJsonContext.Default.ResponseIncompleteDetailsWire) : null,
            Usage = response.Usage is null ? null : new UsageWire
            {
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens,
                TotalTokens = response.Usage.TotalTokens,
                CacheReadTokens = response.Usage.CacheReadTokens,
                CacheWriteTokens = response.Usage.CacheWriteTokens,
                Extensions = response.Usage.Raw.ValueKind == JsonValueKind.Object ? Read(response.Usage.Raw, WireJsonContext.Default.UsageWire).Extensions : null
            },
            Extensions = original?.Extensions
        };

        return JsonSerializer.SerializeToElement(wire, WireJsonContext.Default.ResponseWire);
    }

    public static JsonElement SerializeError(UhpError error) => JsonSerializer.SerializeToElement(new ErrorEnvelopeWire { Error = ToError(error) }, WireJsonContext.Default.ErrorEnvelopeWire);

    public static JsonElement SerializeDiscovery(UhpDiscovery discovery) => CreateElement(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("object", "uhp.discovery");
        writer.WriteString("protocol", "uhp");
        writer.WriteStartArray("versions");

        foreach (var version in discovery.Versions)
        {
            writer.WriteStringValue(version);
        }

        writer.WriteEndArray();
        writer.WriteString("default_version", discovery.DefaultVersion);
        writer.WriteString("conformance_class", discovery.ConformanceClass);
        writer.WriteStartObject("capabilities");

        foreach (var (name, enabled) in discovery.Capabilities.Values)
        {
            writer.WriteBoolean(name, enabled);
        }

        writer.WriteEndObject();
        writer.WriteStartObject("implementation");
        writer.WriteString("name", discovery.ImplementationName);
        writer.WriteString("version", discovery.ImplementationVersion);
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    public static JsonElement SerializeHarness(UhpHarness harness)
    {
        var original = harness.Raw.ValueKind == JsonValueKind.Object ? Read(harness.Raw, WireJsonContext.Default.HarnessWire) : null;
        return JsonSerializer.SerializeToElement(new HarnessWire
        {
            Id = harness.Id,
            Name = harness.Name,
            Base = harness.Base,
            Object = "harness",
            BaseLabel = harness.BaseLabel,
            DefaultModel = harness.DefaultModel,
            SystemPrompt = harness.SystemPrompt,
            MaxStep = harness.MaxStep,
            TimeoutSeconds = harness.TimeoutSeconds,
            CreatedAt = harness.CreatedAt,
            McpServers = original?.McpServers,
            Skills = original?.Skills,
            Plugins = original?.Plugins,
            Environment = original?.Environment,
            DisabledTools = original?.DisabledTools,
            Extensions = original?.Extensions
        }, WireJsonContext.Default.HarnessWire);
    }

    public static JsonElement SerializeHarnessList(IReadOnlyList<UhpHarness> harnesses) => CreateElement(writer =>
    {
        writer.WriteStartObject();
        writer.WriteStartArray("harnesses");

        foreach (var harness in harnesses)
        {
            SerializeHarness(harness).WriteTo(writer);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static JsonElement SerializeModelCatalog(UhpModelCatalog catalog) => JsonSerializer.SerializeToElement(new ModelCatalogWire
    {
        Backends = catalog.Backends.ToDictionary(pair => pair.Key, pair => new ModelCatalogBackendsValueWire
        {
            Default = pair.Value.Default,
            Models = pair.Value.Models.Select(ToModel).ToArray()
        })
    }, WireJsonContext.Default.ModelCatalogWire);

    public static JsonElement SerializeHarnessModels(UhpHarnessModels models) => JsonSerializer.SerializeToElement(new HarnessModelsWire
    {
        HarnessId = models.HarnessId,
        Backend = models.Backend,
        Default = models.Default,
        Fallback = models.Fallback,
        Models = models.Models.Select(ToModel).ToArray()
    }, WireJsonContext.Default.HarnessModelsWire);

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

    private static void ValidateTextInput(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            RequireText(input.GetString(), "input");
            return;
        }

        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() == 0)
        {
            throw new UhpProtocolException("Text input must be a string or a nonempty user-message array.");
        }

        foreach (var message in input.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("role", out var role) || role.GetString() != "user" ||
                !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
            {
                throw new UhpProtocolException("This preview accepts typed user text messages only.");
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var type) || type.GetString() != "input_text" ||
                    !part.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                {
                    throw new UhpProtocolException("This preview accepts input_text content only.");
                }

                RequireText(text.GetString(), "input.text");
            }
        }
    }

    private static FrozenDictionary<string, JsonElement>? Own(Dictionary<string, JsonElement>? values) => values?.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

    private static UhpError FromError(ErrorWire error) => new(error.Type, error.Code, error.Message)
    {
        Param = error.Param,
        Detail = error.Detail is null ? null : JsonSerializer.SerializeToElement(error.Detail, WireJsonContext.Default.DictionaryStringJsonElement),
        Raw = JsonSerializer.SerializeToElement(error, WireJsonContext.Default.ErrorWire)
    };

    private static ErrorWire ToError(UhpError error) => new()
    {
        Type = error.Type,
        Code = error.Code,
        Message = error.Message,
        Param = error.Param,
        Detail = error.Detail is { } detail ? Read(detail, WireJsonContext.Default.DictionaryStringJsonElement) : null,
        Extensions = error.Raw.ValueKind == JsonValueKind.Object ? Read(error.Raw, WireJsonContext.Default.ErrorWire).Extensions : null
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

    private static ModelWire ToModel(UhpModel model) => new()
    {
        Id = model.Id,
        Available = model.Available,
        Label = model.Label,
        Backend = model.Backend,
        Default = model.IsDefault,
        Extensions = model.Raw.ValueKind == JsonValueKind.Object ? Read(model.Raw, WireJsonContext.Default.ModelWire).Extensions : null
    };
}
