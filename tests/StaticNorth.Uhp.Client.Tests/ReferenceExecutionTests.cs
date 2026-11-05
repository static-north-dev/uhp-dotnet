using System.Net;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Reference;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ReferenceExecutionTests
{
    [Test]
    public async Task TimerOverflowIsRefusedBeforeAcceptingTheTask()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var error = (await Assert.That((Func<Task>)(async () => await client.RunAsync(new UhpTaskRequest("test")
        {
            TimeoutSeconds = uint.MaxValue
        }))).ThrowsExactly<UhpApiException>())!;

        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(server.Backend.SubmissionCount).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeFaultPublishesOneTerminalAndReleasesTheSession()
    {
        var task = new UhpTaskRequest("input");
        var request = new ReferenceRuntimeRequest("session", Path.GetTempPath(), task, new() { Id = "h", Name = "test", Base = "failing" });
        using var execution = new ReferenceExecution("owner", "session", task, [], CancellationToken.None, new FailingReferenceRuntime(), request);
        var released = 0;
        execution.Start(() => released++);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await execution.Execution.Completion.WaitAsync(deadline.Token);
        await Assert.That(result.Status).IsEqualTo(UhpResponseStatus.Failed);
        await Assert.That(released).IsEqualTo(1);
        var terminals = 0;

        await foreach (var item in execution.Execution.Subscribe(deadline.Token))
        {
            if (item is UhpResponseCompleted or UhpResponseFailed or UhpResponseIncomplete)
            {
                terminals++;
            }
        }

        await Assert.That(terminals).IsEqualTo(1);
    }
}
