namespace Uhp.Core;

public sealed record UhpResponseCompleted(long Sequence, UhpResponse Response) : UhpResponseEvent("response.completed", Sequence, Response);
