namespace Uhp.Core;

public sealed record UhpTextDelta(long Sequence, string Text, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent("response.output_text.delta", Sequence);
