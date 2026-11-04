using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpContentPartDone(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent(UhpEventTypes.ContentPartDone, Sequence);
