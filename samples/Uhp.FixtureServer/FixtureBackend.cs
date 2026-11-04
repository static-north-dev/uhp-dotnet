using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Uhp.Core;
using Uhp.Server;

namespace Uhp.FixtureServer;

// Deterministic, in-memory test infrastructure; this does not execute an agent or provide production persistence.
public sealed class FixtureBackend : IUhpServerBackend, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FixtureExecution> _responses = [];
    private readonly Dictionary<string, FixtureExecution> _active = [];
    private readonly Dictionary<(string Owner, string Key), (string Hash, FixtureExecution State)> _keys = [];
    private int _submissions;
    private int _cancellations;

    public int SubmissionCount => Volatile.Read(ref _submissions);
    public int CancellationCount => Volatile.Read(ref _cancellations);
    public string LatestResponseId { get { lock (_gate) { return _responses.Keys.Last(); } } }

    public Task<IReadOnlyList<UhpHarness>> ListHarnessesAsync(UhpRequestContext context, CancellationToken ct) => Task.FromResult<IReadOnlyList<UhpHarness>>([Harness]);

    public Task<UhpHarness> GetHarnessAsync(string harnessId, UhpRequestContext context, CancellationToken ct) => Task.FromResult(harnessId == Harness.Id ? Harness : throw UhpServerException.NotFound("harness_not_found"));

    public Task<UhpModelCatalog> GetModelsAsync(UhpRequestContext context, CancellationToken ct) => Task.FromResult(new UhpModelCatalog(new Dictionary<string, UhpModelBackend>
    {
        ["fixture"] = new("fixture-model", [new UhpModel("fixture-model", true) { IsDefault = true, Backend = "fixture" }])
    }));

    public Task<UhpHarnessModels> GetHarnessModelsAsync(string harnessId, UhpRequestContext context, CancellationToken ct)
    {
        if (harnessId != Harness.Id)
        {
            throw UhpServerException.NotFound("harness_not_found");
        }

        return Task.FromResult(new UhpHarnessModels([new UhpModel("fixture-model", true)]) { HarnessId = Harness.Id, Backend = "fixture", Default = "fixture-model" });
    }

    public Task<UhpExecution> SubmitAsync(UhpSubmission submission, UhpRequestContext context, CancellationToken serverStopping)
    {
        var request = submission.Request;
        var owner = Owner(context);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(UhpJson.SerializeRequest(request, false).GetRawText())));

        lock (_gate)
        {
            if (context.IdempotencyKey is { } key && _keys.TryGetValue((owner, key), out var replay))
            {
                if (hash != replay.Hash)
                {
                    throw new UhpServerException(409, new("invalid_request_error", "idempotency_key_reused", "The key was already used for another request."));
                }

                return Task.FromResult(replay.State.Execution);
            }

            var session = request.PreviousResponseId is { } previous ? Find(previous, owner).SessionId : "hsess_" + Guid.NewGuid().ToString("N");
            var harness = request.HarnessId ?? (request.Metadata?.GetValueOrDefault("harness_id") is { ValueKind: System.Text.Json.JsonValueKind.String } selected ? selected.GetString() : null);

            if (harness is not null && harness != Harness.Id)
            {
                throw new UhpServerException(request.PreviousResponseId is null ? 404 : 409, new("invalid_request_error", request.PreviousResponseId is null ? "harness_not_found" : "harness_mismatch", "The selected harness cannot serve this task."));
            }

            if (_active.ContainsKey(session))
            {
                throw new UhpServerException(409, new("invalid_request_error", "session_busy", "The session already has an active turn."));
            }

            var state = new FixtureExecution(owner, session, request, submission.IgnoredFields, serverStopping);
            _responses.Add(state.Response.Id, state);
            _active.Add(session, state);
            Interlocked.Increment(ref _submissions);
            state.Start(() =>
            {
                lock (_gate)
                {
                    _active.Remove(session);
                }
            });

            if (context.IdempotencyKey is { } acceptedKey)
            {
                _keys.Add((owner, acceptedKey), (hash, state));
            }

            return Task.FromResult(state.Execution);
        }
    }

    public Task<UhpResponse> GetResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            var response = Find(responseId, Owner(context)).Response;
            return Task.FromResult(response.Store == false ? throw UhpServerException.NotFound("response_not_found") : response);
        }
    }

    public Task<UhpResponse> CancelResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            var state = Find(responseId, Owner(context));
            state.Cancellation.Cancel();
            Interlocked.Increment(ref _cancellations);
            return Task.FromResult(state.Response);
        }
    }

    public Task<UhpSessionCancelResult> CancelSessionAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            var state = _responses.Values.FirstOrDefault(state => state.SessionId == sessionId && state.Owner == Owner(context)) ?? throw UhpServerException.NotFound("session_not_found");
            _active.GetValueOrDefault(sessionId)?.Cancellation.Cancel();
            Interlocked.Increment(ref _cancellations);
            return Task.FromResult(new UhpSessionCancelResult(state.SessionId, "cancelled"));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var state in _responses.Values)
            {
                state.Cancellation.Cancel();
                state.Dispose();
            }
        }
    }

    private FixtureExecution Find(string id, string owner) => _responses.TryGetValue(id, out var state) && state.Owner == owner ? state : throw UhpServerException.NotFound("response_not_found");

    private static string Owner(UhpRequestContext context) => context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The fixture requires a stable principal ID.");

    private static UhpHarness Harness => new("chrn_fixture", "Deterministic fixture", "fixture") { DefaultModel = "fixture-model", CreatedAt = 1_795_000_000_123 };
}
