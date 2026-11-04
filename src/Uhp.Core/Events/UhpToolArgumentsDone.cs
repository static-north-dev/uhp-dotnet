namespace Uhp.Core;

public sealed record UhpToolArgumentsDone(long Sequence, string Arguments, string ItemId, long OutputIndex) : UhpEvent("response.function_call_arguments.done", Sequence);
