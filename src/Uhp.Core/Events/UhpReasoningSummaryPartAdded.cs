using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpReasoningSummaryPartAdded(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long SummaryIndex) : UhpEvent("response.reasoning_summary_part.added", Sequence);
