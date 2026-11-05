using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

internal sealed record ReferenceFileEntry(string Owner, string? SessionId, Wire.File File, byte[] Content, string ContentType);
