using System.Collections.Frozen;

namespace Uhp.Core;

public sealed class UhpCapabilities
{
    private readonly FrozenDictionary<string, bool> _values;

    public UhpCapabilities(IEnumerable<KeyValuePair<string, bool>> values) => _values = values.ToFrozenDictionary(StringComparer.Ordinal);

    public bool this[string name] => _values.GetValueOrDefault(name);
    public bool Streaming => this["streaming"];
    public bool Sessions => this["sessions"];
    public bool Cancellation => this["cancellation"];
    public bool Idempotency => this["idempotency"];
    public IReadOnlyDictionary<string, bool> Values => _values;
}
