using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpReasoningSummaryPartAdded(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long SummaryIndex) : UhpEvent(UhpEventTypes.ReasoningSummaryPartAdded, Sequence);
