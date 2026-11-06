using Microsoft.Extensions.DependencyInjection;
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Harnesses.OpenCode;
using StaticNorth.Uhp.Reference;
using StaticNorth.Uhp.Core.Providers;
using StaticNorth.Uhp.FixtureServer;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class OpenCodeRuntimeTests
{
    [Test]
    public async Task StreamsMatchingTextAndResumesAfterAnAbortThatRacesSubmission()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var directory = Directory.CreateTempSubdirectory("uhp-opencode-");

        try
        {
            var fixture = new OpenCodeHttpFixture();
            using var upstream = new HttpClient(fixture) { BaseAddress = new Uri("http://127.0.0.1:4096") };
            var connection = new OpenCodeConnection(upstream);
            await Assert
                .That(await connection.HasModelAsync(directory.FullName, "fixture", "demo", ct))
                .IsTrue();
            await Assert
                .That(await connection.HasModelAsync(directory.FullName, "fixture", "missing", ct))
                .IsFalse();
            await using var services = new ServiceCollection()
                .AddSingleton<IUhpHarnessProvider, FixtureRuntime>()
                .AddUhpOpenCode(upstream, directory.FullName, "fixture", "demo")
                .BuildServiceProvider();
            var providers = new UhpHarnessProviderRegistry(services.GetServices<IUhpHarnessProvider>());
            await providers.InitializeAsync(ct);
            using var backend = new ReferenceBackend(providers, "fixture");
            await using var host = await ServerTestHost.StartAsync(sessions: true, backend: backend);
            using var http = host.CreateHttp();
            var client = await ServerTestHost.ConnectAsync(http);
            var harnesses = await client.ListHarnessesAsync(ct);
            var openCode = harnesses.Single(harness => harness.Base == "opencode");
            await Assert
                .That(openCode.Id)
                .IsEqualTo("chrn_opencode");
            var models = await client.GetHarnessModelsAsync(openCode.Id, ct);
            await Assert
                .That(models.Default)
                .IsEqualTo("fixture/demo");

            var first = await StreamAsync(client, fixture, openCode.Id, ct);
            var held = await CancelAsync(client, fixture, first.Id, ct);
            await Assert
                .That(held.Status)
                .IsEqualTo(UhpResponseStatus.Cancelled);

            await Assert
                .That(fixture.AbortCount)
                .IsGreaterThanOrEqualTo(2);
            var resumed = await client.RunAsync(new("follow") { PreviousResponseId = held.Id }, ct);
            await Assert
                .That(resumed.Status)
                .IsEqualTo(UhpResponseStatus.Completed);
            await Assert
                .That(resumed.SessionId)
                .IsEqualTo(first.SessionId);
            await client.DeleteSessionAsync(resumed.SessionId!, ct: ct);
            await Assert
                .That(fixture.Deleted)
                .IsTrue();
            await Assert
                .That(Directory.Exists(fixture.Workspace))
                .IsFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<UhpResponse> CancelAsync(UhpClient client, OpenCodeHttpFixture fixture, string previousResponseId, CancellationToken ct)
    {
        await foreach (var update in client.StreamAsync(new("hold") { PreviousResponseId = previousResponseId }, ct))
        {
            if (update is UhpResponseCreated created)
            {
                await fixture.HoldStarted.Task.WaitAsync(ct);
                await client.CancelResponseAsync(created.Response.Id, ct);
            }
            else if (update is UhpResponseFailed cancelled)
            {
                return cancelled.Response;
            }
        }

        throw new InvalidOperationException("OpenCode did not cancel the turn.");
    }

    private static async Task<UhpResponse> StreamAsync(UhpClient client, OpenCodeHttpFixture fixture, string harnessId, CancellationToken ct)
    {
        var streamed = "";

        await foreach (var update in client.StreamAsync(new("start") { HarnessId = harnessId }, ct))
        {
            if (update is UhpTextDelta delta)
            {
                if (streamed.Length == 0)
                {
                    await Assert
                        .That(delta.Text)
                        .IsEqualTo("Purple");
                    fixture.FirstDeltaObserved.TrySetResult();
                }

                streamed += delta.Text;
            }
            else if (update is UhpResponseCompleted completed)
            {
                await Assert
                    .That(streamed)
                    .IsEqualTo("Purple tulips");
                await Assert
                    .That(completed.Response.Output[0].Text)
                    .IsEqualTo(streamed);
                return completed.Response;
            }
        }

        throw new InvalidOperationException("OpenCode did not complete the stream.");
    }
}
