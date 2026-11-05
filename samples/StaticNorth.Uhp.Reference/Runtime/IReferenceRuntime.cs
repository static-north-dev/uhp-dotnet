using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

public interface IReferenceRuntime
{
    string Base { get; }
    string DefaultModel { get; }
    string WorkspaceRoot { get; }
    bool ProducesArtifacts => false;
    Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct);
    Task<ReferenceRuntimeResult> RunAsync(ReferenceRuntimeRequest request, Func<string, ValueTask> textDelta, CancellationToken ct);
    void ValidateSession(string sessionId)
    {
    }

    ValueTask DeleteSessionAsync(string sessionId, CancellationToken ct) => ValueTask.CompletedTask;
}
