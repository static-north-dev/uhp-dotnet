using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Uhp.SchemaGen;

internal sealed class SchemaGenerator(string root)
{
    private static readonly string[] Roots =
    [
        "Discovery", "Capabilities", "Harness", "ModelCatalog", "HarnessModels", "Model",
        "CreateResponseRequest", "Response", "ErrorEnvelope", "Error", "Usage", "Event",
        "OutputItem", "ContentPart", "Annotation"
    ];

    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    private JsonObject _definitions = null!;

    public async Task RefreshAsync()
    {
        var manifest = ReadManifest();
        using var http = new HttpClient();

        foreach (var file in manifest["files"]!.AsArray())
        {
            var url = $"https://raw.githubusercontent.com/HarnessRouter/harnessrouter/{manifest["commit"]}/{file!["upstreamPath"]}";
            var bytes = await http.GetByteArrayAsync(url);
            VerifyHash(bytes, file["sha256"]!.GetValue<string>());
            await File.WriteAllBytesAsync(Path.Combine(root, "spec/upstream", file["path"]!.GetValue<string>()), bytes);
        }
    }

    public void Generate(bool check)
    {
        var manifest = ReadManifest();

        foreach (var file in manifest["files"]!.AsArray())
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, "spec/upstream", file!["path"]!.GetValue<string>()));
            VerifyHash(bytes, file["sha256"]!.GetValue<string>());
        }

        _definitions = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "spec/upstream/uhp-2026-10-19.schema.json")))!["$defs"]!.AsObject();

        foreach (var name in Roots)
        {
            EmitObject(name, _definitions[name]!.AsObject());
        }

        var context = new StringBuilder(Header);
        context.AppendLine("[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, RespectNullableAnnotations = true)]");

        foreach (var name in _emitted.Order(StringComparer.Ordinal))
        {
            context.AppendLine($"[JsonSerializable(typeof({name}Wire))]");
        }

        context.AppendLine("[JsonSerializable(typeof(JsonElement))]");
        context.AppendLine("[JsonSerializable(typeof(Dictionary<string, JsonElement>))]");
        context.AppendLine("internal sealed partial class WireJsonContext : JsonSerializerContext;");
        _files.Add("WireJsonContext.g.cs", context.ToString());

        var output = Path.Combine(root, "src/Uhp.Core/Serialization/Generated");
        Directory.CreateDirectory(output);
        var unexpected = Directory.GetFiles(output, "*.g.cs").Where(path => !_files.ContainsKey(Path.GetFileName(path))).ToArray();

        if (check && unexpected.Length > 0)
        {
            throw new InvalidOperationException("Unexpected generated files: " + string.Join(", ", unexpected));
        }

        foreach (var path in unexpected)
        {
            File.Delete(path);
        }

        foreach (var (name, content) in _files)
        {
            var path = Path.Combine(output, name);
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);

            if (check)
            {
                if (!File.Exists(path) || File.ReadAllText(path) != normalized)
                {
                    throw new InvalidOperationException($"Generated schema output differs: {name}");
                }
            }
            else
            {
                File.WriteAllText(path, normalized, new UTF8Encoding(false));
            }
        }

        Console.WriteLine($"{(check ? "Verified" : "Generated")} {_files.Count} files from pinned UHP schema; all input hashes verified.");
    }

    private void EmitObject(string name, JsonObject schema)
    {
        if (!_emitted.Add(name))
        {
            return;
        }

        var required = schema["required"]?.AsArray().Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        var source = new StringBuilder(Header);

        if (name == "OutputItem")
        {
            source.AppendLine("[JsonConverter(typeof(OutputItemWireConverter))]");
        }

        source.AppendLine($"internal sealed class {name}Wire");
        source.AppendLine("{");

        foreach (var (wireName, node) in schema["properties"]!.AsObject())
        {
            var property = node!.AsObject();
            var type = GetType(name + Pascal(wireName), property, name + "." + wireName);
            var isRequired = required.Contains(wireName);
            var nullable = !isRequired || AllowsNull(property);
            source.AppendLine($"    [JsonPropertyName(\"{wireName}\")]");
            source.AppendLine($"    public {(isRequired ? "required " : "")}{type}{(nullable ? "?" : "")} {Pascal(wireName)} {{ get; init; }}");
            source.AppendLine();
        }

        source.AppendLine("    [JsonExtensionData]");
        source.AppendLine("    public Dictionary<string, JsonElement>? Extensions { get; set; }");

        if (name == "OutputItem")
        {
            source.AppendLine();
            source.AppendLine("    [JsonIgnore]");
            source.AppendLine("    public JsonElement RawItem { get; set; }");
        }

        source.AppendLine("}");
        _files.Add(name + "Wire.g.cs", source.ToString());
    }

    private string GetType(string name, JsonObject schema, string path)
    {
        if (path is "CreateResponseRequest.metadata" or "Response.metadata")
        {
            return "Dictionary<string, JsonElement>";
        }

        if (schema["$ref"] is JsonValue reference)
        {
            var target = reference.GetValue<string>().Split('/')[^1];

            if (target is "McpServer" or "Skill" or "Plugin")
            {
                return "JsonElement";
            }

            var definition = _definitions[target]!.AsObject();

            if (definition["type"]?.ToString() == "string")
            {
                return "string";
            }

            EmitObject(target, definition);
            return target + "Wire";
        }

        if (schema["oneOf"] is JsonArray union)
        {
            var variants = union.Where(node => node!["type"]?.ToString() != "null").ToArray();
            return variants.Length == 1 ? GetType(name, variants[0]!.AsObject(), path) : "JsonElement";
        }

        var type = schema["type"] is JsonArray types
            ? types.First(node => node!.ToString() != "null")!.ToString()
            : schema["type"]?.ToString();

        switch (type)
        {
            case "string":
                return "string";
            case "integer":
                return "long";
            case "number":
                return "double";
            case "boolean":
                return "bool";
            case "array":
                return GetType(name + "Item", schema["items"]!.AsObject(), path + "[]") + "[]";
            case "object":
                if (schema["properties"] is JsonObject)
                {
                    EmitObject(name, schema);
                    return name + "Wire";
                }

                return schema["additionalProperties"] is JsonObject additional
                    ? "Dictionary<string, " + GetType(name + "Value", additional, path + ".*") + ">"
                    : "Dictionary<string, JsonElement>";
            case null when schema.Count == 0:
                return "JsonElement";
            default:
                throw new InvalidOperationException($"Unsupported preview schema shape at {path}: {schema}");
        }
    }

    private static bool AllowsNull(JsonObject schema)
    {
        return schema["type"] is JsonArray types && types.Any(node => node!.ToString() == "null") ||
            schema["oneOf"] is JsonArray variants && variants.Any(node => node!["type"]?.ToString() == "null");
    }

    private JsonObject ReadManifest() => JsonNode.Parse(File.ReadAllText(Path.Combine(root, "spec/upstream/manifest.json")))!.AsObject();

    private static void VerifyHash(byte[] bytes, string expected)
    {
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Pinned upstream input failed SHA-256 verification.");
        }
    }

    private static string Pascal(string value) => string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private const string Header = "// <auto-generated />\n// UHP 2026-10-19 at 80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6. Run eng/Uhp.SchemaGen to regenerate.\n#nullable enable\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\nnamespace Uhp.Core.Serialization;\n\n";
}
