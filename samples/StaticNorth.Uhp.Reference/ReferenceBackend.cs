using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;

namespace StaticNorth.Uhp.Reference;

// In-memory reference infrastructure with an injected execution runtime; not a persistent storage package.
public partial class ReferenceBackend(IReferenceRuntime runtime, Environments.ReferenceEnvironmentBackend? environments = null, ClaudeCodeRuntime? claude = null) : IUhpServerBackend, IUhpSessionBackend, IUhpResponseStorageBackend, IDisposable
{
    private const string DefaultHarnessId = "chrn_fixture";
    private const string ClaudeHarnessId = "chrn_claude_code";
    private readonly IReferenceRuntime _runtime = runtime;
    private Memory.ReferenceMemoryBackend? _memories;
    private readonly long _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly object _gate = new();
    private readonly Dictionary<string, ReferenceExecution> _responses = [];
    private readonly Dictionary<string, ReferenceExecution> _active = [];
    private readonly Dictionary<(string Owner, string Key), (string Hash, ReferenceExecution State)> _keys = [];
    private readonly HashSet<string> _deletedResponses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletingSessions = new(StringComparer.Ordinal);
    private int _submissions;
    private int _cancellations;
    private int _disposed;

    public int SubmissionCount => Volatile.Read(ref _submissions);
    public int CancellationCount => Volatile.Read(ref _cancellations);
    public Uri? ArtifactOrigin { private get; set; }
    public string LatestResponseId { get { lock (_gate) { return _responses.Keys.Last(); } } }

    public Task<IReadOnlyList<UhpHarness>> ListHarnessesAsync(UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var result = new List<UhpHarness> { Harness };

            if (ClaudeHarness is { } additional)
            {
                result.Add(additional);
            }

            foreach (var entry in _harnesses.Values)
            {
                if (entry.Owner == Owner(context))
                {
                    result.Add(ToCore(entry.Harness));
                }
            }

            return Task.FromResult<IReadOnlyList<UhpHarness>>(result);
        }
    }

    public Task<UhpHarness> GetHarnessAsync(string harnessId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(ToCore(FindHarness(harnessId, Owner(context))));
        }
    }

    public async Task<UhpModelCatalog> GetModelsAsync(UhpRequestContext context, CancellationToken ct)
    {
        var primary = await _runtime
            .GetModelsAsync(ct)
            .ConfigureAwait(false);

        return claude is null ? primary : new(new Dictionary<string, UhpModelBackend>(primary.Backends) { [claude.Base] = claude.Models });
    }

    public Task<UhpHarnessModels> GetHarnessModelsAsync(string harnessId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var harness = FindHarness(harnessId, Owner(context));

            if (harnessId == ClaudeHarnessId && claude is { } additional)
            {
                return Task.FromResult(new UhpHarnessModels(additional.Models.Models) { HarnessId = harnessId, Backend = additional.Base, Default = additional.DefaultModel });
            }

            return Task.FromResult(new UhpHarnessModels([new UhpModel(harness.DefaultModel ?? _runtime.DefaultModel, true)]) { HarnessId = harnessId, Backend = _runtime.Base, Default = harness.DefaultModel ?? _runtime.DefaultModel });
        }
    }

    public void ConnectMemories(Memory.ReferenceMemoryBackend memories) => _memories = memories;

    public async Task<UhpExecution> SubmitAsync(UhpSubmission submission, UhpRequestContext context, CancellationToken serverStopping)
    {
        var request = submission.Request;
        var owner = Owner(context);

        if (request.TimeoutSeconds > ReferenceExecution.MaximumTimeoutSeconds)
        {
            throw Failure(400, "invalid_input", "The timeout exceeds the supported timer range.");
        }
        string? hash = null;

        if (context.IdempotencyKey is not null)
        {
            var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(UhpJson.SerializeRequest(request, false, submission.Background), UhpJsonContext.Default.JsonElement);

            try
            {
                hash = Convert.ToHexStringLower(SHA256.HashData(payload));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }

        Memory.ReferenceAgentMemory? memory = null;
        System.Text.Json.JsonElement? requestedMemory = request.Metadata?.TryGetValue("memory", out var suppliedMemory) == true ? suppliedMemory : null;

        if (requestedMemory is { } selection && selection.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw Failure(400, "invalid_input", "metadata.memory must name a memory.");
        }

        if (_memories is { } memories)
        {
            string selectedHarness;
            string? selectedMemory;

            lock (_gate)
            {
                if (Replay(context, owner, hash) is { } replay)
                {
                    return replay;
                }

                var prior = request.PreviousResponseId is { } previous ? Find(previous, owner) : null;
                selectedHarness = SelectHarness(request, prior);
                FindHarness(selectedHarness, owner);
                selectedMemory = requestedMemory?.GetString() ?? prior?.MemoryId;

                if (selectedHarness != ClaudeHarnessId && prior is not null && requestedMemory is not null && selectedMemory != prior.MemoryId)
                {
                    throw Failure(409, "memory_mismatch", "A continuation keeps the session's default memory.");
                }
            }

            if (selectedHarness != ClaudeHarnessId)
            {
                memory = await memories
                    .SelectForSessionAsync(selectedHarness, owner, selectedMemory, request.PreviousResponseId is not null,
                        context.ProtocolVersion, serverStopping)
                    .ConfigureAwait(false);
            }
        }

        lock (_gate)
        {
            if (Replay(context, owner, hash) is { } replay)
            {
                return replay;
            }

            var session = request.PreviousResponseId is { } previous ? Find(previous, owner).SessionId : "hsess_" + Guid
                .NewGuid()
                .ToString("N");

            if (request.PreviousResponseId is { } previousId && _deletedResponses.Contains(previousId))
            {
                throw UhpServerException.NotFound("response_not_found");
            }

            if (_deletingSessions.Contains(session))
            {
                throw UhpServerException.NotFound("session_not_found");
            }
            var previousState = request.PreviousResponseId is { } priorId ? Find(priorId, owner) : null;
            var harness = SelectHarness(request, previousState);

            var configuration = FindHarness(harness, owner);
            var selectedRuntime = _runtime;

            if (harness == ClaudeHarnessId && claude is { } additional)
            {
                additional.ValidateTask(request);
                selectedRuntime = additional;
            }
            else if (_memories is null && requestedMemory is not null)
            {
                throw UhpServerException.NotFound("memory_not_found");
            }

            var answer = ValidateAnswer(request, previousState);
            var environment = SelectEnvironment(request, configuration, previousState, owner);

            if (_active.ContainsKey(session))
            {
                throw new UhpServerException(409, new("invalid_request_error", "session_busy", "The session already has an active turn."));
            }

            selectedRuntime.ValidateSession(session);
            var attachments = Attachments(request, owner);
            var voided = answer is null && _pendingInputs.TryGetValue(session, out var pending) ? pending.Question.Id : null;

            var retained = RetainedRequest(request, answer);
            var workspace = Path.Combine(selectedRuntime.WorkspaceRoot, session);
            var runtimeRequest = new ReferenceRuntimeRequest(session, workspace, retained,
                configuration)
            {
                Attachments = attachments,
                Answer = answer,
                Environment = environment,
                VoidedInputRequestId = voided,
                Memory = memory,
                CaptureArtifactsAsync = selectedRuntime.ProducesArtifacts ? ct => CaptureArtifactsAsync(workspace, session, owner, ct) : null,
                ArtifactBaseUrl = ArtifactOrigin is { } origin ? origin.GetLeftPart(UriPartial.Authority) + context.BasePath : null
            };
            var ignored = submission.IgnoredFields;

            if (request.MaxStep is not null || request.MaxOutputTokens is not null)
            {
                var unsupported = new List<string>(ignored);

                if (request.MaxStep is not null)
                {
                    unsupported.Add("max_step");
                }

                if (request.MaxOutputTokens is not null)
                {
                    unsupported.Add("max_output_tokens");
                }

                ignored = unsupported;
            }

            var state = new ReferenceExecution(owner, session, retained, ignored, serverStopping, selectedRuntime, runtimeRequest);
            _responses.Add(state.Response.Id, state);
            _active.Add(session, state);
            _pendingInputs.Remove(session);
            Interlocked.Increment(ref _submissions);
            state.Start(() =>
            {
                lock (_gate)
                {
                    _active.Remove(session);
                    var completed = state.Response;

                    if (completed.Status == UhpResponseStatus.Incomplete && completed.Output.LastOrDefault() is { Type: "input_request" } question)
                    {
                        _pendingInputs[session] = (completed.Id, question);
                    }
                }
            });

            if (context.IdempotencyKey is { } acceptedKey)
            {
                _keys.Add((owner, acceptedKey), (hash!, state));
            }

            return state.Execution;
        }
    }

    private static string SelectHarness(UhpTaskRequest request, ReferenceExecution? previous)
    {
        var harness = request.HarnessId
            ?? (request.Metadata?.GetValueOrDefault("harness_id") is { ValueKind: System.Text.Json.JsonValueKind.String } selected ? selected.GetString() : null)
            ?? previous?.HarnessId ?? DefaultHarnessId;

        if (previous is not null && harness != previous.HarnessId)
        {
            throw Failure(409, "harness_mismatch", "A continuation must keep its harness.");
        }

        return harness;
    }

    private UhpExecution? Replay(UhpRequestContext context, string owner, string? hash)
    {
        if (context.IdempotencyKey is not { } key || !_keys.TryGetValue((owner, key), out var replay))
        {
            return null;
        }

        if (hash != replay.Hash)
        {
            throw Failure(409, "idempotency_key_reused", "The key was already used for another request.");
        }

        return replay.State.Execution with { IsReplay = true };
    }

    public Task<UhpResponse> GetResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            var response = Find(responseId, Owner(context)).Response;
            return Task.FromResult(response.Store == false || _deletedResponses.Contains(responseId) ? throw UhpServerException.NotFound("response_not_found") : response);
        }
    }

    public Task<UhpResponse> CancelResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct)
    {
        ReferenceExecution state;

        lock (_gate)
        {
            state = Find(responseId, Owner(context));
        }

        state.Cancellation.Cancel();
        Interlocked.Increment(ref _cancellations);
        return Task.FromResult(state.Response);
    }

    public Task<UhpSessionCancelResult> CancelSessionAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        ReferenceExecution state;
        ReferenceExecution? active;

        lock (_gate)
        {
            state = _responses.Values.FirstOrDefault(state => state.SessionId == sessionId && state.Owner == Owner(context)) ?? throw UhpServerException.NotFound("session_not_found");
            active = _active.GetValueOrDefault(sessionId);
        }

        active?.Cancellation.Cancel();
        Interlocked.Increment(ref _cancellations);
        return Task.FromResult(new UhpSessionCancelResult(state.SessionId, "cancelled"));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReferenceExecution[] executions;

        lock (_gate)
        {
            executions = _responses.Values.ToArray();
        }

        foreach (var state in executions)
        {
            state.Cancellation.Cancel();
            state.Dispose();
        }
    }

    private ReferenceExecution Find(string id, string owner) => _responses.TryGetValue(id, out var state) && state.Owner == owner ? state : throw UhpServerException.NotFound("response_not_found");

    private static string Owner(UhpRequestContext context) => context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The fixture requires a stable principal ID.");

    private UhpHarness Harness => new(DefaultHarnessId, "Reference harness", _runtime.Base) { DefaultModel = _runtime.DefaultModel, CreatedAt = _createdAt };

    private UhpHarness? ClaudeHarness => claude is null ? null : new(ClaudeHarnessId, "Claude Code", claude.Base) { DefaultModel = claude.DefaultModel, CreatedAt = _createdAt };
}
