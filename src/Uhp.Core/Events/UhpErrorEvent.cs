namespace Uhp.Core;

public sealed record UhpErrorEvent(long Sequence, string Code, string Message, string? Param = null) : UhpEvent("error", Sequence);
