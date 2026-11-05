namespace StaticNorth.Uhp.TestSupport.Memory;

/// <summary>Test/reference-host storage seam. The caller supplies an authenticated owner identity.</summary>
/// <remarks>Tree placement, grants and cross-memory authorization belong to the server facade.</remarks>
public interface IReferenceMemoryProvider
{
    ValueTask<string> CreateMemoryAsync(string owner, CancellationToken ct = default);
    ValueTask DeleteMemoryAsync(string owner, string memoryId, CancellationToken ct = default);
    ValueTask<ReferenceMemoryRecord> RememberAsync(string owner, string memoryId, ReferenceMemoryWrite input, CancellationToken ct = default, string? writer = null);
    ValueTask<ReferenceMemoryRecord> GetAsync(string owner, string memoryId, string recordId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ReferenceMemoryRecord>> ListAsync(string owner, string memoryId, CancellationToken ct = default);
    ValueTask<ReferenceMemoryRecord> ReviseAsync(string owner, string memoryId, string recordId, ReferenceMemoryPatch patch, CancellationToken ct = default, string? writer = null);
    ValueTask<IReadOnlyList<ReferenceMemoryRecord>> HistoryAsync(string owner, string memoryId, string recordId, CancellationToken ct = default);
    ValueTask ForgetAsync(string owner, string memoryId, string recordId, CancellationToken ct = default, string? writer = null);
    ValueTask EraseAsync(string owner, string memoryId, string recordId, CancellationToken ct = default);
}
