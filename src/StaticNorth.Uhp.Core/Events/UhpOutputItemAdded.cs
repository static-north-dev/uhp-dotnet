namespace StaticNorth.Uhp.Core;

public sealed record UhpOutputItemAdded(long Sequence, UhpOutputItem Item, long OutputIndex) : UhpEvent(UhpEventTypes.OutputItemAdded, Sequence);
