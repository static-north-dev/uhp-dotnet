namespace StaticNorth.Uhp.SchemaGen;

internal sealed record ApiOperation(string Name, string Method, string Path, string Family, string Capability,
    string? RequestType, bool OptionalBody, string? ResponseType, bool BinaryRequest, bool BinaryResponse,
    IReadOnlyList<ApiParameter> Parameters, string? QueryType);
