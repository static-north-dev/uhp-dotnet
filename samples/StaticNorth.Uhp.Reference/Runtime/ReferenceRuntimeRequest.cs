using StaticNorth.Uhp.Core;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public sealed record ReferenceRuntimeRequest(string SessionId, string Workspace, UhpTaskRequest Task, Wire.Harness Harness)
{
    public IReadOnlyDictionary<string, ReferenceAttachment> Attachments { get; init; } = new Dictionary<string, ReferenceAttachment>();
    public ReferenceInputAnswer? Answer { get; init; }
    public Environments.ReferenceEnvironmentSelection? Environment { get; init; }
    public string? VoidedInputRequestId { get; init; }
    internal Memory.ReferenceAgentMemory? Memory { get; init; }
    internal Func<CancellationToken, Task<IReadOnlyList<Wire.File>>>? CaptureArtifactsAsync { get; init; }
    internal string? ArtifactBaseUrl { get; init; }
}
