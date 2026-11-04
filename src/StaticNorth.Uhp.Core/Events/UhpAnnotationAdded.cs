using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpAnnotationAdded(long Sequence, JsonElement Annotation, string ItemId, long OutputIndex, long ContentIndex, long AnnotationIndex) : UhpEvent(UhpEventTypes.AnnotationAdded, Sequence);
