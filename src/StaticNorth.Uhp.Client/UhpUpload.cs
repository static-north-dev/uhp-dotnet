namespace StaticNorth.Uhp.Client;

/// <summary>A streamed request body. The caller retains ownership of Content.</summary>
public sealed record UhpUpload(Stream Content, string ContentType = "application/octet-stream", string? FileName = null);
