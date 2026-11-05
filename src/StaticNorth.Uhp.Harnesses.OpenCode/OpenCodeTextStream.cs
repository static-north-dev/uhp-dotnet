using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Harnesses.OpenCode;

internal sealed class OpenCodeTextStream(string sessionId, Func<string, ValueTask> textDelta)
{
    private readonly List<string> _textParts = [];
    private readonly StringBuilder _emitted = new();
    private string? _user;
    private string? _assistant;

    public async Task ReadAsync(IAsyncEnumerator<SseItem<JsonElement>> events)
    {
        while (await events.MoveNextAsync().ConfigureAwait(false))
        {
            var update = events.Current.Data;

            if (!update.TryGetProperty("type"u8, out var kind) || !update.TryGetProperty("properties"u8, out var properties))
            {
                continue;
            }

            if (kind.ValueEquals("message.updated"u8) && properties.TryGetProperty("info"u8, out var message) && MatchesSession(message))
            {
                ObserveMessage(message);
            }
            else if (kind.ValueEquals("message.part.updated"u8) && properties.TryGetProperty("part"u8, out var part) &&
                MatchesSession(part) && MatchesAssistant(part) && part.GetProperty("type"u8).ValueEquals("text"u8))
            {
                var id = part.GetProperty("id"u8);

                if (!IsTextPart(id))
                {
                    _textParts.Add(id.GetString()!);
                }
            }
            else if (kind.ValueEquals("message.part.delta"u8) && MatchesSession(properties) && MatchesAssistant(properties) &&
                properties.GetProperty("field"u8).ValueEquals("text"u8) && IsTextPart(properties.GetProperty("partID"u8)) &&
                properties.GetProperty("delta"u8).GetString() is { Length: > 0 } delta)
            {
                _emitted.Append(delta);
                await textDelta(delta).ConfigureAwait(false);
            }
        }
    }

    public ValueTask CompleteAsync(JsonElement reply, string provider, string model)
    {
        if (!IsAssistantReply(reply, sessionId))
        {
            throw new IOException("OpenCode returned a reply for a different session.");
        }

        var info = reply.GetProperty("info"u8);

        if ((info.TryGetProperty("error"u8, out var error) && error.ValueKind != JsonValueKind.Null) ||
            !info.GetProperty("providerID"u8).ValueEquals(provider) || !info.GetProperty("modelID"u8).ValueEquals(model) ||
            (_user is not null && !info.GetProperty("parentID"u8).ValueEquals(_user)) ||
            (_emitted.Length > 0 && !info.GetProperty("id"u8).ValueEquals(_assistant)))
        {
            throw new IOException("OpenCode did not return a successful reply for this turn and model.");
        }

        var final = new StringBuilder();

        foreach (var part in reply.GetProperty("parts"u8).EnumerateArray())
        {
            if (part.GetProperty("type"u8).ValueEquals("text"u8))
            {
                final.Append(part.GetProperty("text"u8).GetString());
            }
        }

        var text = final.ToString();
        var offset = 0;

        foreach (var chunk in _emitted.GetChunks())
        {
            if (!text.AsSpan(offset).StartsWith(chunk.Span, StringComparison.Ordinal))
            {
                throw new IOException("OpenCode's final reply did not match the streamed text.");
            }

            offset += chunk.Length;
        }

        return offset == text.Length ? ValueTask.CompletedTask : textDelta(text[offset..]);
    }

    internal static bool IsAssistantReply(JsonElement reply, string sessionId) =>
        reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("info"u8, out var info) && info.ValueKind == JsonValueKind.Object &&
        info.TryGetProperty("role"u8, out var role) && role.ValueEquals("assistant"u8) &&
        info.TryGetProperty("sessionID"u8, out var session) && session.ValueEquals(sessionId);

    private void ObserveMessage(JsonElement message)
    {
        var role = message.GetProperty("role"u8);

        if (role.ValueEquals("user"u8))
        {
            _user ??= message.GetProperty("id"u8).GetString();
        }
        else if (_user is not null && role.ValueEquals("assistant"u8) && message.GetProperty("parentID"u8).ValueEquals(_user))
        {
            _assistant ??= message.GetProperty("id"u8).GetString();
        }
    }

    private bool MatchesSession(JsonElement element) => element.TryGetProperty("sessionID"u8, out var session) && session.ValueEquals(sessionId);

    private bool MatchesAssistant(JsonElement element) => _assistant is not null &&
        element.TryGetProperty("messageID"u8, out var message) && message.ValueEquals(_assistant);

    private bool IsTextPart(JsonElement id)
    {
        for (var index = 0; index < _textParts.Count; index++)
        {
            if (id.ValueEquals(_textParts[index]))
            {
                return true;
            }
        }

        return false;
    }
}
