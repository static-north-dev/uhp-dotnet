namespace StaticNorth.Uhp.TestSupport.Memory;

internal sealed class InMemoryMemoryState(string owner)
{
    public string Owner { get; } = owner;
    public Dictionary<string, List<ReferenceMemoryRecord>> Records { get; } = new(StringComparer.Ordinal);
}
