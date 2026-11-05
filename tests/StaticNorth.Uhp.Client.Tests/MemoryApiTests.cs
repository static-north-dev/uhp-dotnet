using System.Net;
using System.Text.Json;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class MemoryApiTests
{
    [Test]
    public async Task GrantedWriterIsStampedWithoutChangingTheStorageOwner()
    {
        await using var server = await ServerTestHost.StartAsync(memories: true);
        using var http = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(http);
        var bob = await ServerTestHost.ConnectAsync(http, "bob");
        var root = await alice.CreateMemoryAsync(new() { Name = "shared", Provider = "in-memory" });
        await alice.CreateMemoryGrantAsync(root.Id, new() { Principal = "member:bob", Privileges = ["read", "write"] });
        var record = await bob.RememberRecordAsync(root.Id, new() { Content = Text("written by Bob") });
        await Assert.That(record.WrittenBy!.Id).IsEqualTo("bob");
        await Assert.That((await alice.GetMemoryRecordAsync(root.Id, record.Id)).WrittenBy!.Id).IsEqualTo("bob");
        await Assert.That((await bob.GetMemoryAsync(root.Id)).Privileges.Contains("delete")).IsFalse();
        var error = (await Assert.That((Func<Task>)(async () => await bob.DeleteMemoryAsync(root.Id))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task RestrictedChildCutsOffInheritedGrantsAndRecall()
    {
        await using var server = await ServerTestHost.StartAsync(memories: true);
        using var http = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(http);
        var bob = await ServerTestHost.ConnectAsync(http, "bob");
        var root = await alice.CreateMemoryAsync(new() { Name = "root" });
        var child = await alice.CreateMemoryAsync(new() { Name = "private", ParentId = root.Id, Restricted = true });
        await alice.CreateMemoryGrantAsync(root.Id, new() { Principal = "member:bob", Privileges = ["read"] });
        await alice.RememberRecordAsync(child.Id, new() { Content = Text("private-marker") });
        var recalled = await bob.RecallMemoryAsync(root.Id, new() { Text = "private-marker" });
        await Assert.That(recalled.Results.Length).IsEqualTo(0);
        await Assert.That(recalled.Children!.Length).IsEqualTo(0);
        await Assert.That((Func<Task>)(async () => await bob.GetMemoryAsync(child.Id))).ThrowsExactly<UhpApiException>();
    }

    [Test]
    public async Task HiddenReferenceSurvivesAnEditWithoutExposingTheTargetMemory()
    {
        await using var server = await ServerTestHost.StartAsync(memories: true);
        using var http = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(http);
        var bob = await ServerTestHost.ConnectAsync(http, "bob");
        var root = await alice.CreateMemoryAsync(new() { Name = "private" });
        var child = await alice.CreateMemoryAsync(new() { Name = "shared", ParentId = root.Id });
        var target = await alice.RememberRecordAsync(root.Id, new() { Title = "private target" });
        var source = await alice.RememberRecordAsync(child.Id, new()
        {
            Title = "shared note",
            References = [new() { MemoryId = root.Id, RecordId = target.Id, Rel = "references" }]
        });
        await alice.CreateMemoryGrantAsync(child.Id, new() { Principal = "member:bob", Privileges = ["read", "write"] });
        var visible = await bob.GetMemoryAsync(child.Id);
        await Assert.That(visible.ParentId).IsNull();
        await Assert.That(visible.Ancestors!.Length).IsEqualTo(0);
        var result = await bob.GetMemoryRecordAsync(child.Id, source.Id);
        var reference = result.References![0];
        await Assert.That(reference.RecordId).IsEqualTo(target.Id);
        await Assert.That(reference.MemoryId).IsNull();
        await Assert.That(reference.Rel).IsNull();
        await Assert.That(reference.Available ?? true).IsFalse();
        await bob.ReviseMemoryRecordAsync(child.Id, source.Id, new() { Title = "edited", References = result.References });
        var revised = await alice.GetMemoryRecordAsync(child.Id, source.Id);
        await Assert.That(revised.References![0].MemoryId).IsEqualTo(root.Id);
    }

    [Test]
    public async Task TitleStandsAloneAndForgottenReadbackSurvivesUntilErase()
    {
        await using var server = await ServerTestHost.StartAsync(memories: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var memory = await client.CreateMemoryAsync(new() { Name = "notes" });
        var record = await client.RememberRecordAsync(memory.Id, new() { Title = "A title without content" });
        await Assert.That(record.Content!.Length).IsEqualTo(0);
        await client.ForgetMemoryRecordAsync(memory.Id, record.Id);
        await Assert.That((await client.GetMemoryRecordAsync(memory.Id, record.Id)).Status).IsEqualTo("forgotten");
        await Assert.That((await client.ListMemoryRecordsAsync(memory.Id)).Data!.Length).IsEqualTo(0);
        await client.EraseMemoryRecordsAsync(memory.Id, new() { RecordIds = [record.Id] });
        await Assert.That((Func<Task>)(async () => await client.GetMemoryRecordAsync(memory.Id, record.Id))).ThrowsExactly<UhpApiException>();
    }

    [Test]
    public async Task RecordIdsCannotCrossMemoryBoundaries()
    {
        await using var server = await ServerTestHost.StartAsync(memories: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var first = await client.CreateMemoryAsync(new() { Name = "first" });
        var second = await client.CreateMemoryAsync(new() { Name = "second" });
        var record = await client.RememberRecordAsync(first.Id, new() { Content = Text("original") });
        var error = (await Assert.That((Func<Task>)(async () => await client.ReviseMemoryRecordAsync(second.Id, record.Id, new() { Content = Text("changed") }))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.Code).IsEqualTo("memory_record_not_found");
        await Assert.That((await client.GetMemoryRecordAsync(first.Id, record.Id)).Content![0].Text).IsEqualTo("original");
    }

    private static JsonElement Text(string value)
    {
        using var document = JsonDocument.Parse("\"" + value + "\"");
        return document.RootElement.Clone();
    }
}
