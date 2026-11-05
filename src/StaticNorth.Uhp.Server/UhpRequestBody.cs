namespace StaticNorth.Uhp.Server;

/// <summary>A bounded request stream. Consume it before the handler returns; the HTTP request owns its lifetime.</summary>
public sealed record UhpRequestBody(Stream Content, string ContentType, string? FileName = null);
