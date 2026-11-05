using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace StaticNorth.Uhp.SchemaGen;

/// <summary>Reads the pinned OpenAPI YAML into a JSON-shaped DOM without reflection or YAML object activation.</summary>
internal static class OpenApiReader
{
    public static async Task<JsonObject> ReadAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var parser = new Parser(new StringReader(text));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var root = ReadNode(parser)!.AsObject();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return root;
    }

    private static JsonNode? ReadNode(IParser parser)
    {
        if (parser.Accept<MappingStart>(out _))
        {
            parser.Consume<MappingStart>();
            var result = new JsonObject();

            while (!parser.Accept<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value;
                // Upstream repeats a descriptive tag key. Preserve YAML's last value without changing its pinned bytes.
                result[key] = ReadNode(parser);
            }

            parser.Consume<MappingEnd>();
            return result;
        }

        if (parser.Accept<SequenceStart>(out _))
        {
            parser.Consume<SequenceStart>();
            var result = new JsonArray();

            while (!parser.Accept<SequenceEnd>(out _))
            {
                result.Add(ReadNode(parser));
            }

            parser.Consume<SequenceEnd>();
            return result;
        }

        var scalar = parser.Consume<Scalar>();

        if (scalar.Style == ScalarStyle.Plain)
        {
            if (scalar.Value is "null" or "~")
            {
                return null;
            }

            if (bool.TryParse(scalar.Value, out var boolean))
            {
                return JsonValue.Create(boolean);
            }

            if (long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return JsonValue.Create(integer);
            }
        }

        return JsonValue.Create(scalar.Value);
    }
}
