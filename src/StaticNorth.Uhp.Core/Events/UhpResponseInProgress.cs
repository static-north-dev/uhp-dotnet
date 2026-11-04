namespace StaticNorth.Uhp.Core;

public sealed record UhpResponseInProgress(long Sequence, UhpResponse Response) : UhpResponseEvent(UhpEventTypes.ResponseInProgress, Sequence, Response);
