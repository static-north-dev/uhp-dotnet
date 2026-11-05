using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

public interface IReferenceRuntime
{
    string Base { get; }
    string DefaultModel { get; }
    string WorkspaceRoot { get; }
    bool ProducesArtifacts => false;
    bool SupportsMemory => true;
    Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct);
    Task<ReferenceRuntimeResult> RunAsync(ReferenceRuntimeRequest request, Func<string, ValueTask> textDelta, CancellationToken ct);
    void ValidateTask(UhpTaskRequest task)
    {
    }

    void ValidateSession(string sessionId)
    {
    }

    ValueTask DeleteSessionAsync(string sessionId, CancellationToken ct) => ValueTask.CompletedTask;
}
