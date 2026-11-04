using Uhp.Core;

namespace Uhp.Server;

public sealed record UhpSubmission(UhpTaskRequest Request, IReadOnlyList<string> IgnoredFields);
