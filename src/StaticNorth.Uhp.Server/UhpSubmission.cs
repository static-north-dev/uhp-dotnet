using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server;

public sealed record UhpSubmission(UhpTaskRequest Request, IReadOnlyList<string> IgnoredFields);
