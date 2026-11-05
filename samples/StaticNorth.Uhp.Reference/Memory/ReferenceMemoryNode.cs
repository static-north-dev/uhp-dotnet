using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

internal sealed class ReferenceMemoryNode(string id, string owner, string name, string? parentId, string? description, bool restricted)
{
    public string Id { get; } = id;
    public string Owner { get; } = owner;
    public string Name { get; set; } = name;
    public string? ParentId { get; set; } = parentId;
    public string? Description { get; set; } = description;
    public bool Restricted { get; set; } = restricted;
    public long CreatedAt { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public Dictionary<string, Wire.MemoryGrant> Grants { get; } = new(StringComparer.Ordinal);
}
