using System.Collections.Concurrent;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Harnesses.Codex;

public sealed class CodexRuntime(string stateRoot, string authenticationHome, string? executablePath = null) : IUhpHarnessProvider, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CodexRuntimeSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _lostSessions = new(StringComparer.Ordinal);
    private UhpModelCatalog _models = new(new Dictionary<string, UhpModelBackend>());
    public string Base => "codex";
    public bool ProducesArtifacts => true;
    public bool SupportsMemory => true;
    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), $"uhp-codex-{Guid.NewGuid():N}");
    public string DefaultModel { get; private set; } = "";
    private string ExecutablePath => executablePath ?? CodexExecutable.ExecutablePath;

    public async Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var directory = Path.Combine(stateRoot, "catalog");
        Directory.CreateDirectory(directory);
        var home = await PrepareHomeAsync(directory, ct)
            .ConfigureAwait(false);
        await using var connection = await CodexAppServerConnection
            .StartAsync(ExecutablePath, home, directory, ct)
            .ConfigureAwait(false);
        var result = await connection
            .CallAsync("model/list", Empty(), ct)
            .ConfigureAwait(false);
        var models = new List<UhpModel>();
        string? defaultModel = null;

        foreach (var model in result
            .GetProperty("data")
            .EnumerateArray())
        {
            var id = model
                .GetProperty("model")
                .GetString()!;
            var isDefault = model.TryGetProperty("isDefault", out var field) && field.GetBoolean();
            models.Add(new(id, true) { IsDefault = isDefault, Backend = Base });

            if (isDefault)
            {
                defaultModel = id;
            }
        }

        if (models.Count == 0)
        {
            throw new InvalidOperationException("Codex App Server returned no models.");
        }

        defaultModel ??= models[0].Id;
        _models = new(new Dictionary<string, UhpModelBackend> { [Base] = new(defaultModel, models) });
        DefaultModel = defaultModel;
    }

    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_models);
    }

    public async Task<UhpHarnessRunResult> RunAsync(UhpHarnessRunRequest request, Func<string, ValueTask> textDelta, CancellationToken ct)
    {
        try
        {
            return await RunTurnAsync(request, textDelta, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            if (_sessions.TryGetValue(request.SessionId, out var session) && session.Executing && _sessions.TryRemove(request.SessionId, out _))
            {
                _lostSessions.TryAdd(request.SessionId, 0);
                await DisposeSessionAsync(session)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<UhpHarnessRunResult> RunTurnAsync(UhpHarnessRunRequest request, Func<string, ValueTask> textDelta, CancellationToken ct)
    {
        Directory.CreateDirectory(request.Workspace);

        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            var home = await PrepareHomeAsync(Path.Combine(stateRoot, request.SessionId), ct)
                .ConfigureAwait(false);
            var bindings = await CodexHarnessBinding
                .PrepareAsync(request, home, ExecutablePath, ct)
                .ConfigureAwait(false);
            var connection = await CodexAppServerConnection
                .StartAsync(ExecutablePath, home, request.Workspace, ct)
                .ConfigureAwait(false);

            try
            {
                var started = await connection
                    .CallAsync("thread/start", StartParameters(request, home, bindings), ct)
                    .ConfigureAwait(false);
                session = new(connection, started
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString()!, started
                        .GetProperty("model")
                        .GetString()!, home, bindings)
                { Environment = request.Environment };
                _sessions[request.SessionId] = session;
            }
            catch
            {
                await connection
                    .DisposeAsync()
                    .ConfigureAwait(false);
                throw;
            }
        }

        else if (session.Environment?.Id != request.Environment?.Id || session.Environment?.Version != request.Environment?.Version)
        {
            var parameters = StartParameters(request, session.Home, session.Bindings);
            var forked = await session.Connection
                .CallAsync("thread/fork", UhpJson.CreateElement(writer =>
            {
                writer.WriteStartObject(); writer.WriteString("threadId", session.ThreadId);

                foreach (var property in parameters.EnumerateObject())
                {
                    if (!property.NameEquals("dynamicTools"))
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }), ct)
                .ConfigureAwait(false);
            session.ThreadId = forked
                .GetProperty("thread")
                .GetProperty("id")
                .GetString()!;
            session.Environment = request.Environment;
        }

        var model = request.Task.Model ?? request.Harness.DefaultModel ?? session.Model;
        var secretReference = request.Answer is { Kind: "secret" } secret ? session.Secrets.Store(secret.Value) : null;
        var prime = request.Memory is { } memory && !session.MemoryPrimed ? await memory
            .PrimeAsync(ct)
            .ConfigureAwait(false) : null;
        var input = await CodexInput
            .PrepareAsync(request, secretReference, prime, ct)
            .ConfigureAwait(false);
        session.MemoryPrimed |= prime is not null;
        session.Executing = true;
        var startedTurn = await session.Connection
            .CallAsync("turn/start", UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("threadId", session.ThreadId); writer.WriteString("model", model);
            writer.WritePropertyName("input"); input.WriteTo(writer); writer.WriteEndObject();
        }), ct)
            .ConfigureAwait(false);
        var turnId = startedTurn
            .GetProperty("turn")
            .GetProperty("id")
            .GetString()!;

        try
        {
            while (true)
            {
                var packet = await session.Connection
                    .NextAsync(ct)
                    .ConfigureAwait(false);

                if (!packet.TryGetProperty("method", out var method))
                {
                    continue;
                }

                var parameters = packet.GetProperty("params");

                if (parameters.TryGetProperty("threadId", out var packetThread) && packetThread.GetString() != session.ThreadId ||
                    parameters.TryGetProperty("turnId", out var packetTurn) && packetTurn.GetString() != turnId)
                {
                    continue;
                }

                if (method.ValueEquals("item/agentMessage/delta"))
                {
                    await textDelta(parameters
                        .GetProperty("delta")
                        .GetString()!)
                        .ConfigureAwait(false);
                }
                else if (method.ValueEquals("item/tool/call") && parameters
                    .GetProperty("tool")
                    .ValueEquals("ask_user"))
                {
                    var question = CodexInteractive.Read(parameters);
                    await StopTurnAsync(session, turnId, ct)
                        .ConfigureAwait(false);
                    await textDelta("\n" + question.Prompt)
                        .ConfigureAwait(false);
                    return new(model, UhpResponseStatus.Incomplete)
                    {
                        Output = [question.Call, question.Request],
                        IncompleteDetails = UhpJson.CreateElement(static writer =>
                        {
                            writer.WriteStartObject(); writer.WriteString("reason", "input_required"); writer.WriteEndObject();
                        })
                    };
                }
                else if (method.ValueEquals("item/tool/call") && parameters
                    .GetProperty("tool")
                    .ValueEquals("use_secret"))
                {
                    var reference = parameters
                        .GetProperty("arguments")
                        .GetProperty("reference")
                        .GetString()!;
                    var fingerprint = session.Secrets.ConsumeFingerprint(reference);
                    await session.Connection
                        .RespondAsync(packet.GetProperty("id"), CodexInteractive.ToolResult(fingerprint), ct)
                        .ConfigureAwait(false);
                }
                else if (method.ValueEquals("item/tool/call") && parameters
                    .GetProperty("tool")
                    .GetString() is { } tool && tool.StartsWith("memory_", StringComparison.Ordinal))
                {
                    var result = await CodexMemoryTools
                        .InvokeAsync(request.Memory, tool, parameters.GetProperty("arguments"), ct)
                        .ConfigureAwait(false);
                    await session.Connection
                        .RespondAsync(packet.GetProperty("id"), result, ct)
                        .ConfigureAwait(false);
                }
                else if (method.ValueEquals("thread/compacted"))
                {
                    session.MemoryPrimed = false;
                }
                else if (method.ValueEquals("turn/completed"))
                {
                    var turn = parameters.GetProperty("turn");

                    if (turn
                        .GetProperty("id")
                        .GetString() != turnId)
                    {
                        continue;
                    }

                    var status = turn
                        .GetProperty("status")
                        .GetString();

                    await session.Connection
                        .CallAsync("thread/backgroundTerminals/clean", ThreadParameters(session.ThreadId), ct)
                        .ConfigureAwait(false);
                    session.Executing = false;

                    return new(model,
                        status == "completed" ? UhpResponseStatus.Completed : status == "interrupted" ? UhpResponseStatus.Cancelled : UhpResponseStatus.Failed);
                }
                else if (packet.TryGetProperty("id", out var id))
                {
                    // No approval can expand this test host's filesystem or network permissions.
                    await session.Connection
                        .RespondAsync(id, UhpJson.CreateElement(static writer =>
                    {
                        writer.WriteStartObject(); writer.WriteString("decision", "decline"); writer.WriteEndObject();
                    }), ct)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await StopTurnAsync(session, turnId, cleanup.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                _sessions.TryRemove(request.SessionId, out _);
                _lostSessions.TryAdd(request.SessionId, 0);
                await DisposeSessionAsync(session)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    public void ValidateSession(string sessionId)
    {
        if (_lostSessions.ContainsKey(sessionId))
        {
            throw UhpRequestException.InvalidRequest(409, "staticnorth_session_runtime_unavailable", "The native session could not be recovered after a runtime failure.");
        }
    }

    private static async Task StopTurnAsync(CodexRuntimeSession session, string turnId, CancellationToken ct)
    {
        await session.Connection
            .CallAsync("turn/interrupt", UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("threadId", session.ThreadId); writer.WriteString("turnId", turnId); writer.WriteEndObject();
        }), ct)
            .ConfigureAwait(false);

        while (true)
        {
            var packet = await session.Connection
                .NextAsync(ct)
                .ConfigureAwait(false);

            if (packet.TryGetProperty("method", out var method) && method.ValueEquals("turn/completed") &&
                packet
                    .GetProperty("params")
                    .GetProperty("threadId")
                    .GetString() == session.ThreadId &&
                packet
                    .GetProperty("params")
                    .GetProperty("turn")
                    .GetProperty("id")
                    .GetString() == turnId)
            {
                break;
            }
        }

        await session.Connection
            .CallAsync("thread/backgroundTerminals/clean", ThreadParameters(session.ThreadId), ct)
            .ConfigureAwait(false);
        session.Executing = false;
    }

    private static async ValueTask DisposeSessionAsync(CodexRuntimeSession session)
    {
        session.Secrets.Dispose();
        await session.Connection
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    private JsonElement StartParameters(UhpHarnessRunRequest request, string home, List<CodexMcpBinding> bindings) => UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("cwd", request.Workspace); writer.WriteString("approvalPolicy", "never");
        writer.WriteString("permissions", "uhp-session"); writer.WriteBoolean("ephemeral", true);
        writer.WriteString("model", request.Task.Model ?? request.Harness.DefaultModel);
        writer.WriteString("developerInstructions", request.Harness.SystemPrompt + (request.Environment is { } environment
            ? $"\nThe project is mounted read-only and shared at {environment.Mount}. Its dependencies are installed. Write task outputs only in your private working directory {request.Workspace}. Enable pipefail for shell pipelines so failures retain their exit status. Project entry: {environment.Entry}"
            : ""));
        writer.WriteStartArray("dynamicTools");
        CodexInteractive.WriteTool(writer);

        if (request.Memory is { } memory)
        {
            CodexMemoryTools.Write(writer, memory);
        }

        writer.WriteEndArray();
        writer.WriteStartObject("config");
        writer.WriteString("default_permissions", "uhp-session");
        writer.WriteNumber("project_doc_max_bytes", 0);
        writer.WriteStartObject("features"); writer.WriteBoolean("network_proxy", request.Environment is not null); writer.WriteEndObject();

        if (request.Environment is { } executionEnvironment)
        {
            writer.WriteStartObject("shell_environment_policy"); writer.WriteString("inherit", "none"); writer.WriteStartObject("set");
            writer.WriteString("PATH", Path.Combine(executionEnvironment.Root, ".venv", "bin") + Path.PathSeparator + Path.Combine(executionEnvironment.Root, "node_modules", ".bin") + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin"));
            writer.WriteString("PYTHONPATH", executionEnvironment.Mount); writer.WriteString("NODE_PATH", Path.Combine(executionEnvironment.Mount, "node_modules")); writer.WriteString("PROJECT_ROOT", executionEnvironment.Mount);
            writer.WriteEndObject(); writer.WriteEndObject();
        }

        CodexHarnessBinding.Write(writer, bindings, request.Harness.DisabledTools);
        writer.WriteStartObject("permissions"); writer.WriteStartObject("uhp-session");
        writer.WriteStartObject("filesystem"); writer.WriteString(":minimal", "read"); writer.WriteString(ExecutablePath, "read"); writer.WriteString(request.Workspace, "write"); writer.WriteString(home, "read"); writer.WriteString(Path.Combine(home, "auth.json"), "deny");
        writer.WriteString(Path.Combine(request.Workspace, ".uhp", "plugins"), "read"); writer.WriteString(Path.Combine(request.Workspace, ".agents"), "read");

        if (request.Environment is { } readOnlyEnvironment)
        {
            writer.WriteString(readOnlyEnvironment.Root, "read"); writer.WriteString(readOnlyEnvironment.Mount, "read");

            if (OperatingSystem.IsMacOS() && Directory.Exists("/opt/homebrew"))
            {
                writer.WriteString("/opt/homebrew", "read");
            }
            else if (Directory.Exists("/usr/local"))
            {
                writer.WriteString("/usr/local", "read");
            }
        }

        writer.WriteEndObject();

        if (request.Environment is not null)
        {
            CodexNetworkPolicy.Write(writer);
        }
        else
        {
            writer.WriteStartObject("network"); writer.WriteBoolean("enabled", false); writer.WriteEndObject();
        }
        writer.WriteEndObject(); writer.WriteEndObject();
        writer.WriteEndObject(); writer.WriteEndObject();
    });

    private async Task<string> PrepareHomeAsync(string directory, CancellationToken ct)
    {
        var home = Path.Combine(directory, "codex-home");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(home);
        }
        else
        {
            Directory.CreateDirectory(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var credentials = await System.IO.File
            .ReadAllBytesAsync(Path.Combine(authenticationHome, "auth.json"), ct)
            .ConfigureAwait(false);

        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Options = FileOptions.Asynchronous };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using var file = new FileStream(Path.Combine(home, "auth.json"), options);
            await file
                .WriteAsync(credentials, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(credentials);
        }

        await System.IO.File
            .WriteAllTextAsync(Path.Combine(home, "config.toml"), "approval_policy = \"never\"\nweb_search = \"disabled\"\n[features]\napps = false\nworkspace_dependencies = false\nskip_host_skill_discovery = true\n[shell_environment_policy]\ninherit = \"none\"\n", ct)
            .ConfigureAwait(false);
        return home;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _sessions)
        {
            await DisposeSessionAsync(entry.Value)
                .ConfigureAwait(false);
        }

        _sessions.Clear();

        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot, true);
        }
    }

    public async ValueTask DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await DisposeSessionAsync(session)
                .ConfigureAwait(false);
        }

        _lostSessions.TryRemove(sessionId, out _);
        var privateState = Path.Combine(stateRoot, sessionId);

        if (Directory.Exists(privateState))
        {
            Directory.Delete(privateState, true);
        }

        var workspace = Path.Combine(WorkspaceRoot, sessionId);

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, true);
        }
    }

    private static JsonElement Empty() => UhpJson.CreateElement(static writer => { writer.WriteStartObject(); writer.WriteEndObject(); });
    private static JsonElement ThreadParameters(string threadId) => UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("threadId", threadId); writer.WriteEndObject();
    });
}
