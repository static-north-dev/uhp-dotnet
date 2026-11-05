using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

internal sealed record ReferenceHarnessEntry(string Owner, Wire.Harness Harness);
