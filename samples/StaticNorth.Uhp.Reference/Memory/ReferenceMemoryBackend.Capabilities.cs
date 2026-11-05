using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

public sealed partial class ReferenceMemoryBackend
{
    public Task<Wire.MemoryProviderList> ListMemoryProvidersAsync(UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new Wire.MemoryProviderList
        {
            Data = [new()
            {
                Id = ProviderId, Isolation = "container", Derivation = "none", Prime = true, Holds = true,
                Recall = Fields("{\"signals\":[\"text\"]}"), History = Fields("{\"content\":\"versions\"}"),
                Graph = new() { Entities = "stated" }, Content = new() { Media = ["text/*"], Bytes = "kept", Describes = [] },
                Erase = Fields("{\"supported\":true}"), Queries = Fields("{\"named\":false,\"free\":false}")
            }]
        });
    }

    public Task<Wire.MemoryTypeList> ListMemoryTypesAsync(UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new Wire.MemoryTypeList
        {
            Data = [new() { Type = "fact", Core = true }, new() { Type = "note", Core = true }, new() { Type = "entity", Core = true }, new() { Type = "procedure", Core = true }]
        });
    }

    public async Task<Wire.HarnessMemories> ListHarnessMemoriesAsync(string harnessId, UhpRequestContext context, CancellationToken ct)
    {
        await harnesses.GetHarnessAsync(harnessId, context, ct).ConfigureAwait(false);
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        return HarnessMemories(AgentId(harnessId, Caller(context)));
    }

    public async Task<Wire.HarnessMemories> SetHarnessMemoriesAsync(string harnessId, Wire.HarnessMemorySettings request, UhpRequestContext context, CancellationToken ct)
    {
        await harnesses.GetHarnessAsync(harnessId, context, ct).ConfigureAwait(false);
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var agent = AgentId(harnessId, Caller(context));

        if (request.DefaultMemoryId is { } memory)
        {
            if (!_nodes.TryGetValue(memory, out var node) || (EffectivePrivileges(node, agent) & Write) == 0)
            {
                throw Invalid("The default memory must be writable by the harness's agent.");
            }
        }

        if (request.Observe == true)
        {
            throw ReferenceBackend.Failure(422, "memory_unsupported", "This provider stores stated records and does not derive observations.");
        }

        _harnessSettings[agent] = request;
        return HarnessMemories(agent);
    }

    internal async Task<ReferenceAgentMemory?> SelectForSessionAsync(string harnessId, string owner, string? selected, bool continuation, string version, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);

        if (selected is null && _nodes.Count == 0)
        {
            return null;
        }

        var agent = AgentId(harnessId, owner);

        if (selected is not null && (!_nodes.TryGetValue(selected, out var node) || EffectivePrivileges(node, agent) == 0))
        {
            throw UhpServerException.NotFound("memory_not_found");
        }

        var settings = HarnessMemories(agent);

        if (settings.Data.Length == 0)
        {
            return null;
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, agent)], "UhpAgent");
        var context = new UhpRequestContext(new ClaimsPrincipal(identity), version, null);
        return new(this, context, selected ?? (continuation ? null : settings.DefaultMemoryId), settings.Data,
            _nodes.Values.Any(node => (EffectivePrivileges(node, agent) & Write) != 0));
    }

    private Wire.HarnessMemories HarnessMemories(string agent)
    {
        _harnessSettings.TryGetValue(agent, out var settings);
        var defaultId = settings?.DefaultMemoryId;

        if (defaultId is not null && (!_nodes.TryGetValue(defaultId, out var defaultNode) || (EffectivePrivileges(defaultNode, agent) & Write) == 0))
        {
            defaultId = null;
        }

        var result = new List<Wire.HarnessMemoriesDataItem>();

        foreach (var node in _nodes.Values)
        {
            var effective = EffectivePrivileges(node, agent);

            if (effective == 0 || (node.ParentId is { } parent && EffectivePrivileges(_nodes[parent], agent) == effective && node.Id != defaultId))
            {
                continue;
            }

            result.Add(new() { Id = node.Id, Name = node.Name, Privileges = Privileges(node, agent), Default = node.Id == defaultId });
        }

        return new() { Principal = MemberPrefix + agent, DefaultMemoryId = defaultId, Observe = false, Data = result.ToArray() };
    }

    private static string AgentId(string harness, string owner) => "uhpag_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{owner.Length}:{owner}{harness}")));

    private async Task<T> UnsupportedAsync<T>(string memoryId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        Require(memoryId, Caller(context), "read");
        throw ReferenceBackend.Failure(422, "memory_unsupported", "This connected provider does not support the requested operation.");
    }

    private static Dictionary<string, JsonElement> Fields(string json) => JsonSerializer.Deserialize(json, Wire.UhpContractJsonContext.Default.DictionaryStringJsonElement)!;
}
