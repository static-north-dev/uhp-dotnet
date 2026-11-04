using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpContentPartAdded(long Sequence, JsonElement Part, string ItemId, long OutputIndex, long ContentIndex) : UhpEvent(UhpEventTypes.ContentPartAdded, Sequence);
