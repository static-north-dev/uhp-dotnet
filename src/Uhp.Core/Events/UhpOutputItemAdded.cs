namespace Uhp.Core;

public sealed record UhpOutputItemAdded(long Sequence, UhpOutputItem Item, long OutputIndex) : UhpEvent("response.output_item.added", Sequence);
