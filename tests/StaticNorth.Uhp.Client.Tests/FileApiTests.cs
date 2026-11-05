using System.Net;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class FileApiTests
{
    [Test]
    public async Task MultipartUploadPreservesBytesAndCallerStreamOwnership()
    {
        await using var server = await ServerTestHost.StartAsync(files: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        using var source = new MemoryStream([0, 1, 2, 3, 255]);
        var file = await client.UploadFileAsync(new(source, "application/octet-stream", "blob.bin"));
        await Assert.That(file.Bytes).IsEqualTo(5L);
        await Assert.That(source.CanRead).IsTrue();
        await using var download = await client.DownloadFileAsync(file.ContainerId!, file.Id);
        using var destination = new MemoryStream();
        await download.Content.CopyToAsync(destination);
        await Assert.That(destination.GetBuffer().AsSpan(0, (int)destination.Length).SequenceEqual((ReadOnlySpan<byte>)[0, 1, 2, 3, 255])).IsTrue();
        await Assert.That(download.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
    }

    [Test]
    public async Task ArtifactsRemainScopedToTheirPrincipalContainerAndSession()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true, files: true);
        using var http = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(http);
        var bob = await ServerTestHost.ConnectAsync(http, "bob");
        var response = await alice.RunAsync(new("artifact"));
        var file = server.Backend.SeedArtifact(response.SessionId!, "alice", "nested/result.bin", [1, 2, 3]);
        await Assert.That((await alice.ListSessionFilesAsync(response.SessionId!)).Files.Length).IsEqualTo(1);
        var error = (await Assert.That((Func<Task>)(async () => await bob.DownloadFileAsync(file.ContainerId!, file.Id))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var wrongContainer = (await Assert.That((Func<Task>)(async () => await alice.DownloadFileAsync("cntr_other", file.Id))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(wrongContainer.Code).IsEqualTo("file_not_found");
        await alice.DeleteSessionAsync(response.SessionId!);
        await Assert.That((Func<Task>)(async () => await alice.DownloadFileAsync(file.ContainerId!, file.Id))).ThrowsExactly<UhpApiException>();
    }

    [Test]
    public async Task MultipartLimitCountsTheFileBytes()
    {
        await using var server = await ServerTestHost.StartAsync(files: true, maximumUploadBytes: 4);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        using var source = new MemoryStream(new byte[5]);
        var error = (await Assert.That((Func<Task>)(async () => await client.UploadFileAsync(new(source, FileName: "large.bin")))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        await Assert.That(error.Code).IsEqualTo("file_too_large");
    }

    [Test]
    public async Task ArchiveContainsTheCompleteArtifactBytes()
    {
        await using var server = await ServerTestHost.StartAsync(files: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var response = await client.RunAsync(new("artifact"));
        server.Backend.SeedArtifact(response.SessionId!, "alice", "nested/result.bin", [7, 8, 9]);
        await using var download = await client.DownloadSessionArchiveAsync(response.SessionId!);
        using var archiveBytes = new MemoryStream();
        await download.Content.CopyToAsync(archiveBytes);
        archiveBytes.Position = 0;
        await using var archive = new System.IO.Compression.ZipArchive(archiveBytes);
        await using var entry = await archive.GetEntry("nested/result.bin")!.OpenAsync();
        using var bytes = new MemoryStream();
        await entry.CopyToAsync(bytes);
        await Assert.That(bytes.GetBuffer().AsSpan(0, (int)bytes.Length).SequenceEqual((ReadOnlySpan<byte>)[7, 8, 9])).IsTrue();
    }
}
