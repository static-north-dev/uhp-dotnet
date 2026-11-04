using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpAnnotationAdded(long Sequence, JsonElement Annotation, string ItemId, long OutputIndex, long ContentIndex, long AnnotationIndex) : UhpEvent("response.output_text.annotation.added", Sequence);
