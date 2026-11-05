namespace StaticNorth.Uhp.Core;

public sealed record UhpToolArgumentsDelta(long Sequence, string Delta, string ItemId, long OutputIndex) : UhpEvent(UhpEventTypes.ToolArgumentsDelta, Sequence);
