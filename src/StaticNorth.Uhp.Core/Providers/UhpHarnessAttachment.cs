namespace StaticNorth.Uhp.Core.Providers;

public sealed record UhpHarnessAttachment(string FileName, ReadOnlyMemory<byte> Content, string ContentType);
