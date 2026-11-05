using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.FixtureServer;
using StaticNorth.Uhp.Reference;
using TUnit.Assertions;
using TUnit.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class FixtureSubscriptionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentAndLateReadersObserveTerminalPublication(bool cancel)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var task = new UhpTaskRequest(cancel ? "hold" : "text");
        using var fixture = new ReferenceExecution("owner", "session", task, [], deadline.Token, new FixtureRuntime(),
            new ReferenceRuntimeRequest("session", Path.GetTempPath(), task, new Core.Contracts.Harness { Id = "h", Name = "test", Base = "fixture" }));
        fixture.Start(static () => { });
        var first = ReadAsync(fixture.Execution.Subscribe(deadline.Token));
        var second = ReadAsync(fixture.Execution.Subscribe(deadline.Token));

        if (cancel)
        {
            fixture.Cancellation.Cancel();
        }

        var terminal = await fixture.Execution.Completion.WaitAsync(deadline.Token);
        var late = await ReadAsync(fixture.Execution.Subscribe(deadline.Token));
        var readers = await Task.WhenAll(first, second);

        foreach (var history in readers.Append(late))
        {
            await Assert.That(history.Count).IsEqualTo(5);

            for (var index = 0; index < history.Count; index++)
            {
                await Assert.That(history[index].SequenceNumber).IsEqualTo((long)index);
            }

            var final = (UhpResponseEvent)history[^1];
            await Assert.That(final.Response.Status).IsEqualTo(terminal.Status);
            await Assert.That(final.Response.Status.IsTerminal).IsTrue();
        }
    }

    private static async Task<List<UhpEvent>> ReadAsync(IAsyncEnumerable<UhpEvent> events)
    {
        var result = new List<UhpEvent>();

        await foreach (var update in events)
        {
            result.Add(update);
        }

        return result;
    }
}
