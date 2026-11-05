namespace StaticNorth.Uhp.Reference;

internal sealed record CodexMcpBinding(string Name, string? Url, string? Command, string[]? Args, Dictionary<string, string>? Headers, Dictionary<string, string>? Env, string? Cwd);
