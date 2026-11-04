namespace Uhp.Core;

public sealed record UhpToolArgumentsDelta(long Sequence, string Delta, string ItemId, long OutputIndex) : UhpEvent("response.function_call_arguments.delta", Sequence);
