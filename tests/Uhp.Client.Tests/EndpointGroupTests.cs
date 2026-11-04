using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uhp.Client.Tests;

[TestClass]
public sealed class EndpointGroupTests
{
    [TestMethod]
    public async Task GroupConventionRestrictsOnlyUhpRoutes()
    {
        await using var server = await ServerTestHost.StartAsync(configureEndpoints: options =>
        {
            options.ConfigureEndpointGroup = (group, _) => group.RequireHost("uhp.example");
        });
        using var http = server.CreateHttp();

        using var rejected = await http.GetAsync("v1/uhp");
        Assert.AreEqual(HttpStatusCode.NotFound, rejected.StatusCode);

        using var health = await http.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);

        http.DefaultRequestHeaders.Host = "uhp.example";
        using var accepted = await http.GetAsync("v1/uhp");
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
    }

    [TestMethod]
    public async Task GroupPolicyCanExcludeUhpSurface()
    {
        await using var server = await ServerTestHost.StartAsync(configureEndpoints: options =>
        {
            options.MapEndpointGroupsWhen = _ => false;
        });
        using var http = server.CreateHttp();

        using var discovery = await http.GetAsync("v1/uhp");
        Assert.AreEqual(HttpStatusCode.NotFound, discovery.StatusCode);

        using var health = await http.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
    }
}
