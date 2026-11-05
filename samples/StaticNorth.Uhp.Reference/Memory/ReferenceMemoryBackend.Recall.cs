using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

public sealed partial class ReferenceMemoryBackend
{
    public async Task<Wire.MemoryRecall> RecallMemoryAsync(string memoryId, Wire.MemoryRecallRequest request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var root = Require(memoryId, caller, "read");

        if (request.Query is null && request.Text is null && request.Filters is null || request.Limit is < 1 or > 1000 || request.Depth < 0)
        {
            throw Invalid("Recall requires a signal and valid bounds.");
        }

        var degraded = new List<string>();

        if (request.Query is not null)
        {
            degraded.Add("query:not_supported");
        }

        if (request.Filters is not null)
        {
            degraded.Add("filters:not_supported");
        }

        var candidates = await CandidatesAsync(root, caller, request.Depth, ct).ConfigureAwait(false);
        var selected = new List<Wire.MemoryRecallResultsItem>();

        for (var index = 0; index < candidates.Count; index++)
        {
            var record = candidates[index];

            if (request.Types is not null && !request.Types.Contains(record.Type, StringComparer.Ordinal))
            {
                continue;
            }

            var score = Score(record, request.Text);

            if (score > 0)
            {
                selected.Add(Hit(record, caller, score, ["text"]));
            }
        }

        selected.Sort(static (left, right) => Nullable.Compare(right.Score, left.Score));
        var limit = (int)(request.Limit ?? 20);

        if (selected.Count > limit)
        {
            selected.RemoveRange(limit, selected.Count - limit);
        }

        var primaryCount = selected.Count;
        var included = selected.Select(result => result.Record.Id).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < primaryCount; index++)
        {
            foreach (var reference in selected[index].Record.References ?? [])
            {
                if (reference.Rel != "contradicts" || included.Contains(reference.RecordId))
                {
                    continue;
                }

                var other = candidates.Find(record => record.Id == reference.RecordId && record.MemoryId == (reference.MemoryId ?? selected[index].Record.MemoryId));

                if (other is not null)
                {
                    included.Add(other.Id);
                    selected.Add(Hit(other, caller, null, ["contradicts"]));
                }
            }
        }

        return new()
        {
            Results = selected.ToArray(),
            Degraded = degraded.ToArray(),
            Abstain = selected.Count == 0,
            Parent = root.ParentId is { } parent && (EffectivePrivileges(_nodes[parent], caller) & Read) != 0 ? Neighbour(_nodes[parent], caller) : null,
            Children = _nodes.Values.Where(node => node.ParentId == root.Id && (EffectivePrivileges(node, caller) & Read) != 0).Select(node => Neighbour(node, caller)).ToArray()
        };
    }

    public async Task<Wire.MemoryPrime> PrimeMemoryAsync(string memoryId, Wire.PrimeMemoryQuery query, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var root = Require(memoryId, caller, "read");
        var candidates = await CandidatesAsync(root, caller, query.Depth ?? 0, ct).ConfigureAwait(false);
        candidates.RemoveAll(static record => record.Attributes?.TryGetValue("standing", out var standing) != true || standing.ValueKind != JsonValueKind.True);
        candidates.Sort(static (left, right) => string.CompareOrdinal(right.Time?.WrittenAt, left.Time?.WrittenAt));
        var result = new List<Wire.MemoryRecord>();
        long tokens = 0;
        var budget = query.Budget ?? 4000;

        if (budget < 0)
        {
            throw Invalid("A prime budget cannot be negative.");
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            var record = candidates[index];
            var cost = Math.Max(1, (RecordText(record).Length + 3) / 4);

            if (result.Count > 0 && tokens + cost > budget)
            {
                break;
            }

            result.Add(VisibleRecord(record, caller));
            tokens += cost;
        }

        return new() { Records = result.ToArray(), Tokens = tokens, Truncated = result.Count != candidates.Count };
    }

    public async Task<Wire.MemoryGraph> GraphMemoryAsync(string memoryId, Wire.MemoryGraphRequest request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var root = Require(memoryId, caller, "read");
        var candidates = await CandidatesAsync(root, caller, null, ct).ConfigureAwait(false);
        var byId = candidates.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var edges = new List<Wire.MemoryGraphEdgesItem>();

        foreach (var record in candidates)
        {
            foreach (var reference in record.References ?? [])
            {
                var targetMemory = reference.MemoryId ?? record.MemoryId;

                if (!CanRead(targetMemory, caller))
                {
                    continue;
                }

                edges.Add(new()
                {
                    From = new() { MemoryId = record.MemoryId, RecordId = record.Id },
                    To = new() { MemoryId = targetMemory, RecordId = reference.RecordId },
                    Rel = reference.Rel,
                    Available = byId.TryGetValue(reference.RecordId, out var target) && target.MemoryId == targetMemory
                });
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);

        if (request.Around is { } around)
        {
            if (!byId.ContainsKey(around))
            {
                throw UhpServerException.NotFound("memory_record_not_found");
            }

            var frontier = new List<string> { around };
            visited.Add(around);

            for (long hop = 0; hop < (request.Hops ?? 1); hop++)
            {
                var next = new List<string>();

                foreach (var id in frontier)
                {
                    foreach (var edge in edges)
                    {
                        var other = edge.Available && edge.From.RecordId == id ? edge.To.RecordId : edge.Available && edge.To.RecordId == id ? edge.From.RecordId : null;

                        if (other is not null && visited.Add(other))
                        {
                            next.Add(other);
                        }
                    }
                }

                frontier = next;
            }
        }
        else
        {
            visited.UnionWith(byId.Keys);
        }

        var limit = (int)(request.Limit ?? 1000);
        var nodes = candidates.Where(record => visited.Contains(record.Id)).Take(limit).Select(record => new Wire.MemoryGraphNodesItem
        {
            Record = record,
            Memory = new() { Id = record.MemoryId, Name = _nodes[record.MemoryId].Name }
        }).ToArray();
        var returned = nodes.Select(node => node.Record.Id).ToHashSet(StringComparer.Ordinal);
        return new() { Nodes = nodes, Edges = edges.Where(edge => returned.Contains(edge.From.RecordId) && (!edge.Available || returned.Contains(edge.To.RecordId))).ToArray(), Truncated = nodes.Length < visited.Count, Degraded = [] };
    }

    private async Task<List<Wire.MemoryRecord>> CandidatesAsync(ReferenceMemoryNode root, string caller, long? depth, CancellationToken ct)
    {
        var result = new List<Wire.MemoryRecord>();

        foreach (var node in Descendants(root.Id, depth))
        {
            if ((EffectivePrivileges(node, caller) & Read) == 0)
            {
                continue;
            }

            var records = await provider.ListAsync(node.Owner, node.Id, ct).ConfigureAwait(false);

            for (var index = 0; index < records.Count; index++)
            {
                result.Add(Record(records[index]));
            }
        }

        return result;
    }

    private Wire.MemoryRecallResultsItem Hit(Wire.MemoryRecord record, string caller, double? score, string[] why) => new()
    {
        Record = VisibleRecord(record, caller),
        Score = score,
        Why = why,
        Memory = new() { Id = record.MemoryId, Name = _nodes[record.MemoryId].Name }
    };

    private Wire.MemoryNeighbour Neighbour(ReferenceMemoryNode node, string caller) => new() { Id = node.Id, Name = node.Name, Description = node.Description, Privileges = Privileges(node, caller) };
    private static string RecordText(Wire.MemoryRecord record) => record.Title + " " + string.Join(' ', (record.Content ?? []).Select(part => part.Text));
    private static double Score(Wire.MemoryRecord record, string? text)
    {
        if (text is null)
        {
            return 1;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = RecordText(record);
        var hits = 0;

        foreach (var word in words)
        {
            if (content.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                hits++;
            }
        }

        return hits;
    }
}
