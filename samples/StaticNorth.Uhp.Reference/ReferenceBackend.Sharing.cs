using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public partial class ReferenceBackend : IUhpSharingBackend, IUhpSharedViewBackend
{
    private readonly Dictionary<string, ReferenceShareEntry> _shares = new(StringComparer.Ordinal);

    public Task<Wire.SessionShare> GetSessionShareAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            SessionStates(sessionId, Owner(context));
            return Task.FromResult(_shares.TryGetValue(sessionId, out var entry) ? entry.Share : throw UhpServerException.NotFound("share_not_found"));
        }
    }

    public Task<Wire.SessionShare> ShareSessionAsync(string sessionId, Wire.ShareSessionRequest? request, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (request?.Enabled == false)
        {
            return RevokeSessionShareAsync(sessionId, context, ct);
        }

        lock (_gate)
        {
            var owner = Owner(context);
            SessionStates(sessionId, owner);

            if (_shares.TryGetValue(sessionId, out var existing))
            {
                return Task.FromResult(existing.Share);
            }

            var id = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var share = new Wire.SessionShare { Id = id, Url = context.BasePath + "/v1/shared/" + id, Enabled = true, Object = "session.share" };
            _shares.Add(sessionId, new(owner, sessionId, share));
            return Task.FromResult(share);
        }
    }

    public Task<Wire.SessionShare> RevokeSessionShareAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            SessionStates(sessionId, Owner(context));
            _shares.Remove(sessionId, out var entry);
            return Task.FromResult(entry is not null ? entry.Share with { Enabled = false } : new Wire.SessionShare { Id = "", Url = "", Enabled = false });
        }
    }

    public Task<UhpSharedSessionView> GetSharedViewAsync(string shareId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ReferenceShareEntry? share = null;

            foreach (var entry in _shares.Values)
            {
                if (entry.Share.Id == shareId)
                {
                    share = entry;
                    break;
                }
            }

            if (share is null || _deletingSessions.Contains(share.SessionId))
            {
                throw UhpServerException.NotFound("share_not_found");
            }

            var states = SessionStates(share.SessionId, share.Owner);
            var turns = new Wire.TurnItem[states.Count];

            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];
                turns[index] = new() { Id = state.Response.Id, Status = state.Response.Status.Value, Assistant = state.Response.Output.LastOrDefault()?.Text };
            }

            var session = Session(states[^1]) with { Title = null, AdditionalData = null };
            return Task.FromResult(new UhpSharedSessionView(session, turns));
        }
    }
}
