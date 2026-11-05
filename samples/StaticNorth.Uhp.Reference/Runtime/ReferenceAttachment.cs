namespace StaticNorth.Uhp.Reference;

public sealed record ReferenceAttachment(string FileName, ReadOnlyMemory<byte> Content, string ContentType);
