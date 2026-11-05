using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpReasoningSummaryPartDone(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long SummaryIndex) : UhpEvent(UhpEventTypes.ReasoningSummaryPartDone, Sequence);
