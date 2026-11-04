namespace StaticNorth.Uhp.Core;

public sealed record UhpResponseCreated(long Sequence, UhpResponse Response) : UhpResponseEvent(UhpEventTypes.ResponseCreated, Sequence, Response);
