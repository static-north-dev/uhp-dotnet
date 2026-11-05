using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server;

/// <summary>The host-owned, singleton execution and persistence boundary. Every operation must enforce principal ownership.</summary>
public interface IUhpServerBackend
{
    Task<IReadOnlyList<UhpHarness>> ListHarnessesAsync(UhpRequestContext context, CancellationToken ct);
    Task<UhpHarness> GetHarnessAsync(string harnessId, UhpRequestContext context, CancellationToken ct);
    Task<UhpModelCatalog> GetModelsAsync(UhpRequestContext context, CancellationToken ct);
    Task<UhpHarnessModels> GetHarnessModelsAsync(string harnessId, UhpRequestContext context, CancellationToken ct);

    /// <summary>Atomically accepts work and establishes replayable progress. Execution must outlive request scopes and readers.</summary>
    /// <remarks>The backend owns sequence numbers, exactly one initial and terminal event, retention, continuation, session concurrency and advertised idempotency. Include IgnoredFields in stored response metadata.</remarks>
    Task<UhpExecution> SubmitAsync(UhpSubmission submission, UhpRequestContext context, CancellationToken serverStopping);

    Task<UhpResponse> GetResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct);
    Task<UhpResponse> CancelResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct);
    Task<StaticNorth.Uhp.Core.UhpSessionCancelResult> CancelSessionAsync(string sessionId, UhpRequestContext context, CancellationToken ct);
}
