namespace Uhp.Core;

public sealed record UhpResponseFailed(long Sequence, UhpResponse Response) : UhpResponseEvent("response.failed", Sequence, Response);
