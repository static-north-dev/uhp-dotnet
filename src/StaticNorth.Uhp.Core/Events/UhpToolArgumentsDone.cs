namespace StaticNorth.Uhp.Core;

public sealed record UhpToolArgumentsDone(long Sequence, string Arguments, string ItemId, long OutputIndex) : UhpEvent(UhpEventTypes.ToolArgumentsDone, Sequence);
