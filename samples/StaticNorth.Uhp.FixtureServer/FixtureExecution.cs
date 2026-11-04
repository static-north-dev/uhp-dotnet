using System.Runtime.CompilerServices;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;

namespace StaticNorth.Uhp.FixtureServer;

public sealed class FixtureExecution(string owner, string sessionId, UhpTaskRequest request, IReadOnlyList<string> ignored, CancellationToken stopping) : IDisposable
{
    private readonly object _gate = new();
    private readonly List<UhpEvent> _events = [];
    private readonly TaskCompletionSource<UhpResponse> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private UhpResponse _response = new("resp_" + Guid.NewGuid().ToString("N"), UhpResponseStatus.InProgress, request.Model ?? "fixture-model")
    {
        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Store = request.Store,
        PreviousResponseId = request.PreviousResponseId,
        Metadata = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["session_id"] = UhpJson.CreateElement(writer => writer.WriteStringValue(sessionId)),
            ["ignored_fields"] = UhpJson.CreateElement(writer =>
            {
                writer.WriteStartArray();

                foreach (var field in ignored)
                {
                    writer.WriteStringValue(field);
                }

                writer.WriteEndArray();
            })
        }
    };

    public string Owner { get; } = owner;
    public string SessionId { get; } = sessionId;
    public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    public UhpResponse Response { get { lock (_gate) { return _response; } } }
    public UhpExecution Execution { get; private set; } = null!;

    public void Start(Action finished)
    {
        var initial = Response;
        Publish(new UhpResponseCreated(0, initial));
        Execution = new UhpExecution(initial, _completion.Task, Subscribe);
        _ = CompleteAsync(finished);
    }

    public void Dispose() => Cancellation.Dispose();

    private async Task CompleteAsync(Action finished)
    {
        const string text = "Fixture response.";
        var itemId = "msg_" + Guid.NewGuid().ToString("N");
        Publish(new UhpOutputItemAdded(1, UhpOutputItem.TextMessage(itemId, ""), 0));
        Publish(new UhpTextDelta(2, text, itemId, 0, 0));
        var cancelled = false;

        try
        {
            var hold = request.Input.ToString().Contains("hold", StringComparison.OrdinalIgnoreCase);
            var slow = request.Input.ToString().Contains("slow", StringComparison.OrdinalIgnoreCase);
            await Task.Delay(hold ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(slow ? 400 : 40), Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        UhpResponse terminal;

        lock (_gate)
        {
            terminal = _response with
            {
                Status = cancelled ? UhpResponseStatus.Cancelled : UhpResponseStatus.Completed,
                Output = Array.AsReadOnly([UhpOutputItem.TextMessage(itemId, text)])
            };
            _response = terminal;
        }

        Publish(new UhpOutputItemDone(3, terminal.Output[0], 0));
        Publish(cancelled ? new UhpResponseFailed(4, terminal) : new UhpResponseCompleted(4, terminal));
        finished();
        _completion.TrySetResult(terminal);
    }

    private void Publish(UhpEvent update)
    {
        lock (_gate)
        {
            _events.Add(update);
            var changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult();
        }
    }

    private async IAsyncEnumerable<UhpEvent> Subscribe([EnumeratorCancellation] CancellationToken ct)
    {
        var index = 0;

        while (true)
        {
            UhpEvent? update;
            Task changed;

            lock (_gate)
            {
                update = index < _events.Count ? _events[index++] : null;
                changed = _changed.Task;
            }

            if (update is not null)
            {
                ct.ThrowIfCancellationRequested();
                yield return update;

                if (update is UhpResponseCompleted or UhpResponseIncomplete or UhpResponseFailed)
                {
                    yield break;
                }

                continue;
            }

            await changed.WaitAsync(ct).ConfigureAwait(false);
        }
    }
}
