using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

public sealed partial class ReferenceMemoryBackend
{
    public Task<Wire.MemoryRecordList> ObserveMemoryAsync(string memoryId, Wire.MemoryObserve request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryRecordList>(memoryId, context, ct);

    public Task<Wire.MemoryJob> GetMemoryJobAsync(string memoryId, string jobId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryJob>(memoryId, context, ct);

    public Task<UhpBinaryContent> GetMemoryRecordFileAsync(string memoryId, string recordId, string index, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<UhpBinaryContent>(memoryId, context, ct);

    public Task<Wire.MemoryOperationResult> OperateMemoryRecordAsync(string memoryId, string recordId, string name, Dictionary<string, JsonElement> request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryOperationResult>(memoryId, context, ct);

    public Task<Wire.MemoryQueryList> ListMemoryQueriesAsync(string memoryId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryQueryList>(memoryId, context, ct);

    public Task<Wire.MemoryRecall> RunMemoryQueryAsync(string memoryId, string name, Wire.MemoryQueryRun request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryRecall>(memoryId, context, ct);

    public Task<Wire.MemoryQuery> DefineMemoryQueryAsync(string memoryId, string name, Wire.MemoryQuery request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryQuery>(memoryId, context, ct);

    public Task<Wire.MemoryRecall> FreeMemoryQueryAsync(string memoryId, Wire.MemoryFreeQuery request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryRecall>(memoryId, context, ct);

    public Task<Wire.MemorySnapshotList> ListMemorySnapshotsAsync(string memoryId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemorySnapshotList>(memoryId, context, ct);

    public Task<Wire.MemorySnapshot> CreateMemorySnapshotAsync(string memoryId, Wire.MemorySnapshotCreate request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemorySnapshot>(memoryId, context, ct);

    public Task<Wire.MemoryConsolidationList> ListMemoryConsolidationsAsync(string memoryId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryConsolidationList>(memoryId, context, ct);

    public Task<Wire.MemoryConsolidation> StartMemoryConsolidationAsync(string memoryId, Wire.MemoryConsolidationStart request, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryConsolidation>(memoryId, context, ct);

    public Task<Wire.MemoryConsolidation> GetMemoryConsolidationAsync(string memoryId, string runId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryConsolidation>(memoryId, context, ct);

    public Task<Wire.MemoryRecordList> ListMemoryConsolidationChangesAsync(string memoryId, string runId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryRecordList>(memoryId, context, ct);

    public Task<Wire.MemoryConsolidation> RevertMemoryConsolidationAsync(string memoryId, string runId, UhpRequestContext context, CancellationToken ct) => UnsupportedAsync<Wire.MemoryConsolidation>(memoryId, context, ct);

}
