namespace StaticNorth.Uhp.Core;

public sealed record UhpTextDelta(long Sequence, string Text, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent(UhpEventTypes.TextDelta, Sequence);
