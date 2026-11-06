using System.Collections.Concurrent;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Harnesses.ClaudeCode;

public sealed class ClaudeCodeRuntime(string executablePath, string stateRoot, string? apiKey, string model,
    string? subscriptionProfileDirectory = null, string? cliConfigurationDirectory = null) : IUhpHarnessProvider
{
    private readonly ConcurrentDictionary<string, (Guid Id, bool Ready)> _sessions = new(StringComparer.Ordinal);

    public string Base => "claude-code";
    public string DisplayName => "Claude Code";
    public string DefaultModel => model;
    public string WorkspaceRoot { get; } = Path.Combine(stateRoot, "workspaces");
    public bool SupportsMemory => false;
    private UhpModelBackend Models => new(model, [new(model, File.Exists(executablePath)) { IsDefault = true, Backend = Base }]);

    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new UhpModelCatalog(new Dictionary<string, UhpModelBackend> { [Base] = Models }));
    }

    public void ValidateTask(UhpTaskRequest task)
    {
        if ((task.Model is { } requested && requested != model) || !File.Exists(executablePath))
        {
            throw UhpRequestException.InvalidRequest(422, "model_unavailable", "The Claude Code harness cannot serve the requested model.");
        }

        UhpHarnessTextInput.Validate(task, "Claude Code");
    }

    public void ValidateSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var nativeSession) && !nativeSession.Ready)
        {
            throw UhpRequestException.InvalidRequest(409, "staticnorth_session_runtime_unavailable", "The native session could not be recovered after a runtime failure.");
        }
    }

    public async Task<UhpHarnessRunResult> RunAsync(UhpHarnessRunRequest request, Func<string, ValueTask> textDelta, CancellationToken ct)
    {
        var prior = _sessions.GetValueOrDefault(request.SessionId);
        var nativeId = subscriptionProfileDirectory is not null && prior.Id == Guid.Empty ? Guid.NewGuid() : prior.Id;
        _sessions[request.SessionId] = (nativeId, false);

        var configuration = Path.Combine(stateRoot, "sessions", request.SessionId);
        CreatePrivateDirectory(configuration);
        CreatePrivateDirectory(request.Workspace);
        var runner = new ClaudeCodeRunner(executablePath, subscriptionProfileDirectory is null ? configuration : cliConfigurationDirectory);
        var instructions = string.IsNullOrWhiteSpace(request.Task.Instructions) ? null : Path.Combine(configuration, "task-instructions.txt");
        var result = default(JsonElement);
        var streamed = false;

        try
        {
            if (instructions is not null)
            {
                await File
                    .WriteAllTextAsync(instructions, request.Task.Instructions, ct)
                    .ConfigureAwait(false);
            }

            var prompt = UhpHarnessTextInput.Prompt(request.Task.Input);
            var messages = subscriptionProfileDirectory is null
                ? runner.RunAsync(prompt, request.Workspace, apiKey!, prior.Ready ? prior.Id : null, model, instructions, ct)
                : runner.RunWithSubscriptionAsync(prompt, request.Workspace, nativeId, prior.Ready, model, instructions, ct);

            await foreach (var message in messages.ConfigureAwait(false))
            {
                if (!message.TryGetProperty("type"u8, out var type))
                {
                    continue;
                }

                if (type.ValueEquals("stream_event"u8) && message.TryGetProperty("event"u8, out var update) &&
                    update.TryGetProperty("delta"u8, out var delta) && delta.TryGetProperty("type"u8, out var kind) && kind.ValueEquals("text_delta"u8) &&
                    delta.TryGetProperty("text"u8, out var text) && text.GetString() is { Length: > 0 } chunk)
                {
                    await textDelta(chunk)
                        .ConfigureAwait(false);
                    streamed = true;
                }
                else if (type.ValueEquals("system"u8) && message.TryGetProperty("subtype"u8, out var subtype) && subtype.ValueEquals("init"u8) &&
                    message.TryGetProperty("model"u8, out var actual) && !actual.ValueEquals(model))
                {
                    throw new IOException("Claude Code selected an unexpected model.");
                }
                else if (type.ValueEquals("result"u8))
                {
                    result = message;
                }
            }

            if (!streamed && result.TryGetProperty("result"u8, out var final) && final.GetString() is { Length: > 0 } answer)
            {
                await textDelta(answer)
                    .ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            if (instructions is not null)
            {
                File.Delete(instructions);
            }
        }

        _sessions[request.SessionId] = (result.GetProperty("session_id"u8).GetGuid(), true);
        return new(model, UhpResponseStatus.Completed);
    }

    public ValueTask DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var native = _sessions.GetValueOrDefault(sessionId);
        _sessions[sessionId] = (native.Id, false);

        if (subscriptionProfileDirectory is not null && native.Id != Guid.Empty)
        {
            DeleteSubscriptionTranscript(subscriptionProfileDirectory, native.Id, ct);
        }

        DeleteDirectory(Path.Combine(stateRoot, "sessions", sessionId));
        DeleteDirectory(Path.Combine(WorkspaceRoot, sessionId));
        _sessions.TryRemove(sessionId, out _);
        return ValueTask.CompletedTask;
    }

    private static void DeleteSubscriptionTranscript(string profile, Guid sessionId, CancellationToken ct)
    {
        var projects = Path.Combine(profile, "projects");

        if (!Directory.Exists(projects) || (File.GetAttributes(projects) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        var fileName = $"{sessionId:D}.jsonl";
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };

        // Claude Code owns the project folder names. Match only the UUID generated for this UHP session.
        foreach (var project in Directory.EnumerateDirectories(projects, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(Path.Combine(project, fileName));
        }
    }

    private static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
