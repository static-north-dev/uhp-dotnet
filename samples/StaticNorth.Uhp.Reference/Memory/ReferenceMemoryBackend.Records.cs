using System.Globalization;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;
using StaticNorth.Uhp.TestSupport.Memory;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

public sealed partial class ReferenceMemoryBackend
{
    public async Task<Wire.MemoryRecord> RememberRecordAsync(string memoryId, Wire.MemoryRecordCreate request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "write");
        RejectFiles(request.Content);
        var references = await ValidateReferencesAsync(request.References, node, caller, ct).ConfigureAwait(false);
        var time = Time(request.Time);
        ReferenceMemoryRecord stored;

        try
        {
            stored = await provider.RememberAsync(node.Owner, memoryId, new(request.Content ?? default)
            {
                Type = request.Type ?? "note",
                Title = request.Title,
                Attributes = request.Attributes,
                References = references
            }, ct, caller).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            throw Invalid("Invalid memory record content, title, or attributes.");
        }

        _recordMetadata[(stored.Id, stored.Version)] = new(time, Member(caller));
        var closed = new List<string>();
        var conflicts = new List<string>();
        var result = Record(stored);

        if (IsSingleValue(result))
        {
            var previous = await provider.ListAsync(node.Owner, memoryId, ct).ConfigureAwait(false);
            var start = Instant(result.Time?.ValidFrom);
            DateTimeOffset? nextStart = null;

            for (var index = 0; index < previous.Count; index++)
            {
                var old = Record(previous[index]);

                if (old.Id == result.Id || !SameSlot(old, result))
                {
                    continue;
                }

                var oldStart = Instant(old.Time?.ValidFrom);

                if (oldStart == start)
                {
                    conflicts.Add(old.Id);
                    await AddContradictionAsync(node, old, result.Id, caller, ct).ConfigureAwait(false);
                    await AddContradictionAsync(node, result, old.Id, caller, ct).ConfigureAwait(false);
                    result = Record(await GetStoredAsync(node, stored.Id, ct).ConfigureAwait(false));
                }
                else if (oldStart < start && (old.Time?.ValidTo is null || Instant(old.Time.ValidTo) > start))
                {
                    await ReviseInternalAsync(node, old, new(), old.Time! with { ValidTo = time.ValidFrom }, caller, ct).ConfigureAwait(false);
                    closed.Add(old.Id);
                }
                else if (oldStart > start && (nextStart is null || oldStart < nextStart))
                {
                    nextStart = oldStart;
                }
            }

            if (nextStart is { } end)
            {
                _recordMetadata[(result.Id, result.Version!.Value)] = new(time with { ValidTo = end.ToString("O", CultureInfo.InvariantCulture) }, result.WrittenBy!);
                result = Record(await GetStoredAsync(node, result.Id, ct).ConfigureAwait(false));
            }
        }

        foreach (var reference in result.References ?? [])
        {
            if (reference.Rel == "contradicts")
            {
                var targetNode = Require(reference.MemoryId ?? memoryId, caller, "write");
                var target = Record(await GetStoredAsync(targetNode, reference.RecordId, ct).ConfigureAwait(false));
                await AddContradictionAsync(targetNode, target, result.Id, caller, ct, memoryId).ConfigureAwait(false);
            }
        }

        return VisibleRecord(result with { Closed = IsSingleValue(result) ? closed.ToArray() : null, Conflicts = IsSingleValue(result) ? conflicts.ToArray() : null }, caller);
    }

    public async Task<Wire.MemoryRecord> GetMemoryRecordAsync(string memoryId, string recordId, Wire.GetMemoryRecordQuery query, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "read");
        var versions = await HistoryAsync(node, recordId, ct).ConfigureAwait(false);
        var selected = versions[^1];

        if (query.AsOf is { } asOf)
        {
            var instant = Instant(asOf);
            selected = versions.LastOrDefault(version => version.WrittenAt <= instant) ?? throw UhpServerException.NotFound("memory_record_not_found");
        }

        return VisibleRecord(Record(selected), caller);
    }

    public async Task<Wire.MemoryRecordList> GetMemoryRecordHistoryAsync(string memoryId, string recordId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "read");
        var history = await HistoryAsync(node, recordId, ct).ConfigureAwait(false);
        return new() { Data = history.Select(value => VisibleRecord(Record(value), caller)).ToArray() };
    }

    public async Task<Wire.MemoryRecord> ReviseMemoryRecordAsync(string memoryId, string recordId, Wire.MemoryRecordRevise request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "write");
        RejectFiles(request.Content);
        var previous = Record(await GetStoredAsync(node, recordId, ct).ConfigureAwait(false));
        var references = request.References is null ? null : await ValidateReferencesAsync(
            request.References.Where(static reference => reference.Available != false).ToArray(), node, caller, ct).ConfigureAwait(false);

        if (references is not null && previous.References is { Length: > 0 } oldReferences)
        {
            List<JsonElement>? retained = null;

            for (var index = 0; index < oldReferences.Length; index++)
            {
                var reference = oldReferences[index];

                if (!CanRead(reference.MemoryId ?? memoryId, caller))
                {
                    retained ??= new(references);
                    retained.Add(JsonSerializer.SerializeToElement(reference, Wire.UhpContractJsonContext.Default.MemoryReference));
                }
            }

            if (retained is not null)
            {
                references = retained;
            }
        }

        try
        {
            var next = await provider.ReviseAsync(node.Owner, memoryId, recordId, new()
            {
                Content = request.Content,
                Title = request.Title,
                Attributes = request.Attributes,
                References = references
            }, ct, caller).ConfigureAwait(false);
            _recordMetadata[(recordId, next.Version)] = new(request.Time is null ? previous.Time! : Time(request.Time), Member(caller));
            return VisibleRecord(Record(next), caller);
        }
        catch (ArgumentException)
        {
            throw Invalid("Invalid memory revision.");
        }
        catch (KeyNotFoundException)
        {
            throw UhpServerException.NotFound("memory_record_not_found");
        }
    }

    public async Task<Wire.MemoryRecord> ForgetMemoryRecordAsync(string memoryId, string recordId, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "write");
        var previous = Record(await GetStoredAsync(node, recordId, ct).ConfigureAwait(false));
        await provider.ForgetAsync(node.Owner, memoryId, recordId, ct, caller).ConfigureAwait(false);
        var next = await GetStoredAsync(node, recordId, ct).ConfigureAwait(false);
        _recordMetadata[(recordId, next.Version)] = new(previous.Time!, Member(caller));
        return VisibleRecord(Record(next), caller);
    }

    public async Task<Wire.MemoryErasure> EraseMemoryRecordsAsync(string memoryId, Wire.MemoryErase request, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var node = Require(memoryId, Caller(context), "delete");
        var erased = new List<string>();

        foreach (var id in request.RecordIds ?? [])
        {
            await GetStoredAsync(node, id, ct).ConfigureAwait(false);
            await provider.EraseAsync(node.Owner, memoryId, id, ct).ConfigureAwait(false);
            erased.Add(id);
            var metadata = _recordMetadata.Keys.Where(key => key.Record == id).ToArray();

            foreach (var key in metadata)
            {
                _recordMetadata.Remove(key);
            }
        }

        return new() { Erased = erased.ToArray(), Unreachable = [] };
    }

    public async Task<Wire.MemoryRecordList> ListMemoryRecordsAsync(string memoryId, Wire.ListMemoryRecordsQuery query, UhpRequestContext context, CancellationToken ct)
    {
        using var lease = await EnterAsync(ct).ConfigureAwait(false);
        var caller = Caller(context);
        var node = Require(memoryId, caller, "read");
        var records = await provider.ListAsync(node.Owner, memoryId, ct).ConfigureAwait(false);
        return new() { Data = records.Where(record => query.Type is null || record.Type == query.Type).Take((int)(query.Limit ?? 100)).Select(value => VisibleRecord(Record(value), caller)).ToArray() };
    }

    private async Task<IReadOnlyList<ReferenceMemoryRecord>> HistoryAsync(ReferenceMemoryNode node, string id, CancellationToken ct)
    {
        try
        {
            return await provider.HistoryAsync(node.Owner, node.Id, id, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            throw UhpServerException.NotFound("memory_record_not_found");
        }
    }

    private async Task<ReferenceMemoryRecord> GetStoredAsync(ReferenceMemoryNode node, string id, CancellationToken ct) => (await HistoryAsync(node, id, ct).ConfigureAwait(false))[^1];

    private Wire.MemoryRecord Record(ReferenceMemoryRecord stored)
    {
        _recordMetadata.TryGetValue((stored.Id, stored.Version), out var metadata);
        return new()
        {
            Id = stored.Id,
            MemoryId = stored.MemoryId,
            Type = stored.Type,
            Title = stored.Title,
            Content = stored.Content.Select(part => part.Deserialize(Wire.UhpContractJsonContext.Default.MemoryContentPart)!).ToArray(),
            Attributes = stored.Attributes.ToDictionary(),
            References = stored.References.Select(part => part.Deserialize(Wire.UhpContractJsonContext.Default.MemoryReference)!).ToArray(),
            Version = stored.Version,
            Status = stored.Status,
            Supersedes = stored.Supersedes,
            WrittenBy = metadata?.Writer ?? Member(stored.WrittenBy),
            Time = (metadata?.Time ?? new()) with { WrittenAt = stored.WrittenAt.ToString("O", CultureInfo.InvariantCulture) }
        };
    }

    private bool CanRead(string memory, string caller) => _nodes.TryGetValue(memory, out var node) && (EffectivePrivileges(node, caller) & Read) != 0;

    private Wire.MemoryRecord VisibleRecord(Wire.MemoryRecord record, string caller)
    {
        if (record.References is not { Length: > 0 } references)
        {
            return record;
        }

        Wire.MemoryReference[]? projected = null;

        for (var index = 0; index < references.Length; index++)
        {
            var reference = references[index];

            if (!CanRead(reference.MemoryId ?? record.MemoryId, caller))
            {
                projected ??= (Wire.MemoryReference[])references.Clone();
                projected[index] = new() { RecordId = reference.RecordId, Available = false };
            }
        }

        return projected is null ? record : record with { References = projected };
    }

    private async Task<IReadOnlyList<JsonElement>> ValidateReferencesAsync(Wire.MemoryReference[]? references, ReferenceMemoryNode node, string caller, CancellationToken ct)
    {
        if (references is null)
        {
            return [];
        }

        var result = new JsonElement[references.Length];

        for (var index = 0; index < references.Length; index++)
        {
            var reference = references[index];
            var target = Require(reference.MemoryId ?? node.Id, caller, reference.Rel == "contradicts" ? "write" : "read");
            await GetStoredAsync(target, reference.RecordId, ct).ConfigureAwait(false);
            result[index] = JsonSerializer.SerializeToElement(reference, Wire.UhpContractJsonContext.Default.MemoryReference);
        }

        return result;
    }

    private async Task AddContradictionAsync(ReferenceMemoryNode node, Wire.MemoryRecord record, string otherId, string caller, CancellationToken ct, string? otherMemory = null)
    {
        if (Array.Exists(record.References ?? [], reference => reference.Rel == "contradicts" && reference.RecordId == otherId))
        {
            return;
        }

        var references = new List<JsonElement>();

        foreach (var reference in record.References ?? [])
        {
            references.Add(JsonSerializer.SerializeToElement(reference, Wire.UhpContractJsonContext.Default.MemoryReference));
        }

        references.Add(JsonSerializer.SerializeToElement(new Wire.MemoryReference { Rel = "contradicts", RecordId = otherId, MemoryId = otherMemory ?? node.Id }, Wire.UhpContractJsonContext.Default.MemoryReference));
        await ReviseInternalAsync(node, record, new() { References = references }, record.Time!, caller, ct).ConfigureAwait(false);
    }

    private async Task ReviseInternalAsync(ReferenceMemoryNode node, Wire.MemoryRecord record, ReferenceMemoryPatch patch, Wire.MemoryRecordTime time, string caller, CancellationToken ct)
    {
        var next = await provider.ReviseAsync(node.Owner, node.Id, record.Id, patch, ct, ProviderId).ConfigureAwait(false);
        _recordMetadata[(next.Id, next.Version)] = new(time, new() { Kind = "provider", Id = ProviderId, OnBehalfOf = caller });
    }

    private static Wire.MemoryWriter Member(string caller) => new() { Kind = "member", Id = caller };
    private static void RejectFiles(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Array } parts)
        {
            return;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var type) && type.ValueEquals("file"))
            {
                throw ReferenceBackend.Failure(422, "memory_unsupported", "This provider declares text content only.");
            }
        }
    }
    private static Wire.MemoryRecordTime Time(Dictionary<string, JsonElement>? value)
    {
        var result = value is null ? new Wire.MemoryRecordTime() : JsonSerializer.SerializeToElement(value, Wire.UhpContractJsonContext.Default.DictionaryStringJsonElement).Deserialize(Wire.UhpContractJsonContext.Default.MemoryRecordTime)!;
        _ = result.ValidFrom is { } start ? Instant(start) : default;
        _ = result.ValidTo is { } end ? Instant(end) : default;
        return result;
    }

    private static DateTimeOffset Instant(string? value) => value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : throw Invalid("A valid ISO timestamp is required.");
    private static bool IsSingleValue(Wire.MemoryRecord record) => record.Type == "fact" && record.Attributes is { } attributes && attributes.TryGetValue("holds", out var holds) && holds.ValueKind == JsonValueKind.String && holds.ValueEquals("one") && record.Time?.ValidFrom is not null;
    private static bool SameSlot(Wire.MemoryRecord left, Wire.MemoryRecord right) => IsSingleValue(left) && left.Attributes?.GetValueOrDefault("predicate").GetRawText() == right.Attributes?.GetValueOrDefault("predicate").GetRawText() && Subject(left) == Subject(right);
    private static string? Subject(Wire.MemoryRecord record) => Array.Find(record.References ?? [], reference => reference.Rel == "subject")?.RecordId;
}
