namespace StaticNorth.Uhp.Core;

public sealed record UhpResponseCompleted(long Sequence, UhpResponse Response) : UhpResponseEvent(UhpEventTypes.ResponseCompleted, Sequence, Response);
