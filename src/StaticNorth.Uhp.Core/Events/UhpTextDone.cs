namespace StaticNorth.Uhp.Core;

public sealed record UhpTextDone(long Sequence, string Text, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent(UhpEventTypes.TextDone, Sequence);
