using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

internal static partial class ReferencePlugins
{
    public static Wire.Plugin[] Install(Wire.PluginInstall[] installations)
    {
        var plugins = new Wire.Plugin[installations.Length];
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < installations.Length; index++)
        {
            var installation = installations[index];
            var files = installation.Files ?? throw ReferenceBackend.Failure(422, "plugin_invalid", "A plugin installation requires its complete files.");
            UhpHarnessPackageFiles.ValidateFiles(files, "plugin_invalid");
            var manifestFile = Array.Find(files, file => file.Path == "plugin.json") ?? throw ReferenceBackend.Failure(422, "plugin_invalid", "A plugin requires plugin.json.");
            Wire.PluginManifest manifest;

            try
            {
                manifest = JsonSerializer.Deserialize(UhpHarnessPackageFiles.Bytes(manifestFile), Wire.UhpContractJsonContext.Default.PluginManifest) ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw ReferenceBackend.Failure(422, "plugin_invalid", "The plugin manifest is invalid.");
            }

            if (manifest.Schema != ReferenceBackend.PluginSchema)
            {
                throw ReferenceBackend.Failure(422, "unsupported_plugin_schema", "The plugin schema is not installed.", UhpJson.CreateElement(writer =>
                {
                    writer.WriteStartObject(); writer.WriteStartArray("supported"); writer.WriteStringValue(ReferenceBackend.PluginSchema); writer.WriteEndArray(); writer.WriteEndObject();
                }));
            }

            if (!PluginNamePattern()
                .IsMatch(manifest.Name))
            {
                throw ReferenceBackend.Failure(422, "plugin_invalid", "The plugin name does not match its manifest schema.");
            }

            if (!names.Add(manifest.Name))
            {
                throw Conflict("plugin", manifest.Name);
            }

            var servers = new List<Wire.PluginMcpServer>();
            var skipped = new List<Wire.PluginSkipped>();
            var mcpFile = Array.Find(files, file => file.Path == "mcp.json");

            if (mcpFile is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(UhpHarnessPackageFiles.Bytes(mcpFile));

                    if (document.RootElement.TryGetProperty("mcpServers", out var entries) && entries.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var entry in entries.EnumerateObject())
                        {
                            var value = entry.Value;
                            var type = Text(value, "type");

                            if (type is not ("streamable-http" or "stdio"))
                            {
                                skipped.Add(new() { Path = "mcp.json#" + entry.Name, Reason = "Unsupported MCP transport." });
                                continue;
                            }

                            servers.Add(new()
                            {
                                Name = entry.Name,
                                Transport = type == "stdio" ? "stdio" : "http",
                                Url = Text(value, "url"),
                                Command = Text(value, "command"),
                                Cwd = Text(value, "cwd"),
                                Args = value.TryGetProperty("args", out var args) ? args.Deserialize(Wire.UhpContractJsonContext.Default.StringArray) : null,
                                Env = value.TryGetProperty("env", out var env) ? env.Deserialize(Wire.UhpContractJsonContext.Default.DictionaryStringString) : null,
                                Headers = value.TryGetProperty("headers", out var headers) ? headers.Deserialize(Wire.UhpContractJsonContext.Default.DictionaryStringString) : null,
                                Enabled = true
                            });
                        }
                    }
                }
                catch (JsonException)
                {
                    skipped.Add(new() { Path = "mcp.json", Reason = "Invalid MCP configuration." });
                }
            }

            var skills = new List<Wire.PluginSkill>();

            for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                var path = files[fileIndex].Path;

                if (path.StartsWith("skills/", StringComparison.Ordinal) && path.EndsWith("/SKILL.md", StringComparison.Ordinal))
                {
                    var name = path[7..^9];

                    if (!name.Contains('/'))
                    {
                        skills.Add(new() { Name = name });
                    }
                }
            }

            plugins[index] = new()
            {
                Name = manifest.Name,
                Enabled = installation.Enabled ?? true,
                Files = files,
                Manifest = manifest,
                McpServers = servers.ToArray(),
                Skills = skills.ToArray(),
                Skipped = skipped.ToArray()
            };
        }

        return plugins;
    }

    public static void ValidateNamespace(Wire.McpServer[] directServers, Wire.Skill[] directSkills, Wire.Plugin[] plugins)
    {
        var servers = new HashSet<string>(StringComparer.Ordinal);
        var skills = new HashSet<string>(StringComparer.Ordinal);

        foreach (var server in directServers)
        {
            if (!servers.Add(server.Name))
            {
                throw Conflict("mcp_server", server.Name);
            }
        }

        foreach (var skill in directSkills)
        {
            if (!skills.Add(skill.Name))
            {
                throw Conflict("skill", skill.Name);
            }
        }

        foreach (var plugin in plugins)
        {
            foreach (var server in plugin.McpServers ?? [])
            {
                if (!servers.Add(server.Name))
                {
                    throw Conflict("mcp_server", server.Name);
                }
            }

            foreach (var skill in plugin.Skills ?? [])
            {
                if (!skills.Add(skill.Name))
                {
                    throw Conflict("skill", skill.Name);
                }
            }
        }
    }

    public static Wire.Plugin Export(Wire.Harness harness)
    {
        var name = harness.Id.Replace('_', '-');
        var files = new List<Wire.SkillFile>();
        var skipped = new List<Wire.PluginSkipped>();
        var manifest = new Wire.PluginManifest { Schema = ReferenceBackend.PluginSchema, Name = name, Version = "1.0.0" };
        files.Add(new() { Path = "plugin.json", Content = JsonSerializer.Serialize(manifest, Wire.UhpContractJsonContext.Default.PluginManifest) });
        var mcp = UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("$schema", "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json"); writer.WriteStartObject("mcpServers");

            foreach (var server in harness.McpServers ?? [])
            {
                if (server.Enabled == false)
                {
                    continue;
                }

                writer.WriteStartObject(server.Name); writer.WriteString("type", "streamable-http"); writer.WriteString("url", server.Url); writer.WriteEndObject();

                if (server.Auth is not null || server.Headers is { Count: > 0 })
                {
                    skipped.Add(new() { Path = "mcp.json#" + server.Name, Reason = "Operator credentials omitted." });
                }
            }

            writer.WriteEndObject(); writer.WriteEndObject();
        });
        files.Add(new() { Path = "mcp.json", Content = mcp.GetRawText() });

        foreach (var skill in harness.Skills ?? [])
        {
            if (skill.Enabled == false)
            {
                continue;
            }

            foreach (var file in skill.Files ?? [])
            {
                files.Add(file with { Path = "skills/" + skill.Name + "/" + file.Path });
            }
        }

        var installed = Install([new() { Files = files.ToArray() }])[0];
        return installed with { Skipped = skipped.ToArray() };
    }

    [System.Text.RegularExpressions.GeneratedRegex("^(?!.*(--|\\.\\.))[a-z0-9]([a-z0-9.-]*[a-z0-9])?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PluginNamePattern();
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static Server.UhpServerException Conflict(string component, string name) => ReferenceBackend.Failure(409, "plugin_conflict", "A component name is already installed.", UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("component", component); writer.WriteString("name", name); writer.WriteEndObject();
    }));
}
