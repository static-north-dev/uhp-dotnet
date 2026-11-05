using System.Text.Json;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class RequiredWireFieldsTests
{
    [Test]
    public async Task UnknownResponseFieldsAreExplicitlyNull()
    {
        var response = new UhpResponse("resp_test", UhpResponseStatus.Completed, "model");
        var json = UhpJson.SerializeResponse(response);

        foreach (var name in new[] { "usage", "error", "incomplete_details", "previous_response_id" })
        {
            await Assert.That(json.GetProperty(name).ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task ErrorFieldsAreExplicitlyNullWhenUnknown()
    {
        var error = new UhpError("invalid_request_error", "invalid_input", "Invalid input.");
        var json = UhpJson.SerializeError(error).GetProperty("error");
        await Assert.That(json.GetProperty("param").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(json.GetProperty("detail").ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}
