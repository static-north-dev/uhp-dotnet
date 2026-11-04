namespace Uhp.Core;

public sealed record UhpTextDone(long Sequence, string Text, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent("response.output_text.done", Sequence);
