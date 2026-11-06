namespace StaticNorth.Uhp.Core.Providers;

/// <summary>Runs a native agent for a host that owns UHP harnesses, task acceptance, and session state.</summary>
public interface IUhpHarnessProvider
{
    string Base { get; }
    string DisplayName => Base;
    string DefaultModel { get; }
    string WorkspaceRoot { get; }
    bool ProducesArtifacts => false;
    bool SupportsMemory => false;

    Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
    Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct);

    /// <summary>Runs a turn after the host has authorized the harness, session, workspace, and attachments.</summary>
    Task<UhpHarnessRunResult> RunAsync(UhpHarnessRunRequest request, Func<string, ValueTask> textDelta, CancellationToken ct);

    void ValidateTask(UhpTaskRequest task)
    {
    }

    void ValidateSession(string sessionId)
    {
    }

    ValueTask DeleteSessionAsync(string sessionId, CancellationToken ct) => ValueTask.CompletedTask;
}
