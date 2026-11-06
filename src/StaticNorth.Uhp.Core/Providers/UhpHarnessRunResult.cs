namespace StaticNorth.Uhp.Core.Providers;

public sealed record UhpHarnessRunResult(string Model, UhpResponseStatus Status)
{
    public UhpError? Error { get; init; }
    public UhpUsage? Usage { get; init; }
    public System.Text.Json.JsonElement? IncompleteDetails { get; init; }
    public IReadOnlyList<UhpOutputItem> Output { get; init; } = [];
}
