using System.Collections.Frozen;
using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal static class UhpJsonWriter
{
    private static readonly string[] ResponseFields = [UhpJsonFields.Id, UhpJsonFields.Object, UhpJsonFields.CreatedAt, UhpJsonFields.Status, UhpJsonFields.Model, UhpJsonFields.Output, UhpJsonFields.Metadata, UhpJsonFields.PreviousResponseId, UhpJsonFields.Store, UhpJsonFields.Error, UhpJsonFields.IncompleteDetails, UhpJsonFields.Usage];
    private static readonly string[] HarnessFields = [UhpJsonFields.Id, UhpJsonFields.Object, UhpJsonFields.Name, UhpJsonFields.Base, UhpJsonFields.BaseLabel, UhpJsonFields.DefaultModel, UhpJsonFields.SystemPrompt, UhpJsonFields.MaxStep, UhpJsonFields.TimeoutSeconds, UhpJsonFields.HarnessCreatedAt];
    private static readonly string[] DiscoveryFields = [UhpJsonFields.Object, UhpJsonFields.Protocol, UhpJsonFields.Versions, UhpJsonFields.DefaultVersion, UhpJsonFields.ConformanceClass, UhpJsonFields.Capabilities, UhpJsonFields.Implementation];
    private static readonly string[] ImplementationFields = [UhpJsonFields.Name, UhpJsonFields.Version];
    private static readonly string[] ModelFields = [UhpJsonFields.Id, UhpJsonFields.Available, UhpJsonFields.Label, UhpJsonFields.Backend, UhpJsonFields.Default];
    private static readonly string[] CatalogFields = [UhpJsonFields.Backends];
    private static readonly string[] BackendFields = [UhpJsonFields.Default, UhpJsonFields.Models];
    private static readonly string[] HarnessModelsFields = [UhpProtocol.HarnessId, UhpJsonFields.Backend, UhpJsonFields.Default, UhpJsonFields.Fallback, UhpJsonFields.Models];
    private static readonly string[] ErrorFields = [UhpJsonFields.Type, UhpJsonFields.Code, UhpJsonFields.Message, UhpJsonFields.Param, UhpJsonFields.Detail];
    private static readonly string[] UsageFields = [UhpJsonFields.InputTokens, UhpJsonFields.OutputTokens, UhpJsonFields.TotalTokens, UhpJsonFields.CacheReadTokens, UhpJsonFields.CacheWriteTokens];
    private static readonly string[] CancellationFields = [UhpJsonFields.Id, UhpJsonFields.Status];

    public static void WriteResponse(Utf8JsonWriter writer, UhpResponse response)
    {
        writer.WriteStartObject();
        WriteRequired(writer, UhpJsonFields.Id, response.Id);
        writer.WriteString(UhpJsonFields.Object, UhpProtocol.ResponseObject);
        writer.WriteNumber(UhpJsonFields.CreatedAt, response.CreatedAt);
        WriteRequired(writer, UhpJsonFields.Status, response.Status.Value);
        WriteRequired(writer, UhpJsonFields.Model, response.Model);
        writer.WriteStartArray(UhpJsonFields.Output);

        for (var index = 0; index < response.Output.Count; index++)
        {
            response.Output[index].Raw.WriteTo(writer);
        }

        writer.WriteEndArray();
        WriteOptional(writer, UhpJsonFields.PreviousResponseId, response.PreviousResponseId);
        WriteOptional(writer, UhpJsonFields.Store, response.Store);

        if (response.Metadata is { } metadata)
        {
            writer.WriteStartObject(UhpJsonFields.Metadata);
            WriteMap(writer, metadata, default, static (target, value, _) => value.WriteTo(target));
            writer.WriteEndObject();
        }

        if (response.Error is { } error)
        {
            writer.WritePropertyName(UhpJsonFields.Error);
            WriteError(writer, error);
        }

        if (response.IncompleteDetails is { } details)
        {
            if (details.ValueKind != JsonValueKind.Object || !details.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Incomplete details require a reason string.");
            }

            writer.WritePropertyName(UhpJsonFields.IncompleteDetails);
            details.WriteTo(writer);
        }

        if (response.Usage is { } usage)
        {
            writer.WritePropertyName(UhpJsonFields.Usage);
            WriteUsage(writer, usage);
        }

        WriteExtensions(writer, response.Raw, ResponseFields);
        writer.WriteEndObject();
    }

    public static void WriteError(Utf8JsonWriter writer, UhpError error)
    {
        writer.WriteStartObject();
        WriteRequired(writer, UhpJsonFields.Type, error.Type);
        WriteRequired(writer, UhpJsonFields.Code, error.Code);
        WriteRequired(writer, UhpJsonFields.Message, error.Message);
        WriteOptional(writer, UhpJsonFields.Param, error.Param);

        if (error.Detail is { } detail)
        {
            if (detail.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Error detail must be an object.");
            }

            writer.WritePropertyName(UhpJsonFields.Detail);
            detail.WriteTo(writer);
        }

        WriteExtensions(writer, error.Raw, ErrorFields);
        writer.WriteEndObject();
    }

    public static void WriteDiscovery(Utf8JsonWriter writer, UhpDiscovery discovery)
    {
        writer.WriteStartObject();
        writer.WriteString(UhpJsonFields.Object, UhpProtocol.DiscoveryObject);
        writer.WriteString(UhpJsonFields.Protocol, UhpProtocol.Name);
        writer.WriteStartArray(UhpJsonFields.Versions);

        for (var index = 0; index < discovery.Versions.Count; index++)
        {
            writer.WriteStringValue(discovery.Versions[index]);
        }

        writer.WriteEndArray();
        WriteRequired(writer, UhpJsonFields.DefaultVersion, discovery.DefaultVersion);
        WriteRequired(writer, UhpJsonFields.ConformanceClass, discovery.ConformanceClass);
        writer.WriteStartObject(UhpJsonFields.Capabilities);
        WriteMap(writer, discovery.Capabilities.Values, default, static (target, value, _) => target.WriteBooleanValue(value));
        writer.WriteEndObject();
        writer.WriteStartObject(UhpJsonFields.Implementation);
        writer.WriteString(UhpJsonFields.Name, discovery.ImplementationName);
        writer.WriteString(UhpJsonFields.Version, discovery.ImplementationVersion);
        WriteExtensions(writer, Property(discovery.Raw, UhpJsonFields.Implementation), ImplementationFields);
        writer.WriteEndObject();
        WriteExtensions(writer, discovery.Raw, DiscoveryFields);
        writer.WriteEndObject();
    }

    public static void WriteHarness(Utf8JsonWriter writer, UhpHarness harness)
    {
        writer.WriteStartObject();
        WriteRequired(writer, UhpJsonFields.Id, harness.Id);
        writer.WriteString(UhpJsonFields.Object, UhpProtocol.HarnessObject);
        WriteRequired(writer, UhpJsonFields.Name, harness.Name);
        WriteRequired(writer, UhpJsonFields.Base, harness.Base);
        WriteOptional(writer, UhpJsonFields.BaseLabel, harness.BaseLabel);
        WriteOptional(writer, UhpJsonFields.DefaultModel, harness.DefaultModel);
        WriteOptional(writer, UhpJsonFields.SystemPrompt, harness.SystemPrompt);
        WriteOptional(writer, UhpJsonFields.MaxStep, harness.MaxStep);
        WriteOptional(writer, UhpJsonFields.TimeoutSeconds, harness.TimeoutSeconds);
        WriteOptional(writer, UhpJsonFields.HarnessCreatedAt, harness.CreatedAt);
        WriteExtensions(writer, harness.Raw, HarnessFields);
        writer.WriteEndObject();
    }

    public static void WriteCatalog(Utf8JsonWriter writer, UhpModelCatalog catalog)
    {
        writer.WriteStartObject();
        writer.WriteStartObject(UhpJsonFields.Backends);
        WriteMap(writer, catalog.Backends, Property(catalog.Raw, UhpJsonFields.Backends), WriteBackend);
        writer.WriteEndObject();
        WriteExtensions(writer, catalog.Raw, CatalogFields);
        writer.WriteEndObject();
    }

    public static void WriteHarnessModels(Utf8JsonWriter writer, UhpHarnessModels models)
    {
        writer.WriteStartObject();
        WriteOptional(writer, UhpProtocol.HarnessId, models.HarnessId);
        WriteOptional(writer, UhpJsonFields.Backend, models.Backend);
        WriteOptional(writer, UhpJsonFields.Default, models.Default);
        WriteOptional(writer, UhpJsonFields.Fallback, models.Fallback);
        WriteModels(writer, models.Models);
        WriteExtensions(writer, models.Raw, HarnessModelsFields);
        writer.WriteEndObject();
    }

    public static void WriteCancellation(Utf8JsonWriter writer, UhpSessionCancelResult result)
    {
        writer.WriteStartObject();
        WriteRequired(writer, UhpJsonFields.Id, result.Id);
        WriteRequired(writer, UhpJsonFields.Status, result.Status);
        WriteExtensions(writer, result.Raw, CancellationFields);
        writer.WriteEndObject();
    }

    public static void WriteExtensions(Utf8JsonWriter writer, JsonElement raw, ReadOnlySpan<string> writtenFields)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in raw.EnumerateObject())
        {
            var written = false;

            foreach (var name in writtenFields)
            {
                if (!property.NameEquals(name))
                {
                    continue;
                }

                written = true;
                break;
            }

            if (!written)
            {
                property.WriteTo(writer);
            }
        }
    }

    private static void WriteUsage(Utf8JsonWriter writer, UhpUsage usage)
    {
        writer.WriteStartObject();
        WriteOptional(writer, UhpJsonFields.InputTokens, usage.InputTokens);
        WriteOptional(writer, UhpJsonFields.OutputTokens, usage.OutputTokens);
        WriteOptional(writer, UhpJsonFields.TotalTokens, usage.TotalTokens);
        WriteOptional(writer, UhpJsonFields.CacheReadTokens, usage.CacheReadTokens);
        WriteOptional(writer, UhpJsonFields.CacheWriteTokens, usage.CacheWriteTokens);
        WriteExtensions(writer, usage.Raw, UsageFields);
        writer.WriteEndObject();
    }

    private static void WriteBackend(Utf8JsonWriter writer, UhpModelBackend backend, JsonElement original)
    {
        writer.WriteStartObject();
        WriteRequired(writer, UhpJsonFields.Default, backend.Default);
        WriteModels(writer, backend.Models);
        WriteExtensions(writer, original, BackendFields);
        writer.WriteEndObject();
    }

    private static void WriteModels(Utf8JsonWriter writer, IReadOnlyList<UhpModel> models)
    {
        writer.WriteStartArray(UhpJsonFields.Models);

        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            writer.WriteStartObject();
            WriteRequired(writer, UhpJsonFields.Id, model.Id);
            writer.WriteBoolean(UhpJsonFields.Available, model.Available);
            WriteOptional(writer, UhpJsonFields.Label, model.Label);
            WriteOptional(writer, UhpJsonFields.Backend, model.Backend);
            WriteOptional(writer, UhpJsonFields.Default, model.IsDefault);
            WriteExtensions(writer, model.Raw, ModelFields);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    internal static void WriteMap<T>(Utf8JsonWriter writer, IReadOnlyDictionary<string, T> values, JsonElement originals, Action<Utf8JsonWriter, T, JsonElement> write)
    {
        if (values is FrozenDictionary<string, T> frozen)
        {
            foreach (var entry in frozen)
            {
                writer.WritePropertyName(entry.Key);
                write(writer, entry.Value, Property(originals, entry.Key));
            }

            return;
        }

        if (values is Dictionary<string, T> dictionary)
        {
            foreach (var entry in dictionary)
            {
                writer.WritePropertyName(entry.Key);
                write(writer, entry.Value, Property(originals, entry.Key));
            }

            return;
        }

        foreach (var entry in values)
        {
            writer.WritePropertyName(entry.Key);
            write(writer, entry.Value, Property(originals, entry.Key));
        }
    }

    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;

    private static void WriteRequired(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            throw new JsonException($"Required property {name} cannot be null.");
        }

        writer.WriteString(name, value);
    }

    internal static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    internal static void WriteOptional(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
    }

    internal static void WriteOptional(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } flag)
        {
            writer.WriteBoolean(name, flag);
        }
    }
}
