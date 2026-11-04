using System.Net;
using Microsoft.AspNetCore.Builder;
using TUnit.Core;
using TUnit.Assertions;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class EndpointGroupTests
{
    [Test]
    public async Task GroupConventionRestrictsOnlyUhpRoutes()
    {
        await using var server = await ServerTestHost.StartAsync(configureEndpoints: options =>
        {
            options.ConfigureEndpointGroup = (group, _) => group.RequireHost("uhp.example");
        });
        using var http = server.CreateHttp();
        using var rejected = await http.GetAsync("v1/uhp");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var health = await http.GetAsync("/health");
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
        http.DefaultRequestHeaders.Host = "uhp.example";
        using var accepted = await http.GetAsync("v1/uhp");
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GroupPolicyCanExcludeUhpSurface()
    {
        await using var server = await ServerTestHost.StartAsync(configureEndpoints: options =>
        {
            options.MapEndpointGroupsWhen = _ => false;
        });
        using var http = server.CreateHttp();
        using var discovery = await http.GetAsync("v1/uhp");
        await Assert.That(discovery.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var health = await http.GetAsync("/health");
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
