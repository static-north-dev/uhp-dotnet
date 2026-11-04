namespace Uhp.Core;

public sealed record UhpResponseIncomplete(long Sequence, UhpResponse Response) : UhpResponseEvent("response.incomplete", Sequence, Response);
