using System.Collections.Frozen;
using StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Core.Providers;

public record UhpHarnessRunRequest(string SessionId, string Workspace, UhpTaskRequest Task, Harness Harness)
{
    public IReadOnlyDictionary<string, UhpHarnessAttachment> Attachments { get; init; } = FrozenDictionary<string, UhpHarnessAttachment>.Empty;
    public UhpHarnessInputAnswer? Answer { get; init; }
    public IUhpHarnessEnvironment? Environment { get; init; }
    public IUhpHarnessMemory? Memory { get; init; }
}
