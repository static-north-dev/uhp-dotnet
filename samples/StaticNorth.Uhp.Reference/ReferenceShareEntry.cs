using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

internal sealed record ReferenceShareEntry(string Owner, string SessionId, Wire.SessionShare Share);
