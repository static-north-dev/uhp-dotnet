using System.Collections.Frozen;
using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal static class UhpRequestJsonWriter
{
    public static void Write(Utf8JsonWriter writer, UhpTaskRequest request, bool stream)
    {
        var harness = request.HarnessId;

        if (harness is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(harness, nameof(request));
        }

        var hasHarness = harness is not null && request.Metadata is { } fields && HasHarnessMetadata(fields, harness);
        writer.WriteStartObject();
        writer.WritePropertyName(UhpJsonFields.Input);
        request.Input.WriteTo(writer);
        writer.WriteBoolean("stream", stream);
        UhpJsonWriter.WriteOptional(writer, UhpJsonFields.Model, request.Model);
        UhpJsonWriter.WriteOptional(writer, UhpJsonFields.PreviousResponseId, request.PreviousResponseId);
        UhpJsonWriter.WriteOptional(writer, "instructions", request.Instructions);
        UhpJsonWriter.WriteOptional(writer, UhpJsonFields.Store, request.Store);
        UhpJsonWriter.WriteOptional(writer, "max_step", request.MaxStep);
        UhpJsonWriter.WriteOptional(writer, "max_output_tokens", request.MaxOutputTokens);
        UhpJsonWriter.WriteOptional(writer, "timeout_seconds", request.TimeoutSeconds);

        if (request.Metadata is not null || harness is not null)
        {
            writer.WriteStartObject(UhpJsonFields.Metadata);

            if (request.Metadata is { } metadata)
            {
                UhpJsonWriter.WriteMap(writer, metadata, default, static (target, value, _) => value.WriteTo(target));
            }

            if (harness is not null && !hasHarness)
            {
                writer.WriteString(UhpProtocol.HarnessId, harness);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static bool HasHarnessMetadata(IReadOnlyDictionary<string, JsonElement> values, string harness)
    {
        if (values is Dictionary<string, JsonElement> dictionary)
        {
            foreach (var entry in dictionary)
            {
                if (MatchesHarness(entry, harness))
                {
                    return true;
                }
            }

            return false;
        }

        if (values is FrozenDictionary<string, JsonElement> frozen)
        {
            foreach (var entry in frozen)
            {
                if (MatchesHarness(entry, harness))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var entry in values)
        {
            if (MatchesHarness(entry, harness))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesHarness(KeyValuePair<string, JsonElement> entry, string harness)
    {
        if (entry.Key != UhpProtocol.HarnessId)
        {
            return false;
        }

        if (entry.Value.ValueKind != JsonValueKind.String || !entry.Value.ValueEquals(harness))
        {
            throw new ArgumentException("HarnessId conflicts with metadata.harness_id.", "request");
        }

        return true;
    }
}
