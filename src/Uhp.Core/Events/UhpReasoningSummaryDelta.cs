namespace Uhp.Core;

public sealed record UhpReasoningSummaryDelta(long Sequence, string Text, string ItemId, long OutputIndex, long SummaryIndex) : UhpEvent("response.reasoning_summary_text.delta", Sequence);
