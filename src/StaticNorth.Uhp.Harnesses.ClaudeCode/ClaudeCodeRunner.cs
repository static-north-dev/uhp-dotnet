using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Harnesses.ClaudeCode;

/// <summary>Runs Claude Code in headless mode for a host-owned configuration directory.</summary>
public sealed class ClaudeCodeRunner(string executablePath, string? configurationDirectory)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] InheritedVariables = ["PATH", "HOME", "USER", "USERPROFILE", "TMPDIR", "TEMP", "TMP", "SystemRoot", "LANG"];
    private static readonly string[] Arguments =
    [
        "--print", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
        "--tools", "", "--permission-prompts", "none", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}"
    ];

    /// <summary>Streams the native events of one turn and validates completion when the stream ends.</summary>
    /// <remarks>Authorize any resume ID for this workspace and configuration before passing it here.</remarks>
    public IAsyncEnumerable<JsonElement> RunAsync(
        string prompt, string workingDirectory, string apiKey, Guid? resumeSessionId = null, CancellationToken ct = default) =>
        RunAsync(prompt, workingDirectory, apiKey, resumeSessionId, null, null, ct);

    /// <summary>Streams a turn using the given model and an optional host-owned system prompt file.</summary>
    public IAsyncEnumerable<JsonElement> RunAsync(
        string prompt, string workingDirectory, string apiKey, Guid? resumeSessionId, string? model, string? systemPromptFile,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        return RunCoreAsync(prompt, workingDirectory, apiKey, null, resumeSessionId, model, systemPromptFile, ct);
    }

    /// <summary>Streams a turn using the subscription login already saved by Claude Code.</summary>
    public IAsyncEnumerable<JsonElement> RunWithSubscriptionAsync(string prompt, string workingDirectory,
        Guid sessionId, bool resume, string model, string? systemPromptFile, CancellationToken ct = default) =>
        RunCoreAsync(prompt, workingDirectory, null, resume ? null : sessionId, resume ? sessionId : null, model, systemPromptFile, ct);

    /// <summary>Returns the CLI profile directory if it has a Claude subscription login.</summary>
    public async Task<string?> GetSubscriptionProfileDirectoryAsync(string workingDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = CreateStartInfo(workingDirectory);
        start.RedirectStandardInput = false;
        start.StandardInputEncoding = null;
        start.ArgumentList.Add("auth");
        start.ArgumentList.Add("status");
        start.ArgumentList.Add("--json");

        using var process = Process.Start(start) ?? throw new IOException("Claude Code could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);

        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(ct))
                .ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            using var status = JsonDocument.Parse(await stdout.ConfigureAwait(false));
            var root = status.RootElement;
            return root.TryGetProperty("loggedIn"u8, out var signedIn) && signedIn.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("authMethod"u8, out var auth) && auth.ValueEquals("claude.ai"u8) &&
                root.TryGetProperty("subscriptionType"u8, out var subscription) && subscription.ValueKind == JsonValueKind.String &&
                !subscription.ValueEquals(""u8) && root.TryGetProperty("configDirectory"u8, out var directory) &&
                directory.ValueKind == JsonValueKind.String && directory.GetString() is { } path && Path.IsPathFullyQualified(path)
                ? path : null;
        }
        finally
        {
            await StopProcessAsync(process)
                .ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<JsonElement> RunCoreAsync(
        string prompt, string workingDirectory, string? apiKey, Guid? newSessionId, Guid? resumeSessionId, string? model, string? systemPromptFile,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ct.ThrowIfCancellationRequested();

        var start = CreateStartInfo(workingDirectory);

        if (apiKey is not null)
        {
            start.ArgumentList.Add("--bare");
            start.Environment["ANTHROPIC_API_KEY"] = apiKey;
        }
        else
        {
            start.ArgumentList.Add("--safe-mode");
            start.ArgumentList.Add("--disable-slash-commands");
        }

        foreach (var argument in Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if ((resumeSessionId ?? newSessionId) is { } sessionId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty, nameof(sessionId));
            start.ArgumentList.Add(resumeSessionId.HasValue ? "--resume" : "--session-id");
            start.ArgumentList.Add(sessionId.ToString("D"));
        }

        if (model is not null)
        {
            start.ArgumentList.Add("--model");
            start.ArgumentList.Add(model);
        }

        if (systemPromptFile is not null)
        {
            start.ArgumentList.Add("--append-system-prompt-file");
            start.ArgumentList.Add(systemPromptFile);
        }

        using var process = Process.Start(start) ?? throw new IOException("Claude Code could not start.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stdout = PipeReader.Create(process.StandardOutput.BaseStream);
        var stderr = process.StandardError.BaseStream;
        var drain = stderr.CopyToAsync(Stream.Null, stop.Token);
        var input = WritePromptAsync(process.StandardInput, prompt, stop.Token);
        var result = default(JsonElement);
        var observedSessionId = resumeSessionId ?? newSessionId;

        try
        {
            while (await ReadMessageAsync(stdout, ct).ConfigureAwait(false) is { } message)
            {
                var isResult = message.TryGetProperty("type"u8, out var type) && type.ValueEquals("result"u8);

                if (message.TryGetProperty("session_id"u8, out var session))
                {
                    if (session.ValueKind != JsonValueKind.String || !session.TryGetGuid(out var id) || id == Guid.Empty ||
                        observedSessionId is { } expected && id != expected)
                    {
                        throw new IOException("Claude Code returned an invalid or unexpected session ID.");
                    }

                    observedSessionId = id;
                }
                else if (isResult)
                {
                    throw new IOException("Claude Code returned a result without a session ID.");
                }

                if (isResult)
                {
                    result = message;
                }

                yield return message;
            }

            await process
                .WaitForExitAsync(ct)
                .ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new IOException($"Claude Code exited with code {process.ExitCode}.");
            }

            await input.ConfigureAwait(false);
            ValidateResult(result);
        }
        finally
        {
            try
            {
                await StopProcessAsync(process)
                    .ConfigureAwait(false);
            }
            finally
            {
                await stop
                    .CancelAsync()
                    .ConfigureAwait(false);
                await stderr
                    .DisposeAsync()
                    .ConfigureAwait(false);
                await stdout
                    .CompleteAsync()
                    .ConfigureAwait(false);
                await input.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await drain.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private ProcessStartInfo CreateStartInfo(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var start = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        start.Environment.Clear();

        foreach (var key in InheritedVariables)
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                start.Environment[key] = value;
            }
        }

        if (configurationDirectory is not null)
        {
            start.Environment["CLAUDE_CONFIG_DIR"] = configurationDirectory;
        }

        return start;
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // Claude Code exited between the check and the kill request.
            }
        }

        await process
            .WaitForExitAsync()
            .ConfigureAwait(false);
    }

    private static async Task WritePromptAsync(StreamWriter input, string prompt, CancellationToken ct)
    {
        await using (input.ConfigureAwait(false))
        {
            await input
                .WriteAsync(prompt.AsMemory(), ct)
                .ConfigureAwait(false);
            await input
                .FlushAsync(ct)
                .ConfigureAwait(false);
        }
    }

    private static void ValidateResult(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Undefined)
        {
            throw new EndOfStreamException("Claude Code exited before sending a result.");
        }

        if (!result.TryGetProperty("is_error"u8, out var error) || error.ValueKind != JsonValueKind.False ||
            !result.TryGetProperty("subtype"u8, out var subtype) || !subtype.ValueEquals("success"u8))
        {
            throw new IOException("Claude Code reported a failed result.");
        }
    }

    private static async ValueTask<JsonElement?> ReadMessageAsync(PipeReader reader, CancellationToken ct)
    {
        var examined = 0L;

        while (true)
        {
            var read = await reader
                .ReadAsync(ct)
                .ConfigureAwait(false);
            var buffer = read.Buffer;
            var delimiter = buffer
                .Slice(examined)
                .PositionOf((byte)'\n');

            if (delimiter is not null || read.IsCompleted)
            {
                var consumed = delimiter is { } boundary ? buffer.GetPosition(1, boundary) : buffer.End;

                try
                {
                    if (buffer.IsEmpty)
                    {
                        return null;
                    }

                    return ParseMessage(delimiter is { } end ? buffer.Slice(0, end) : buffer);
                }
                finally
                {
                    reader.AdvanceTo(consumed);
                }
            }

            examined = buffer.Length;
            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static JsonElement ParseMessage(ReadOnlySequence<byte> message)
    {
        if (message.IsSingleSegment)
        {
            return JsonElement.Parse(message.FirstSpan);
        }

        var length = checked((int)message.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            var bytes = buffer.AsSpan(0, length);
            message.CopyTo(bytes);

            return JsonElement.Parse(bytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
