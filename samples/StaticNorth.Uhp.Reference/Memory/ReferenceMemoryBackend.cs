using System.Security.Claims;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;
using StaticNorth.Uhp.TestSupport.Memory;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

public sealed partial class ReferenceMemoryBackend(IReferenceMemoryProvider provider, IUhpServerBackend harnesses) : IUhpMemoryBackend, IDisposable
{
    private const string ProviderId = "in-memory";
    private const int Read = 1;
    private const int Write = 2;
    private const int Create = 4;
    private const int Delete = 8;
    private const int All = Read | Write | Create | Delete;
    private const string MemberPrefix = "member:";
    private static readonly string[] AllPrivileges = ["read", "write", "create", "delete"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ReferenceMemoryNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Record, long Version), ReferenceRecordMetadata> _recordMetadata = [];
    private readonly Dictionary<string, Wire.HarnessMemorySettings> _harnessSettings = new(StringComparer.Ordinal);

    public async Task<Wire.Memory> CreateMemoryAsync(Wire.MemoryCreate request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        var caller = Caller(context);

        if (request.ParentId is { } parent)
        {
            Require(parent, caller, "create");
        }

        if (request.Provider is { } selected && selected != ProviderId)
        {
            throw ReferenceBackend.Failure(422, "memory_unsupported", "This provider is not connected.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw Invalid("A memory requires a name.");
        }

        var id = await provider
            .CreateMemoryAsync(caller, ct)
            .ConfigureAwait(false);
        var node = new ReferenceMemoryNode(id, caller, request.Name, request.ParentId, request.Description, request.Restricted ?? false);
        _nodes.Add(id, node);
        return View(node, caller);
    }

    public async Task<Wire.Memory> GetMemoryAsync(string memoryId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        return View(Require(memoryId, Caller(context), "read"), Caller(context));
    }

    public async Task<Wire.MemoryList> ListMemoriesAsync(Wire.ListMemoriesQuery query, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        var caller = Caller(context);
        var result = new List<Wire.Memory>();

        foreach (var node in _nodes.Values)
        {
            var privileges = EffectivePrivileges(node, caller);

            if ((privileges & Read) == 0)
            {
                continue;
            }

            if (query.Granted == true)
            {
                if (node.ParentId is { } parent && EffectivePrivileges(_nodes[parent], caller) == privileges)
                {
                    continue;
                }
            }
            else if (node.ParentId != query.Parent)
            {
                continue;
            }

            result.Add(View(node, caller));
        }

        return new() { Data = result.ToArray() };
    }

    public async Task<Wire.Memory> UpdateMemoryAsync(string memoryId, Wire.MemoryUpdate request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "write");

        if (request.ParentId is { } parent)
        {
            var target = Require(parent, caller, "create");

            for (var current = target; current is not null; current = current.ParentId is { } id ? _nodes[id] : null)
            {
                if (current.Id == memoryId)
                {
                    throw Invalid("A memory cannot move into its subtree.");
                }
            }

            node.ParentId = parent;
        }

        node.Name = request.Name ?? node.Name;
        node.Description = request.Description ?? node.Description;
        node.Restricted = request.Restricted ?? node.Restricted;
        return View(node, caller);
    }

    public async Task<Wire.MemoryDeleted> DeleteMemoryAsync(string memoryId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        Require(memoryId, Caller(context), "delete");
        var nodes = Descendants(memoryId, null);

        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            var node = nodes[index];
            await provider
                .DeleteMemoryAsync(node.Owner, node.Id, ct)
                .ConfigureAwait(false);
            _nodes.Remove(node.Id);
        }

        return new() { Id = memoryId, Deleted = true };
    }

    public async Task<Wire.MemoryGrantList> ListMemoryGrantsAsync(string memoryId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        var node = Require(memoryId, Caller(context), "read");
        var result = new List<Wire.MemoryGrant>();

        for (var current = node; current is not null; current = !current.Restricted && current.ParentId is { } id ? _nodes[id] : null)
        {
            foreach (var grant in current.Grants.Values)
            {
                result.Add(grant with { Inherited = current.Id != memoryId });
            }
        }

        return new() { Data = result.ToArray() };
    }

    public async Task<Wire.MemoryGrant> CreateMemoryGrantAsync(string memoryId, Wire.MemoryGrantCreate request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        var node = Require(memoryId, Caller(context), "delete");

        if (!request.Principal.StartsWith(MemberPrefix, StringComparison.Ordinal) || request.Principal
            .AsSpan(MemberPrefix.Length)
            .IsWhiteSpace() || request.Privileges.Length == 0 || request.Privileges.Any(value => Privilege(value) == 0))
        {
            throw Invalid("A grant requires a member principal and valid privileges.");
        }

        var grant = new Wire.MemoryGrant { Id = $"hgrant_{Guid.NewGuid():N}", MemoryId = memoryId, Principal = request.Principal, Privileges = request.Privileges.ToArray(), Inherited = false };
        node.Grants.Add(grant.Id, grant);
        return grant;
    }

    public async Task<Wire.Deleted> DeleteMemoryGrantAsync(string memoryId, string grantId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct)
            .ConfigureAwait(false);
        var node = Require(memoryId, Caller(context), "delete");

        if (!node.Grants.Remove(grantId))
        {
            throw UhpServerException.NotFound("memory_grant_not_found");
        }

        return new() { Id = grantId, IsDeleted = true };
    }

    private async ValueTask<ReferenceMemoryLease> EnterAsync(CancellationToken ct)
    {
        await _gate
            .WaitAsync(ct)
            .ConfigureAwait(false);
        return new(_gate);
    }

    private ReferenceMemoryNode Require(string id, string caller, string privilege)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            throw UhpServerException.NotFound("memory_not_found");
        }

        var effective = EffectivePrivileges(node, caller);

        if ((effective & Privilege(privilege)) != 0)
        {
            return node;
        }

        if (effective == 0)
        {
            throw UhpServerException.NotFound("memory_not_found");
        }

        throw ReferenceBackend.Failure(403, "memory_forbidden", "The caller does not hold the required privilege.");
    }

    private static int Privilege(string value) => value switch
    {
        "read" => Read,
        "write" => Write,
        "create" => Create,
        "delete" => Delete,
        _ => 0
    };

    private int EffectivePrivileges(ReferenceMemoryNode node, string caller)
    {
        var effective = 0;

        for (var current = node; current is not null; current = !current.Restricted && current.ParentId is { } id ? _nodes[id] : null)
        {
            if (current.Owner == caller)
            {
                return All;
            }

            foreach (var grant in current.Grants.Values)
            {
                if (grant.Principal
                    .AsSpan(MemberPrefix.Length)
                    .SequenceEqual(caller.AsSpan()))
                {
                    for (var index = 0; index < grant.Privileges.Length; index++)
                    {
                        effective |= Privilege(grant.Privileges[index]);
                    }
                }
            }
        }

        return effective;
    }

    private string[] Privileges(ReferenceMemoryNode node, string caller)
    {
        var effective = EffectivePrivileges(node, caller);
        var result = new string[System.Numerics.BitOperations.PopCount((uint)effective)];
        var position = 0;

        for (var index = 0; index < AllPrivileges.Length; index++)
        {
            if ((effective & (1 << index)) != 0)
            {
                result[position++] = AllPrivileges[index];
            }
        }

        return result;
    }

    private Wire.Memory View(ReferenceMemoryNode node, string caller)
    {
        var ancestors = new List<string>();

        for (var parent = node.ParentId; parent is not null; parent = _nodes[parent].ParentId)
        {
            if ((EffectivePrivileges(_nodes[parent], caller) & Read) != 0)
            {
                ancestors.Add(parent);
            }
        }

        ancestors.Reverse();
        return new() { Id = node.Id, Name = node.Name, Description = node.Description, ParentId = node.ParentId is { } id && (EffectivePrivileges(_nodes[id], caller) & Read) != 0 ? id : null, Ancestors = ancestors.ToArray(), Restricted = node.Restricted, Provider = ProviderId, Privileges = Privileges(node, caller), CreatedAt = node.CreatedAt };
    }

    private List<ReferenceMemoryNode> Descendants(string id, long? depth)
    {
        var result = new List<ReferenceMemoryNode> { _nodes[id] };
        var queue = new Queue<(string Id, long Depth)>();
        queue.Enqueue((id, 0));

        while (queue.TryDequeue(out var parent))
        {
            if (depth is { } limit && parent.Depth >= limit)
            {
                continue;
            }

            foreach (var node in _nodes.Values)
            {
                if (node.ParentId == parent.Id)
                {
                    result.Add(node);
                    queue.Enqueue((node.Id, parent.Depth + 1));
                }
            }
        }

        return result;
    }

    private static string Caller(UhpRequestContext context) => context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Memory operations require a stable principal.");
    private static UhpServerException Invalid(string message) => ReferenceBackend.Failure(422, "memory_invalid", message);
    public void Dispose() => _gate.Dispose();
}
