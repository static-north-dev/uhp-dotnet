using System.Security.Cryptography;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public partial class ReferenceBackend : IUhpHarnessManagementBackend, IUhpPluginBackend
{
    internal const string PluginSchema = "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json";
    private readonly Dictionary<string, ReferenceHarnessEntry> _harnesses = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Owner, string Key), (string Hash, string Id)> _harnessKeys = [];

    public Task<Wire.Harness> CreateHarnessAsync(Wire.HarnessCreate request, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, Wire.UhpContractJsonContext.Default.HarnessCreate)));

        lock (_gate)
        {
            var owner = Owner(context);

            if (context.IdempotencyKey is { } key && _harnessKeys.TryGetValue((owner, key), out var replay))
            {
                if (hash != replay.Hash)
                {
                    throw Failure(409, "idempotency_key_reused", "The key already names a different harness request.");
                }

                return Task.FromResult(FindHarness(replay.Id, owner));
            }

            var result = ConfigureHarness($"chrn_{Guid.NewGuid():N}", request, null);

            if (result.Environment is { } environment)
            {
                (environments ?? throw UhpServerException.NotFound("environment_not_found")).ValidateReference(environment, owner);
            }
            _harnesses.Add(result.Id, new(owner, result));

            if (context.IdempotencyKey is { } acceptedKey)
            {
                _harnessKeys.Add((owner, acceptedKey), (hash, result.Id));
            }

            return Task.FromResult(result);
        }
    }

    public Task<Wire.Harness> UpdateHarnessAsync(string harnessId, Wire.HarnessCreate request, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var owner = Owner(context);
            var previous = FindHarness(harnessId, owner);

            if (harnessId == Harness.Id || request.Base != previous.Base)
            {
                throw Failure(409, "invalid_input", "The harness base is immutable.");
            }

            var result = ConfigureHarness(harnessId, request, previous);

            if (result.Environment is { } environment)
            {
                (environments ?? throw UhpServerException.NotFound("environment_not_found")).ValidateReference(environment, owner);
            }
            _harnesses[harnessId] = new(owner, result);
            return Task.FromResult(result);
        }
    }

    public Task<Wire.DeleteHarnessResponse> DeleteHarnessAsync(string harnessId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            FindHarness(harnessId, Owner(context));

            if (!_harnesses.Remove(harnessId))
            {
                throw Failure(409, "invalid_input", "The built-in harness cannot be deleted.");
            }

            var keys = _harnessKeys
                .Where(entry => entry.Value.Id == harnessId)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var key in keys)
            {
                _harnessKeys.Remove(key);
            }

            return Task.FromResult(new Wire.DeleteHarnessResponse { Deleted = true, Sessions = "kept" });
        }
    }

    public Task<Wire.FileList> GetSkillFilesAsync(string harnessId, string skillName, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var harness = FindHarness(harnessId, Owner(context));
            var skill = Array.Find(harness.Skills ?? [], entry => entry.Name == skillName) ?? throw UhpServerException.NotFound("skill_not_found");
            return Task.FromResult(new Wire.FileList { Files = skill.Files ?? [] });
        }
    }

    public Task<Wire.FileList> GetPluginFilesAsync(string harnessId, string pluginName, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var harness = FindHarness(harnessId, Owner(context));
            var plugin = Array.Find(harness.Plugins ?? [], entry => entry.Name == pluginName) ?? throw UhpServerException.NotFound("plugin_not_found");
            return Task.FromResult(new Wire.FileList { Files = plugin.Files ?? [] });
        }
    }

    public Task<Wire.Plugin> ExportHarnessPluginAsync(string harnessId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var harness = FindHarness(harnessId, Owner(context));
            return Task.FromResult(ReferencePlugins.Export(harness));
        }
    }

    private Wire.Harness ConfigureHarness(string id, Wire.HarnessCreate request, Wire.Harness? previous)
    {
        if (request.Base != _runtime.Base)
        {
            throw Failure(422, "unsupported_harness_base", "The requested harness base is not installed.", UhpJson.CreateElement(writer =>
            {
                writer.WriteStartObject(); writer.WriteStartArray("supported"); writer.WriteStringValue(_runtime.Base); writer.WriteEndArray(); writer.WriteEndObject();
            }));
        }

        var skills = request.Skills ?? previous?.Skills ?? [];

        for (var index = 0; index < skills.Length; index++)
        {
            var skill = skills[index];
            ReferencePlugins.ValidateFiles(skill.Files, "invalid_skill");

            if (skill.Files is not null && !Array.Exists(skill.Files, file => file.Path == "SKILL.md"))
            {
                throw Failure(422, "invalid_skill", "A skill bundle requires SKILL.md.");
            }
        }

        var plugins = request.Plugins is null ? previous?.Plugins ?? [] : ReferencePlugins.Install(request.Plugins);
        var servers = request.McpServers ?? previous?.McpServers ?? [];
        ReferencePlugins.ValidateNamespace(servers, skills, plugins);

        return new Wire.Harness
        {
            Id = id,
            Name = request.Name ?? previous?.Name ?? id,
            Base = request.Base,
            DefaultModel = request.DefaultModel ?? previous?.DefaultModel ?? _runtime.DefaultModel,
            SystemPrompt = request.SystemPrompt ?? previous?.SystemPrompt,
            Skills = skills,
            McpServers = servers,
            Plugins = plugins,
            Environment = request.Environment ?? previous?.Environment,
            DisabledTools = request.DisabledTools ?? previous?.DisabledTools ?? [],
            MaxStep = request.MaxStep ?? previous?.MaxStep,
            TimeoutSeconds = request.TimeoutSeconds ?? previous?.TimeoutSeconds,
            CreatedAt = previous?.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private Wire.Harness FindHarness(string id, string owner)
    {
        if (id == Harness.Id)
        {
            return new Wire.Harness { Id = Harness.Id, Name = Harness.Name, Base = Harness.Base, DefaultModel = _runtime.DefaultModel };
        }

        return _harnesses.TryGetValue(id, out var entry) && entry.Owner == owner ? entry.Harness : throw UhpServerException.NotFound("harness_not_found");
    }

    private static UhpHarness ToCore(Wire.Harness harness) => UhpJson.ParseHarness(JsonSerializer.SerializeToElement(harness, Wire.UhpContractJsonContext.Default.Harness));

    internal static UhpServerException Failure(int status, string code, string message, JsonElement? detail = null) => new(status, new("invalid_request_error", code, message) { Detail = detail });
}
