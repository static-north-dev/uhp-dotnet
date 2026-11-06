using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Client.Tests;

internal sealed class FailingReferenceRuntime : IUhpHarnessProvider
{
    public string Base => "failing";
    public string DefaultModel => "failing-model";
    public string WorkspaceRoot => Path.GetTempPath();
    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<UhpHarnessRunResult> RunAsync(UhpHarnessRunRequest request, Func<string, ValueTask> textDelta, CancellationToken ct) => throw new IOException("RPC channel closed.");
}
