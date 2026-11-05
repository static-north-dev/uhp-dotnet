using System.Runtime.CompilerServices;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public sealed class ReferenceExecution(string owner, string sessionId, UhpTaskRequest request, IReadOnlyList<string> ignored, CancellationToken stopping, IReferenceRuntime runtime, ReferenceRuntimeRequest runtimeRequest) : IDisposable
{
    internal const long MaximumTimeoutSeconds = (uint.MaxValue - 1L) / 1000;
    private const long DefaultTimeoutSeconds = 300;
    private readonly object _gate = new();
    private readonly List<UhpEvent> _events = [];
    private readonly TaskCompletionSource<UhpResponse> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private UhpResponse _response = new($"resp_{Guid.NewGuid():N}", UhpResponseStatus.InProgress, request.Model ?? runtimeRequest.Harness.DefaultModel ?? runtime.DefaultModel)
    {
        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Store = request.Store ?? true,
        PreviousResponseId = request.PreviousResponseId,
        Metadata = CreateMetadata(request, sessionId, ignored, runtimeRequest)
    };

    public string Owner { get; } = owner;
    public UhpTaskRequest Request { get; } = request;
    public long? CompletedAt { get; private set; }
    public string SessionId { get; } = sessionId;
    public string HarnessId { get; } = runtimeRequest.Harness.Id;
    public Environments.ReferenceEnvironmentSelection? Environment { get; } = runtimeRequest.Environment;
    public string? MemoryId { get; } = runtimeRequest.Memory?.DefaultId;
    public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    public UhpResponse Response { get { lock (_gate) { return _response; } } }
    public UhpExecution Execution { get; private set; } = null!;
    private readonly System.Text.StringBuilder _text = new();
    private readonly string _itemId = $"msg_{Guid.NewGuid():N}";
    private long _sequence;
    private ReferenceRuntimeRequest? _runtimeRequest = runtimeRequest;

    public void Start(Action finished)
    {
        var initial = Response;
        Publish(new UhpResponseCreated(_sequence++, initial));
        Execution = new UhpExecution(initial, _completion.Task, Subscribe);
        var accepted = _runtimeRequest ?? throw new InvalidOperationException("The execution was already started.");
        _runtimeRequest = null;
        _ = CompleteAsync(finished, accepted);
    }

    public void Dispose() => Cancellation.Dispose();

    private async Task CompleteAsync(Action finished, ReferenceRuntimeRequest accepted)
    {
        Publish(new UhpOutputItemAdded(_sequence++, UhpOutputItem.TextMessage(_itemId, ""), 0));
        ReferenceRuntimeResult result;
        IReadOnlyList<Wire.File> artifacts = [];
        CancellationTokenSource? budget = null;
        var runToken = Cancellation.Token;

        try
        {
            var seconds = Request.TimeoutSeconds is > 0 ? Request.TimeoutSeconds.Value : accepted.Harness.TimeoutSeconds is > 0 ? accepted.Harness.TimeoutSeconds.Value : DefaultTimeoutSeconds;
            budget = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Min(seconds, MaximumTimeoutSeconds)));
            runToken = budget.Token;
            result = await runtime
                .RunAsync(accepted, OnTextAsync, runToken)
                .ConfigureAwait(false);

            if (accepted.CaptureArtifactsAsync is { } capture && (result.Status == UhpResponseStatus.Completed || result.Status == UhpResponseStatus.Incomplete))
            {
                artifacts = await capture(runToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            result = Cancellation.IsCancellationRequested
                ? new(runtime.DefaultModel, UhpResponseStatus.Cancelled)
                : new(runtime.DefaultModel, UhpResponseStatus.Incomplete)
                {
                    IncompleteDetails = UhpJson.CreateElement(static writer =>
                    {
                        writer.WriteStartObject(); writer.WriteString("reason", "timeout"); writer.WriteEndObject();
                    })
                };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Runtime failure: " + exception.GetType().Name);
            result = new(runtime.DefaultModel, UhpResponseStatus.Failed)
            {
                Error = new("server_error", "harness_execution_failed", "The harness execution failed.")
            };
        }
        finally
        {
            budget?.Dispose();
        }

        UhpResponse terminal;
        var output = new UhpOutputItem[result.Output.Count + 1];
        var text = _text.ToString();
        output[0] = artifacts.Count == 0 ? UhpOutputItem.TextMessage(_itemId, text) : ArtifactMessage(text, artifacts, accepted.ArtifactBaseUrl);

        for (var index = 0; index < result.Output.Count; index++)
        {
            output[index + 1] = result.Output[index];
        }

        lock (_gate)
        {
            terminal = _response with
            {
                Status = result.Status,
                Model = result.Model,
                Error = result.Error,
                Usage = result.Usage,
                IncompleteDetails = result.IncompleteDetails,
                Output = Array.AsReadOnly(output)
            };
            _response = terminal;
            CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        Publish(new UhpOutputItemDone(_sequence++, terminal.Output[0], 0));

        for (var index = 1; index < output.Length; index++)
        {
            Publish(new UhpOutputItemAdded(_sequence++, output[index], index));
            Publish(new UhpOutputItemDone(_sequence++, output[index], index));
        }

        Publish(result.Status == UhpResponseStatus.Completed ? new UhpResponseCompleted(_sequence++, terminal) : result.Status == UhpResponseStatus.Incomplete ? new UhpResponseIncomplete(_sequence++, terminal) : new UhpResponseFailed(_sequence++, terminal));

        try
        {
            finished();
        }
        finally
        {
            _completion.TrySetResult(terminal);
        }
    }

    private ValueTask OnTextAsync(string text)
    {
        _text.Append(text);
        Publish(new UhpTextDelta(_sequence++, text, _itemId, 0, 0));
        return ValueTask.CompletedTask;
    }

    private UhpOutputItem ArtifactMessage(string text, IReadOnlyList<Wire.File> files, string? baseUrl) => new(UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("id", _itemId);
        writer.WriteString("role", "assistant");
        writer.WriteString("status", "completed");
        writer.WriteStartArray("content");
        writer.WriteStartObject();
        writer.WriteString("type", "output_text");
        writer.WriteString("text", text);
        writer.WriteStartArray("annotations");

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var annotation = new Wire.Annotation
            {
                ContainerId = file.ContainerId,
                FileId = file.Id,
                Filename = file.Filename,
                DownloadUrl = baseUrl is null ? null : $"{baseUrl}/v1/containers/{file.ContainerId}/files/{file.Id}/content"
            };
            System.Text.Json.JsonSerializer.Serialize(writer, annotation, Wire.UhpContractJsonContext.Default.Annotation);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }));

    private static Dictionary<string, System.Text.Json.JsonElement> CreateMetadata(UhpTaskRequest request, string session, IReadOnlyList<string> ignored, ReferenceRuntimeRequest accepted)
    {
        var metadata = request.Metadata is null ? new Dictionary<string, System.Text.Json.JsonElement>() : new(request.Metadata, StringComparer.Ordinal);
        metadata["session_id"] = System.Text.Json.JsonSerializer.SerializeToElement(session, Core.Contracts.UhpContractJsonContext.Default.String);
        metadata["ignored_fields"] = UhpJson.CreateElement(writer =>
        {
            writer.WriteStartArray();

            for (var index = 0; index < ignored.Count; index++)
            {
                writer.WriteStringValue(ignored[index]);
            }

            writer.WriteEndArray();
        });

        if (accepted.Environment is { } environment)
        {
            metadata["environment"] = System.Text.Json.JsonSerializer.SerializeToElement(environment.Id, Core.Contracts.UhpContractJsonContext.Default.String);
            metadata["environment_version"] = UhpJson.CreateElement(writer => writer.WriteNumberValue(environment.Version));
        }

        if (accepted.VoidedInputRequestId is { } voided)
        {
            metadata["voided_input_requests"] = System.Text.Json.JsonSerializer.SerializeToElement<string[]>([voided], Core.Contracts.UhpContractJsonContext.Default.StringArray);
        }

        return metadata;
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

            await changed
                .WaitAsync(ct)
                .ConfigureAwait(false);
        }
    }
}
