using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

public sealed record ReferenceRuntimeResult(string Model, UhpResponseStatus Status)
{
    public UhpError? Error { get; init; }
    public UhpUsage? Usage { get; init; }
    public System.Text.Json.JsonElement? IncompleteDetails { get; init; }
    public IReadOnlyList<UhpOutputItem> Output { get; init; } = [];
}
