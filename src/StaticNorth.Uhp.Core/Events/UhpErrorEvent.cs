namespace StaticNorth.Uhp.Core;

public sealed record UhpErrorEvent(long Sequence, string Code, string Message, string? Param = null) : UhpEvent(UhpEventTypes.ErrorEvent, Sequence);
