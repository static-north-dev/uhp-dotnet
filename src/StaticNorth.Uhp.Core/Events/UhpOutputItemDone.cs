namespace StaticNorth.Uhp.Core;

public sealed record UhpOutputItemDone(long Sequence, UhpOutputItem Item, long OutputIndex) : UhpEvent(UhpEventTypes.OutputItemDone, Sequence);
