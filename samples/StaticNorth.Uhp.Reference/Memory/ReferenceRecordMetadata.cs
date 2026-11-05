using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Memory;

internal sealed record ReferenceRecordMetadata(Wire.MemoryRecordTime Time, Wire.MemoryWriter Writer);
