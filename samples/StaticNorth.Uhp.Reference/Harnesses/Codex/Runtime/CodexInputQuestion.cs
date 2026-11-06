using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Harnesses.Codex;

internal sealed record CodexInputQuestion(string Prompt, string Kind, UhpOutputItem Call, UhpOutputItem Request);
