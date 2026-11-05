using System.Collections.Concurrent;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Harnesses.OpenCode;

namespace StaticNorth.Uhp.Reference;

public sealed class OpenCodeRuntime(OpenCodeConnection connection, string stateRoot, string provider, string model) : IReferenceRuntime
{
    private readonly ConcurrentDictionary<string, (string? Id, bool Ready)> _sessions = new(StringComparer.Ordinal);

    public string Base => "opencode";
    public string DefaultModel { get; } = provider + "/" + model;
    public string WorkspaceRoot { get; } = Path.Combine(stateRoot, "workspaces");
    public bool SupportsMemory => false;

    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var models = new UhpModelBackend(DefaultModel, [new(DefaultModel, true) { Backend = Base, IsDefault = true }]);
        return Task.FromResult(new UhpModelCatalog(new Dictionary<string, UhpModelBackend> { [Base] = models }));
    }

    public void ValidateTask(UhpTaskRequest task)
    {
        if (task.Model is { } requested && requested != DefaultModel)
        {
            throw ReferenceBackend.Failure(422, "model_unavailable", "The OpenCode harness cannot serve the requested model.");
        }

        ReferenceTextInput.Validate(task, "OpenCode");
    }

    public void ValidateSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session) && !session.Ready)
        {
            throw ReferenceBackend.Failure(409, "staticnorth_session_runtime_unavailable", "The native session could not be recovered after a runtime failure.");
        }
    }

    public async Task<ReferenceRuntimeResult> RunAsync(ReferenceRuntimeRequest request, Func<string, ValueTask> textDelta, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var session = _sessions.GetValueOrDefault(request.SessionId).Id;

        if (session is null)
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(request.Workspace);
            }
            else
            {
                Directory.CreateDirectory(request.Workspace, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            // Keep the native ID even if the caller cancels while OpenCode is creating its session.
            using var creation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            session = await connection.CreateSessionAsync(request.Workspace, creation.Token)
                .ConfigureAwait(false);
            _sessions[request.SessionId] = (session, true);
            ct.ThrowIfCancellationRequested();
        }

        _sessions[request.SessionId] = (session, false);
        var completed = await connection
            .RunTurnAsync(request.Workspace, session, provider, model, ReferenceTextInput.Prompt(request.Task.Input), request.Task.Instructions, textDelta, ct)
            .ConfigureAwait(false);
        _sessions[request.SessionId] = (session, true);

        if (!completed)
        {
            ct.ThrowIfCancellationRequested();
        }

        return new(DefaultModel, UhpResponseStatus.Completed);
    }

    public async ValueTask DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        var workspace = Path.Combine(WorkspaceRoot, sessionId);

        if (_sessions.TryGetValue(sessionId, out var session) && session.Id is { } nativeId)
        {
            _sessions[sessionId] = (nativeId, false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await connection.DeleteSessionAsync(workspace, nativeId, timeout.Token)
                .ConfigureAwait(false);
            _sessions.TryRemove(sessionId, out _);
        }

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
