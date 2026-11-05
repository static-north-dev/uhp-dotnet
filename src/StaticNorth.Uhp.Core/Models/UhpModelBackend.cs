namespace StaticNorth.Uhp.Core;

public sealed record UhpModelBackend(string Default, IReadOnlyList<UhpModel> Models);
