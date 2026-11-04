namespace Uhp.Core;

public sealed record UhpOutputItemDone(long Sequence, UhpOutputItem Item, long OutputIndex) : UhpEvent("response.output_item.done", Sequence);
