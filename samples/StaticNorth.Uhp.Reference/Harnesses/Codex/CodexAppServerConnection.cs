using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Harnesses.Codex;

/// <summary>Owns a Codex App Server process and its stdio JSON-RPC connection.</summary>
/// <remarks>Serialize all operations. Canceling a reply wait does not cancel the native RPC. Dispose the connection after an in-flight write fails.</remarks>
public sealed class CodexAppServerConnection : IAsyncDisposable
{
    private const string IdField = "id";
    private const string MethodField = "method";
    private const string ParametersField = "params";
    private const string ResultField = "result";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] InheritedVariables = ["PATH", "HOME", "TMPDIR", "TEMP", "TMP", "SystemRoot", "LANG"];
    private static readonly JsonElement InitializeParameters = JsonElement.Parse("""
        {"clientInfo":{"name":"staticnorth_uhp","version":"0.1.0"},"capabilities":{"experimentalApi":true}}
        """);

    private readonly Process _process;
    private readonly Stream _input;
    private readonly PipeReader _output;
    private readonly StreamReader _error;
    private readonly CancellationTokenSource _stopDraining = new();
    private readonly Task _drain;
    private readonly Queue<JsonElement> _pending = [];
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private readonly Utf8JsonWriter _writer;
    private long _nextId;
    private bool _writeFailed;
    private bool _disposed;

    private CodexAppServerConnection(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = PipeReader.Create(process.StandardOutput.BaseStream);
        _error = process.StandardError;
        _writer = new Utf8JsonWriter(_buffer);
        _drain = DrainAsync(_error, _stopDraining.Token);
    }

    /// <summary>Starts Codex and completes the App Server initialization handshake.</summary>
    public static async Task<CodexAppServerConnection> StartAsync(string executablePath, string codexHome, string workingDirectory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ct.ThrowIfCancellationRequested();

        var start = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        start.ArgumentList.Add("app-server");

        var inherited = start.Environment;
        var retained = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var key in InheritedVariables)
        {
            if (inherited.TryGetValue(key, out var value))
            {
                retained.Add(key, value);
            }
        }

        inherited.Clear();

        foreach (var entry in retained)
        {
            inherited[entry.Key] = entry.Value;
        }

        inherited["CODEX_HOME"] = codexHome;
        var connection = new CodexAppServerConnection(Process.Start(start) ?? throw new IOException("Codex could not start."));

        try
        {
            await connection
                .CallAsync("initialize", InitializeParameters, ct)
                .ConfigureAwait(false);
            await connection
                .AcknowledgeInitializationAsync(ct)
                .ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Calls an RPC method and queues other messages in arrival order.</summary>
    public async Task<JsonElement> CallAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        var writer = BeginMessage(ct);
        var id = ++_nextId;
        writer.WriteNumber(IdField, id);
        writer.WriteString(MethodField, method);
        writer.WritePropertyName(ParametersField);
        parameters.WriteTo(writer);
        await WriteMessageAsync(ct)
            .ConfigureAwait(false);

        while (true)
        {
            var packet = await ReadMessageAsync(ct)
                .ConfigureAwait(false);

            if (packet.TryGetProperty(IdField, out var responseId) && responseId.ValueKind == JsonValueKind.Number && responseId.GetInt64() == id && !packet.TryGetProperty(MethodField, out _))
            {
                if (packet.TryGetProperty("error", out var error))
                {
                    throw new IOException("Codex refused RPC method: " + method + ": " + error
                        .GetProperty("message")
                        .GetString());
                }

                return packet.GetProperty(ResultField);
            }

            _pending.Enqueue(packet);
        }
    }

    /// <summary>Sends the result of a server-initiated request.</summary>
    public ValueTask RespondAsync(JsonElement id, JsonElement result, CancellationToken ct)
    {
        var writer = BeginMessage(ct);
        writer.WritePropertyName(IdField);
        id.WriteTo(writer);
        writer.WritePropertyName(ResultField);
        result.WriteTo(writer);

        return WriteMessageAsync(ct);
    }

    /// <summary>Reads the next queued or incoming server message.</summary>
    public ValueTask<JsonElement> NextAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        return _pending.TryDequeue(out var packet) ? ValueTask.FromResult(packet) : ReadMessageAsync(ct);
    }

    private ValueTask AcknowledgeInitializationAsync(CancellationToken ct)
    {
        var writer = BeginMessage(ct);
        writer.WriteString(MethodField, "initialized");
        writer.WriteStartObject(ParametersField);
        writer.WriteEndObject();

        return WriteMessageAsync(ct);
    }

    private Utf8JsonWriter BeginMessage(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        if (_writeFailed)
        {
            throw new IOException("The Codex RPC channel cannot be written to after an incomplete write.");
        }

        _buffer.ResetWrittenCount();
        _writer.Reset();
        _writer.WriteStartObject();

        return _writer;
    }

    private async ValueTask WriteMessageAsync(CancellationToken ct)
    {
        _writer.WriteEndObject();
        await _writer.FlushAsync(ct);
        _buffer.GetSpan(1)[0] = (byte)'\n';
        _buffer.Advance(1);

        try
        {
            await _input
                .WriteAsync(_buffer.WrittenMemory, ct)
                .ConfigureAwait(false);
            await _input
                .FlushAsync(ct)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            _writeFailed = true;
            throw;
        }
    }

    private async ValueTask<JsonElement> ReadMessageAsync(CancellationToken ct)
    {
        var examined = 0L;

        while (true)
        {
            var read = await _output
                .ReadAsync(ct)
                .ConfigureAwait(false);
            var buffer = read.Buffer;
            var delimiter = buffer
                .Slice(examined)
                .PositionOf((byte)'\n');

            if (delimiter is { } newline)
            {
                try
                {
                    return ParseMessage(buffer.Slice(0, newline));
                }
                finally
                {
                    _output.AdvanceTo(buffer.GetPosition(1, newline));
                }
            }

            if (read.IsCompleted)
            {
                try
                {
                    return buffer.IsEmpty
                        ? throw new EndOfStreamException("Codex closed its RPC channel.")
                        : ParseMessage(buffer);
                }
                finally
                {
                    _output.AdvanceTo(buffer.End);
                }
            }

            // Retain an incomplete frame across a canceled wait so the host can interrupt the turn.
            examined = buffer.Length;
            _output.AdvanceTo(buffer.Start, buffer.End);
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (_process.HasExited)
                {
                    // Codex exited between the check and the kill request.
                }
            }

            await _process
                .WaitForExitAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            await _stopDraining
                .CancelAsync()
                .ConfigureAwait(false);
            _error.Dispose();

            try
            {
                await _output
                    .CompleteAsync()
                    .ConfigureAwait(false);
                await _drain.ConfigureAwait(false);
            }
            finally
            {
                _pending.Clear();
                _stopDraining.Dispose();
                await _input.DisposeAsync();
                _process.Dispose();
                await _writer.DisposeAsync();
            }
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[1024];

        try
        {
            while (await reader
                .ReadAsync(buffer.AsMemory(), ct)
                .ConfigureAwait(false) != 0)
            {
                // Diagnostics may contain request material. Drain the pipe without retaining them.
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
        }
    }
}
