namespace StaticNorth.Uhp.Core;

public sealed record UhpResponseFailed(long Sequence, UhpResponse Response) : UhpResponseEvent(UhpEventTypes.ResponseFailed, Sequence, Response);
