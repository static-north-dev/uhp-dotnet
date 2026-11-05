using System.Text.Json.Nodes;

namespace StaticNorth.Uhp.SchemaGen;

internal sealed record ApiParameter(string Name, string Identifier, string Location, string Type, JsonObject Schema);
