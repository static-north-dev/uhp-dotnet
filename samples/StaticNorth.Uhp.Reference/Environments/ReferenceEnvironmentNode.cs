using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

internal sealed class ReferenceEnvironmentNode(string owner, Wire.Environment value, Wire.EnvironmentCreate configuration)
{
    public string Owner { get; } = owner;
    public Wire.Environment Value { get; set; } = value;
    public Wire.EnvironmentCreate Configuration { get; set; } = configuration;
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<long, ReferenceEnvironmentVersion> Versions { get; } = [];
    public SemaphoreSlim ExecutionGate { get; } = new(1, 1);
    public bool Building { get; set; }
    public bool Deleting { get; set; }
    public long? MountedVersion { get; set; }
}
