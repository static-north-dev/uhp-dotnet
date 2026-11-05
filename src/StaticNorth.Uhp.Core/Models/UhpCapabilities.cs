using System.Collections.Frozen;

namespace StaticNorth.Uhp.Core;

public sealed class UhpCapabilities(IEnumerable<KeyValuePair<string, bool>> values)
{
    private readonly FrozenDictionary<string, bool> _values = values.ToFrozenDictionary(StringComparer.Ordinal);

    public bool this[string name] => _values.GetValueOrDefault(name);
    public bool Streaming => this[UhpProtocol.StreamingCapability];
    public bool Sessions => this[UhpProtocol.SessionsCapability];
    public bool Cancellation => this[UhpProtocol.CancellationCapability];
    public bool Idempotency => this[UhpProtocol.IdempotencyCapability];
    public IReadOnlyDictionary<string, bool> Values => _values;
}
