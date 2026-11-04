namespace StaticNorth.Uhp.Core;

public static class UhpProtocolVersions
{
    public const string Current = "2026-10-19";

    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly([Current, "2026-09-28", "2026-09-12"]);
}
