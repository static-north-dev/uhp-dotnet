using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public sealed record ReferenceRuntimeRequest(string SessionId, string Workspace, UhpTaskRequest Task, Wire.Harness Harness)
    : UhpHarnessRunRequest(SessionId, Workspace, Task, Harness)
{
    public string? VoidedInputRequestId { get; init; }
    internal string? MemoryId { get; init; }
    internal Func<CancellationToken, Task<IReadOnlyList<Wire.File>>>? CaptureArtifactsAsync { get; init; }
    internal string? ArtifactBaseUrl { get; init; }
}
