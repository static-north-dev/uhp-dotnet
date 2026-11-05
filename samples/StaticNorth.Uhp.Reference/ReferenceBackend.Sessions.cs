using System.Globalization;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public partial class ReferenceBackend
{
    public Task<Wire.SessionList> ListSessionsAsync(Wire.ListSessionsQuery query, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var limit = query.Limit ?? 20;

        if (limit is < 1 or > 100 || query.Cursor is { } cursor && (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0))
        {
            throw new UhpServerException(400, new(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "Invalid session page."));
        }

        var offset = query.Cursor is null ? 0 : int.Parse(query.Cursor, CultureInfo.InvariantCulture);

        lock (_gate)
        {
            var owner = Owner(context);
            var sessions = new Dictionary<string, Wire.Session>(StringComparer.Ordinal);

            foreach (var entry in _responses)
            {
                var state = entry.Value;

                if (state.Owner != owner || query.Harness is { } harness && harness != state.HarnessId)
                {
                    continue;
                }

                var session = Session(state);

                if (sessions.TryGetValue(state.SessionId, out var existing))
                {
                    session = session with { CreatedAt = existing.CreatedAt, Title = existing.Title };
                }

                sessions[state.SessionId] = session;
            }

            var page = sessions.Values
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .Skip(offset)
                .Take((int)limit)
                .ToArray();
            return Task.FromResult(new Wire.SessionList { Sessions = page, NextCursor = offset + page.Length < sessions.Count ? (offset + page.Length).ToString(CultureInfo.InvariantCulture) : null });
        }
    }

    public Task<Wire.Session> GetSessionAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var states = SessionStates(sessionId, Owner(context));
            return Task.FromResult(Session(states[^1]) with { CreatedAt = states[0].Response.CreatedAt, Title = states[0].Request.Input.ToString() });
        }
    }

    public Task<Wire.GetSessionTurnsResponse> GetSessionTurnsAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var states = SessionStates(sessionId, Owner(context));
            var turns = new Wire.TurnItem[states.Count];

            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];
                var response = state.Response;
                turns[index] = new Wire.TurnItem
                {
                    Id = response.Id,
                    Status = response.Status.Value,
                    User = state.Request.Input.ToString(),
                    Assistant = response.Output.Count > 0 ? response.Output[^1].Text : null,
                    CreatedAt = response.CreatedAt,
                    CompletedAt = state.CompletedAt,
                    Imported = false
                    ,
                    AdditionalData = state.Environment is null ? null : new Dictionary<string, JsonElement>
                    {
                        ["environment"] = response.Metadata!["environment"],
                        ["environment_version"] = response.Metadata["environment_version"]
                    }
                };
            }

            return Task.FromResult(new Wire.GetSessionTurnsResponse { Turns = turns });
        }
    }

    public async Task<Wire.Deleted> DeleteSessionAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        List<ReferenceExecution> states;

        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            states = SessionStates(sessionId, Owner(context));

            if (!_deletingSessions.Add(sessionId))
            {
                throw UhpServerException.NotFound("session_not_found");
            }

        }

        foreach (var state in states)
        {
            state.Cancellation.Cancel();
        }

        foreach (var state in states)
        {
            await state.Execution.Completion.ConfigureAwait(false);
        }

        try
        {
            await states[0].Runtime
                .DeleteSessionAsync(sessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                _deletingSessions.Remove(sessionId);
            }

            throw;
        }

        lock (_gate)
        {
            foreach (var state in states)
            {
                _responses.Remove(state.Response.Id);
                _deletedResponses.Remove(state.Response.Id);
                state.Dispose();
            }

            _active.Remove(sessionId);
            _shares.Remove(sessionId);
            _pendingInputs.Remove(sessionId);
            DeleteSessionFiles(sessionId);
            _deletingSessions.Remove(sessionId);
            var keys = _keys
                .Where(pair => pair.Value.State.SessionId == sessionId)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in keys)
            {
                _keys.Remove(key);
            }
        }

        return new Wire.Deleted { Id = sessionId, IsDeleted = true };
    }

    public Task<Wire.DeleteResponseResponse> DeleteResponseAsync(string responseId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Find(responseId, Owner(context));
            _deletedResponses.Add(responseId);
            return Task.FromResult(new Wire.DeleteResponseResponse { Id = responseId, Deleted = true });
        }
    }

    public Task<Wire.GetResponseInputItemsResponse> GetResponseInputItemsAsync(string responseId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var state = Find(responseId, Owner(context));

            if (_deletedResponses.Contains(responseId) || state.Request.Store == false)
            {
                throw UhpServerException.NotFound("response_not_found");
            }

            var input = state.Request.Input;
            var items = new List<Dictionary<string, JsonElement>>();

            if (input.ValueKind == JsonValueKind.String)
            {
                var item = UhpJson.CreateElement(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "user");
                    writer.WriteString("content", input.GetString());
                    writer.WriteEndObject();
                });
                items.Add(item.Deserialize(Wire.UhpContractJsonContext.Default.DictionaryStringJsonElement)!);
            }
            else
            {
                foreach (var item in input.EnumerateArray())
                {
                    items.Add(item.Deserialize(Wire.UhpContractJsonContext.Default.DictionaryStringJsonElement)!);
                }
            }

            return Task.FromResult(new Wire.GetResponseInputItemsResponse { Data = items.ToArray() });
        }
    }

    private List<ReferenceExecution> SessionStates(string id, string owner)
    {
        var states = new List<ReferenceExecution>();

        foreach (var entry in _responses)
        {
            if (entry.Value.SessionId == id && entry.Value.Owner == owner)
            {
                states.Add(entry.Value);
            }
        }

        return states.Count > 0 ? states : throw UhpServerException.NotFound("session_not_found");
    }

    private Wire.Session Session(ReferenceExecution state) => new()
    {
        Id = state.SessionId,
        HarnessId = state.HarnessId,
        Title = state.Request.Input.ToString(),
        Environment = state.Environment?.Id,
        Status = state.Response.Status.Value,
        CreatedAt = state.Response.CreatedAt,
        UpdatedAt = state.CompletedAt ?? state.Response.CreatedAt
    };
}
