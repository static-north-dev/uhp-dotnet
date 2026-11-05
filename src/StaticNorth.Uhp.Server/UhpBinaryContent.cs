namespace StaticNorth.Uhp.Server;

/// <summary>The response owns and asynchronously disposes Content after sending it.</summary>
public sealed record UhpBinaryContent(Stream Content, string ContentType, string? FileName = null);
