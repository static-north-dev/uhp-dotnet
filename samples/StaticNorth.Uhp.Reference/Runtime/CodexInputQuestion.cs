using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

internal sealed record CodexInputQuestion(string Prompt, string Kind, UhpOutputItem Call, UhpOutputItem Request);
