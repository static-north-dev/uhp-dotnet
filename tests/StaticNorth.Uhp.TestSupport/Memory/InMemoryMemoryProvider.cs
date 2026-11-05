using System.Collections.Frozen;
using System.Text.Json;

namespace StaticNorth.Uhp.TestSupport.Memory;

/// <summary>Stateful, process-local reference storage. Data is discarded when this instance is released.</summary>
public sealed class InMemoryMemoryProvider(TimeProvider? clock = null) : IReferenceMemoryProvider
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, InMemoryMemoryState> _memories = new(StringComparer.Ordinal);

    public ValueTask<string> CreateMemoryAsync(string owner, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            var id = $"hmem_{Guid.NewGuid():N}";
            _memories.Add(id, new InMemoryMemoryState(owner));
            return ValueTask.FromResult(id);
        }
    }

    public ValueTask DeleteMemoryAsync(string owner, string memoryId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            FindMemory(owner, memoryId, ct);
            _memories.Remove(memoryId);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<ReferenceMemoryRecord> RememberAsync(string owner, string memoryId, ReferenceMemoryWrite input, CancellationToken ct = default, string? writer = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Type);

        if (input.Title?.Length > 300)
        {
            throw new ArgumentException("Memory titles cannot exceed 300 characters.", nameof(input));
        }

        lock (_gate)
        {
            var memory = FindMemory(owner, memoryId, ct);
            var content = OwnContent(input.Content, input.Title);
            var record = new ReferenceMemoryRecord(
                $"hrec_{Guid.NewGuid():N}", memoryId, input.Type, input.Title, content,
                OwnAttributes(input.Attributes), OwnReferences(input.References), writer ?? owner, _clock.GetUtcNow());
            memory.Records.Add(record.Id, [record]);
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<ReferenceMemoryRecord> GetAsync(string owner, string memoryId, string recordId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var current = FindVersions(FindMemory(owner, memoryId, ct), recordId)[^1];
            RequireActive(current);
            return ValueTask.FromResult(current);
        }
    }

    public ValueTask<IReadOnlyList<ReferenceMemoryRecord>> ListAsync(string owner, string memoryId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var memory = FindMemory(owner, memoryId, ct);
            var records = new List<ReferenceMemoryRecord>(memory.Records.Count);

            foreach (var entry in memory.Records)
            {
                var current = entry.Value[^1];

                if (current.Status == ReferenceMemoryValues.Active)
                {
                    records.Add(current);
                }
            }

            return ValueTask.FromResult<IReadOnlyList<ReferenceMemoryRecord>>(records.AsReadOnly());
        }
    }

    public ValueTask<ReferenceMemoryRecord> ReviseAsync(string owner, string memoryId, string recordId, ReferenceMemoryPatch patch, CancellationToken ct = default, string? writer = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        lock (_gate)
        {
            var versions = FindVersions(FindMemory(owner, memoryId, ct), recordId);
            var previous = versions[^1];
            RequireActive(previous);
            var revised = previous with
            {
                Content = patch.Content is { } content ? OwnContent(content, previous.Title) : previous.Content,
                Attributes = patch.Attributes is { } attributes ? OwnAttributes(attributes) : previous.Attributes,
                References = patch.References is { } references ? OwnReferences(references) : previous.References,
                WrittenBy = writer ?? owner,
                Title = patch.Title ?? previous.Title,
                WrittenAt = _clock.GetUtcNow(),
                Version = checked(previous.Version + 1),
                Supersedes = previous.Version
            };
            Append(versions, revised);
            return ValueTask.FromResult(revised);
        }
    }

    public ValueTask<IReadOnlyList<ReferenceMemoryRecord>> HistoryAsync(string owner, string memoryId, string recordId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var versions = FindVersions(FindMemory(owner, memoryId, ct), recordId);
            return ValueTask.FromResult<IReadOnlyList<ReferenceMemoryRecord>>(Array.AsReadOnly(versions.ToArray()));
        }
    }

    public ValueTask ForgetAsync(string owner, string memoryId, string recordId, CancellationToken ct = default, string? writer = null)
    {
        lock (_gate)
        {
            var versions = FindVersions(FindMemory(owner, memoryId, ct), recordId);
            var previous = versions[^1];

            if (previous.Status != ReferenceMemoryValues.Forgotten)
            {
                Append(versions, previous with
                {
                    Status = ReferenceMemoryValues.Forgotten,
                    WrittenBy = writer ?? owner,
                    WrittenAt = _clock.GetUtcNow(),
                    Version = checked(previous.Version + 1),
                    Supersedes = previous.Version
                });
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask EraseAsync(string owner, string memoryId, string recordId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        lock (_gate)
        {
            var memory = FindMemory(owner, memoryId, ct);
            memory.Records.Remove(recordId);
            return ValueTask.CompletedTask;
        }
    }

    private InMemoryMemoryState FindMemory(string owner, string memoryId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

        if (!_memories.TryGetValue(memoryId, out var memory) || memory.Owner != owner)
        {
            throw new KeyNotFoundException("Memory not found.");
        }

        return memory;
    }

    private static List<ReferenceMemoryRecord> FindVersions(InMemoryMemoryState memory, string recordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        if (!memory.Records.TryGetValue(recordId, out var versions))
        {
            throw new KeyNotFoundException("Memory record not found.");
        }

        return versions;
    }

    private static void RequireActive(ReferenceMemoryRecord record)
    {
        if (record.Status != ReferenceMemoryValues.Active)
        {
            throw new KeyNotFoundException("Memory record not found.");
        }
    }

    private static void Append(List<ReferenceMemoryRecord> versions, ReferenceMemoryRecord next)
    {
        versions[^1] = versions[^1] with { Status = ReferenceMemoryValues.Superseded };
        versions.Add(next);
    }

    private static IReadOnlyList<JsonElement> OwnContent(JsonElement content, string? title)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var text = new ReferenceMemoryTextPart(content.GetString());
            var part = JsonSerializer.SerializeToElement(text, ReferenceMemoryJsonContext.Default.ReferenceMemoryTextPart);
            return Array.AsReadOnly<JsonElement>([part]);
        }

        if (content.ValueKind == JsonValueKind.Undefined && !string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<JsonElement>();
        }

        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0 && string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Memory content requires parts or a title.", nameof(content));
        }

        var parts = new JsonElement[content.GetArrayLength()];

        for (var index = 0; index < parts.Length; index++)
        {
            var part = content[index];

            ValidatePart(part);
            parts[index] = part.Clone();
        }

        return Array.AsReadOnly(parts);
    }

    private static void ValidatePart(JsonElement part)
    {
        if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty(ReferenceMemoryValues.Type, out var type) || type.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("Every memory part requires a type.", nameof(part));
        }

        var isText = type.ValueEquals(ReferenceMemoryValues.Text);
        var isFile = type.ValueEquals(ReferenceMemoryValues.File);

        if (!isText && !isFile)
        {
            var kind = type.GetString()!;

            if (kind.StartsWith("x.", StringComparison.Ordinal) && kind.Length > 2)
            {
                return;
            }

            throw new ArgumentException("Unknown memory part types must use the x. prefix.", nameof(part));
        }

        if (isText && (!part.TryGetProperty(ReferenceMemoryValues.Text, out var text) || text.ValueKind != JsonValueKind.String))
        {
            throw new ArgumentException("Text parts require a text string.", nameof(part));
        }

        if (part.TryGetProperty("role", out var role) && (role.ValueKind != JsonValueKind.String || !(role.ValueEquals("user") || role.ValueEquals("assistant") || role.ValueEquals("system") || role.ValueEquals("tool"))))
        {
            throw new ArgumentException("Invalid memory content role.", nameof(part));
        }

        if (part.TryGetProperty(ReferenceMemoryValues.TextSource, out var source) && (source.ValueKind != JsonValueKind.String || !(source.ValueEquals(ReferenceMemoryValues.Stated) || source.ValueEquals(ReferenceMemoryValues.Derived))))
        {
            throw new ArgumentException("Invalid memory text source.", nameof(part));
        }

        ValidateOptionalString(part, ReferenceMemoryValues.Text);
        ValidateOptionalString(part, ReferenceMemoryValues.Name);

        if (isFile)
        {
            if (!part.TryGetProperty(ReferenceMemoryValues.File, out var file) || file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.ValueEquals(""))
            {
                throw new ArgumentException("File parts require a file ID.", nameof(part));
            }

            ValidateOptionalString(file, ReferenceMemoryValues.Name);
            ValidateOptionalString(file, "media_type");
            ValidateOptionalString(file, "version");

            if (file.TryGetProperty("bytes", out var bytes) && (bytes.ValueKind != JsonValueKind.Number || !bytes.TryGetInt64(out var size) || size < 0))
            {
                throw new ArgumentException("File byte counts must be nonnegative integers.", nameof(part));
            }
        }
    }

    private static void ValidateOptionalString(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("Memory content field must be a string: " + name + ".");
        }
    }

    private static FrozenDictionary<string, JsonElement> OwnAttributes(Dictionary<string, JsonElement>? source)
    {
        if (source is null || source.Count == 0)
        {
            return FrozenDictionary<string, JsonElement>.Empty;
        }

        return source.ToFrozenDictionary(static entry => entry.Key, static entry => entry.Value.Clone(), StringComparer.Ordinal);
    }

    private static IReadOnlyList<JsonElement> OwnReferences(IReadOnlyList<JsonElement>? source)
    {
        if (source is null || source.Count == 0)
        {
            return Array.Empty<JsonElement>();
        }

        var copy = new JsonElement[source.Count];

        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = source[index].Clone();
        }

        return Array.AsReadOnly(copy);
    }
}
