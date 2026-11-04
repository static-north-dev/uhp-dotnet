using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpContentPartDone(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent("response.content_part.done", Sequence);
