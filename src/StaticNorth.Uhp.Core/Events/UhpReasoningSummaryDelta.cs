namespace StaticNorth.Uhp.Core;

public sealed record UhpReasoningSummaryDelta(long Sequence, string Text, string ItemId, long OutputIndex, long SummaryIndex) : UhpEvent(UhpEventTypes.ReasoningSummaryDelta, Sequence);
