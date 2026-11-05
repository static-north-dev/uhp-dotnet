using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Reference;

namespace StaticNorth.Uhp.Client.Tests;

internal sealed class FailingReferenceRuntime : IReferenceRuntime
{
    public string Base => "failing";
    public string DefaultModel => "failing-model";
    public string WorkspaceRoot => Path.GetTempPath();
    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<ReferenceRuntimeResult> RunAsync(ReferenceRuntimeRequest request, Func<string, ValueTask> textDelta, CancellationToken ct) => throw new IOException("RPC channel closed.");
}
