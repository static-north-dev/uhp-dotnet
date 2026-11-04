using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpUnknownEvent(string EventType, long Sequence, JsonElement Data) : UhpEvent(EventType, Sequence);
