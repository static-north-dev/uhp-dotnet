using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Harnesses.ClaudeCode;

/// <summary>Runs Claude Code in headless mode for a host-owned configuration directory.</summary>
public sealed class ClaudeCodeRunner(string executablePath, string configurationDirectory)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] InheritedVariables = ["PATH", "HOME", "TMPDIR", "TEMP", "TMP", "SystemRoot", "LANG"];
    private static readonly string[] Arguments =
    [
        "--bare", "--print", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
        "--tools", "", "--permission-prompts", "none", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}"
    ];

    /// <summary>Streams the native events of one turn and validates completion when the stream ends.</summary>
    /// <remarks>Authorize any resume ID for this workspace and configuration before passing it here.</remarks>
    public async IAsyncEnumerable<JsonElement> RunAsync(
        string prompt, string workingDirectory, string apiKey, Guid? resumeSessionId = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ct.ThrowIfCancellationRequested();

        var start = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (resumeSessionId is { } sessionId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty, nameof(resumeSessionId));
            start.ArgumentList.Add("--resume");
            start.ArgumentList.Add(sessionId.ToString("D"));
        }

        start.Environment.Clear();

        foreach (var key in InheritedVariables)
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                start.Environment[key] = value;
            }
        }

        start.Environment["CLAUDE_CONFIG_DIR"] = configurationDirectory;
        start.Environment["ANTHROPIC_API_KEY"] = apiKey;

        using var process = Process.Start(start) ?? throw new IOException("Claude Code could not start.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stdout = PipeReader.Create(process.StandardOutput.BaseStream);
        var stderr = process.StandardError.BaseStream;
        var drain = stderr.CopyToAsync(Stream.Null, stop.Token);
        var input = WritePromptAsync(process.StandardInput, prompt, stop.Token);
        var result = default(JsonElement);
        var observedSessionId = resumeSessionId;

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
