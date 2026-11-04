namespace Uhp.Core;

public sealed record UhpResponseInProgress(long Sequence, UhpResponse Response) : UhpResponseEvent("response.in_progress", Sequence, Response);
