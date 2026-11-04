using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpReasoningSummaryPartDone(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long SummaryIndex) : UhpEvent("response.reasoning_summary_part.done", Sequence);
