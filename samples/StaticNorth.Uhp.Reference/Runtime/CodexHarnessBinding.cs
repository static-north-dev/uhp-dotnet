using System.Text;
using System.Text.Json;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

internal static class CodexHarnessBinding
{
    public static async Task<List<CodexMcpBinding>> PrepareAsync(ReferenceRuntimeRequest request, string home, string executablePath, CancellationToken ct)
    {
        var result = new List<CodexMcpBinding>();

        foreach (var server in request.Harness.McpServers ?? [])
        {
            if (server.Enabled == false)
            {
                continue;
            }

            var headers = server.Headers is null ? new Dictionary<string, string>() : new(server.Headers, StringComparer.OrdinalIgnoreCase);

            if (server.Auth is { } auth)
            {
                headers["Authorization"] = "Bearer " + auth;
            }

            result.Add(new(server.Name, server.Url, null, null, headers, null, null));
        }

        foreach (var skill in request.Harness.Skills ?? [])
        {
            if (skill.Enabled != false)
            {
                await MaterializeAsync(Path.Combine(request.Workspace, ".agents", "skills", SafeFolder(skill.Name)), skill.Files ?? [new() { Path = "SKILL.md", Content = skill.Content ?? "" }], ct)
                    .ConfigureAwait(false);
            }
        }

        foreach (var plugin in request.Harness.Plugins ?? [])
        {
            if (plugin.Enabled == false)
            {
                continue;
            }

            var root = Path.Combine(request.Workspace, ".uhp", "plugins", SafeFolder(plugin.Name));
            var data = Path.Combine(request.Workspace, ".uhp", "plugin-data", SafeFolder(plugin.Name));
            Directory.CreateDirectory(data);
            await MaterializeAsync(root, plugin.Files ?? [], ct)
                .ConfigureAwait(false);

            foreach (var skill in plugin.Skills ?? [])
            {
                var source = "skills/" + skill.Name + "/";
                var files = (plugin.Files ?? [])
                    .Where(file => file.Path.StartsWith(source, StringComparison.Ordinal))
                    .Select(file => file with { Path = file.Path[source.Length..] })
                    .ToArray();
                await MaterializeAsync(Path.Combine(request.Workspace, ".agents", "skills", SafeFolder(skill.Name)), files, ct)
                    .ConfigureAwait(false);
            }

            foreach (var server in plugin.McpServers ?? [])
            {
                if (server.Enabled == false)
                {
                    continue;
                }

                if (server.Transport == "http")
                {
                    result.Add(new(server.Name, server.Url, null, null, server.Headers, null, null));
                    continue;
                }

                var command = Expand(server.Command ?? throw new IOException("A stdio plugin requires a command."), root, data);
                var arguments = (server.Args ?? [])
                    .Select(value => Expand(value, root, data))
                    .ToArray();
                var pluginHome = Path.Combine(home, "plugin-sandboxes", SafeFolder(server.Name));
                Directory.CreateDirectory(pluginHome);
                await WriteSandboxAsync(pluginHome, root, data, ct)
                    .ConfigureAwait(false);
                var args = new List<string> { "sandbox", "--", "/usr/bin/env", "-i", "PATH=/usr/bin:/bin:/opt/homebrew/bin", "HOME=" + data };

                foreach (var variable in server.Env ?? [])
                {
                    if (string.IsNullOrEmpty(variable.Key) || variable.Key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') || char.IsAsciiDigit(variable.Key[0]))
                    {
                        throw new IOException("Invalid plugin environment variable name.");
                    }

                    args.Add(variable.Key + "=" + Expand(variable.Value, root, data));
                }

                args.Add(command.StartsWith("./", StringComparison.Ordinal) ? Path.Combine(root, command[2..]) : command);
                args.AddRange(arguments);
                result.Add(new(server.Name, null, executablePath, args.ToArray(), null, new() { ["CODEX_HOME"] = pluginHome }, root));
            }
        }

        return result;
    }

    public static void Write(Utf8JsonWriter writer, List<CodexMcpBinding> bindings, string[]? disabledTools)
    {
        writer.WriteNumber("mcp_optional_startup_grace_ms", 0);
        writer.WriteStartObject("mcp_servers");

        foreach (var binding in bindings)
        {
            writer.WriteStartObject(binding.Name);
            writer.WriteString("default_tools_approval_mode", "approve");

            if (binding.Url is { } url)
            {
                writer.WriteString("url", url);

                if (binding.Headers is { Count: > 0 } headers)
                {
                    writer.WriteStartObject("http_headers");

                    foreach (var header in headers)
                    {
                        writer.WriteString(header.Key, header.Value);
                    }

                    writer.WriteEndObject();
                }
            }
            else
            {
                writer.WriteString("command", binding.Command);
                writer.WriteString("cwd", binding.Cwd);
                writer.WriteStartArray("args");

                foreach (var argument in binding.Args ?? [])
                {
                    writer.WriteStringValue(argument);
                }

                writer.WriteEndArray();
                writer.WriteStartObject("env");

                foreach (var variable in binding.Env ?? [])
                {
                    writer.WriteString(variable.Key, variable.Value);
                }

                writer.WriteEndObject();
            }

            if (disabledTools is { Length: > 0 })
            {
                writer.WriteStartArray("disabled_tools");
                var prefix = "mcp__" + binding.Name + "__";

                foreach (var tool in disabledTools)
                {
                    if (tool.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        writer.WriteStringValue(tool[prefix.Length..]);
                    }
                    else if (!tool.StartsWith("mcp__", StringComparison.Ordinal))
                    {
                        writer.WriteStringValue(tool);
                    }
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static async Task WriteSandboxAsync(string home, string root, string data, CancellationToken ct)
    {
        var config = new StringBuilder("default_permissions = \"uhp-plugin\"\napproval_policy = \"never\"\n[permissions.uhp-plugin.filesystem]\n\":minimal\" = \"read\"\n");
        AddPath(config, ReferenceCodexExecutable.ExecutablePath, "read");
        AddPath(config, root, "read");
        AddPath(config, data, "write");

        if (OperatingSystem.IsMacOS() && Directory.Exists("/opt/homebrew"))
        {
            AddPath(config, "/opt/homebrew", "read");
        }

        config.AppendLine("[permissions.uhp-plugin.network]\nenabled = false");
        await System.IO.File
            .WriteAllTextAsync(Path.Combine(home, "config.toml"), config.ToString(), ct)
            .ConfigureAwait(false);
    }

    private static void AddPath(StringBuilder config, string path, string access) => config
        .Append(JsonSerializer.Serialize(path, Wire.UhpContractJsonContext.Default.String))
        .Append(" = \"")
        .Append(access)
        .AppendLine("\"");
    private static string Expand(string value, string root, string data) => value
        .Replace("${PLUGIN_ROOT}", root, StringComparison.Ordinal)
        .Replace("${PLUGIN_DATA}", data, StringComparison.Ordinal);
    private static string SafeFolder(string name) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    private static async Task MaterializeAsync(string root, Wire.SkillFile[] files, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        ReferencePlugins.ValidateFiles(files, "plugin_invalid");

        foreach (var file in files)
        {
            var path = Path.Combine(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await System.IO.File
                .WriteAllBytesAsync(path, ReferencePlugins.Bytes(file), ct)
                .ConfigureAwait(false);
        }
    }
}
