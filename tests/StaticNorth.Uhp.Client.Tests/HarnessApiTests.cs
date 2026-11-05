using System.Net;
using StaticNorth.Uhp.Core;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class HarnessApiTests
{
    [Test]
    public async Task ManagedHarnessRunsAndItsDeletedConfigurationKeepsHistory()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true, harnesses: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var harness = await client.CreateHarnessAsync(new() { Base = "fixture", Name = "managed" });
        var response = await client.RunAsync(new("task") { HarnessId = harness.Id });
        await Assert
            .That((await client.GetSessionAsync(response.SessionId!)).HarnessId)
            .IsEqualTo(harness.Id);
        await client.DeleteHarnessAsync(harness.Id);
        await Assert
            .That((await client.GetSessionAsync(response.SessionId!)).HarnessId)
            .IsEqualTo(harness.Id);
        await Assert
            .That((Func<Task>)(async () => await client.GetHarnessAsync(harness.Id)))
            .ThrowsExactly<UhpApiException>();
    }

    [Test]
    public async Task HarnessIdempotencyRejectsDifferentRequestsAndExpiresOnDelete()
    {
        await using var server = await ServerTestHost.StartAsync(harnesses: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var body = new Wire.HarnessCreate { Base = "fixture", Name = "managed" };
        var first = await client.CreateHarnessAsync(body, "create-key");
        await Assert
            .That((await client.CreateHarnessAsync(body, "create-key")).Id)
            .IsEqualTo(first.Id);
        var error = (await Assert
            .That((Func<Task>)(async () => await client.CreateHarnessAsync(body with { Name = "other" }, "create-key")))
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(error.Code)
            .IsEqualTo("idempotency_key_reused");
        await client.DeleteHarnessAsync(first.Id);
        await Assert
            .That((await client.CreateHarnessAsync(body, "create-key")).Id)
            .IsNotEqualTo(first.Id);
    }

    [Test]
    public async Task FilesOnlyPluginInstallationDerivesItsReadShape()
    {
        await using var server = await ServerTestHost.StartAsync(harnesses: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var created = await client.CreateHarnessAsync(new() { Base = "fixture", Plugins = [new() { Files = Package() }] });
        await Assert
            .That(created.Plugins![0].Name)
            .IsEqualTo("demo");
        await Assert
            .That(created.Plugins[0].McpServers![0].Transport)
            .IsEqualTo("stdio");
        await Assert
            .That(created.Plugins[0].Skills![0].Name)
            .IsEqualTo("demo-skill");
        var read = await client.GetHarnessAsync(created.Id);
        await Assert
            .That(read.Raw.GetProperty("plugins")[0]
                .GetProperty("manifest")
                .GetProperty("name")
                .GetString())
            .IsEqualTo("demo");
        await Assert
            .That((await client.GetPluginFilesAsync(created.Id, "demo")).Files.Length)
            .IsEqualTo(3);
    }

    [Test]
    public async Task PackageTraversalIsRejectedBeforeConfigurationChanges()
    {
        await using var server = await ServerTestHost.StartAsync(harnesses: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var error = (await Assert
            .That((Func<Task>)(async () => await client.CreateHarnessAsync(new() { Base = "fixture", Plugins = [new() { Files = [new() { Path = "../escape", Content = "bad" }] }] })))
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(error.StatusCode)
            .IsEqualTo(HttpStatusCode.UnprocessableEntity);
        await Assert
            .That(error.Code)
            .IsEqualTo("plugin_invalid");
        await Assert
            .That((await client.ListHarnessesAsync()).Count)
            .IsEqualTo(1);
    }

    private static Wire.SkillFile[] Package() =>
    [
        new() { Path = "plugin.json", Content = "{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"demo\"}" },
        new() { Path = "mcp.json", Content = "{\"mcpServers\":{\"tool\":{\"type\":\"stdio\",\"command\":\"./tool\",\"args\":[\"${PLUGIN_DATA}\"]}}}" },
        new() { Path = "skills/demo-skill/SKILL.md", Content = "---\nname: demo-skill\n---\n" }
    ];
}
