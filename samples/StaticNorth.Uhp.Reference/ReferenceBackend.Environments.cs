using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Reference.Environments;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public partial class ReferenceBackend
{
    private ReferenceEnvironmentSelection? SelectEnvironment(UhpTaskRequest request, Wire.Harness harness, ReferenceExecution? previous, string owner)
    {
        var id = harness.Environment;

        if (request.Metadata?.TryGetValue("environment", out var requested) == true)
        {
            if (requested.ValueKind != JsonValueKind.String)
            {
                throw Failure(400, "invalid_input", "metadata.environment must name an environment.");
            }

            id = requested.GetString();
        }

        if (id is null)
        {
            return null;
        }

        var catalog = environments ?? throw Server.UhpServerException.NotFound("environment_not_found");
        catalog.ValidateReference(id, owner);
        return previous?.Environment is { } pinned && pinned.Id == id ? pinned : catalog.Select(id, owner);
    }
}
