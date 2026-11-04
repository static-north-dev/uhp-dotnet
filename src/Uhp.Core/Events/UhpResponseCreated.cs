namespace Uhp.Core;

public sealed record UhpResponseCreated(long Sequence, UhpResponse Response) : UhpResponseEvent("response.created", Sequence, Response);
