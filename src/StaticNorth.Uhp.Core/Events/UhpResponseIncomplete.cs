namespace StaticNorth.Uhp.Core;

public sealed record UhpResponseIncomplete(long Sequence, UhpResponse Response) : UhpResponseEvent(UhpEventTypes.ResponseIncomplete, Sequence, Response);
