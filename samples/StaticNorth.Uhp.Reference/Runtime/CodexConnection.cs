using System.Diagnostics;
using System.Text.Json;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

internal sealed class CodexConnection : IAsyncDisposable
{
    internal static string Executable { get; } = FindExecutable();
    private readonly Process _process;
    private readonly Task _stderr;
    private readonly Queue<JsonElement> _pending = [];
    private long _nextId;

    private CodexConnection(Process process)
    {
        _process = process;
        _stderr = DrainAsync(process.StandardError);
    }

    public static async Task<CodexConnection> StartAsync(string codexHome, string cwd, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = cwd
        };
        start.ArgumentList.Add("app-server");

        var inherited = start.Environment;
        var retained = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var key in new[] { "PATH", "HOME", "TMPDIR", "TEMP", "TMP", "SystemRoot", "LANG" })
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

        start.Environment["CODEX_HOME"] = codexHome;
        var connection = new CodexConnection(Process.Start(start) ?? throw new IOException("Codex could not start."));

        try
        {
            await connection.CallAsync("initialize", UhpJson.CreateElement(writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartObject("clientInfo"); writer.WriteString("name", "staticnorth_uhp"); writer.WriteString("version", "0.1.0"); writer.WriteEndObject();
                writer.WriteStartObject("capabilities"); writer.WriteBoolean("experimentalApi", true); writer.WriteEndObject();
                writer.WriteEndObject();
            }), ct).ConfigureAwait(false);
            await connection.NotifyAsync("initialized", UhpJson.CreateElement(static writer => { writer.WriteStartObject(); writer.WriteEndObject(); }), ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<JsonElement> CallAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        var id = await SendAsync(method, parameters, ct).ConfigureAwait(false);

        while (true)
        {
            var packet = await ReadPacketAsync(ct).ConfigureAwait(false);

            if (packet.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.Number && responseId.GetInt64() == id && !packet.TryGetProperty("method", out _))
            {
                if (packet.TryGetProperty("error", out var error))
                {
                    throw new IOException("Codex refused RPC method: " + method + ": " + error.GetProperty("message").GetString());
                }

                return packet.GetProperty("result");
            }

            _pending.Enqueue(packet);
        }
    }

    public async Task<long> SendAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        var id = ++_nextId;
        await WriteAsync(UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("id", id); writer.WriteString("method", method);
            writer.WritePropertyName("params"); parameters.WriteTo(writer); writer.WriteEndObject();
        }), ct).ConfigureAwait(false);
        return id;
    }

    public Task NotifyAsync(string method, JsonElement parameters, CancellationToken ct) => WriteAsync(UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("method", method); writer.WritePropertyName("params"); parameters.WriteTo(writer); writer.WriteEndObject();
    }), ct);

    public Task RespondAsync(JsonElement id, JsonElement result, CancellationToken ct) => WriteAsync(UhpJson.CreateElement(writer =>
    {
        writer.WriteStartObject(); writer.WritePropertyName("id"); id.WriteTo(writer); writer.WritePropertyName("result"); result.WriteTo(writer); writer.WriteEndObject();
    }), ct);

    public Task<JsonElement> NextAsync(CancellationToken ct) => _pending.TryDequeue(out var packet) ? Task.FromResult(packet) : ReadPacketAsync(ct);

    private async Task<JsonElement> ReadPacketAsync(CancellationToken ct)
    {
        var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("Codex closed its RPC channel.");
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private async Task WriteAsync(JsonElement packet, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(packet.GetRawText().AsMemory(), ct).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(true);
        }

        await _process.WaitForExitAsync().ConfigureAwait(false);
        await _stderr.ConfigureAwait(false);
        _process.Dispose();
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is not null)
        {
            // The host does not retain native diagnostics, which may contain request material.
        }
    }

    private static string FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var file = new FileInfo(Path.Combine(directory, OperatingSystem.IsWindows() ? "codex.exe" : "codex"));

            if (file.Exists)
            {
                return file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
            }
        }

        throw new FileNotFoundException("Codex is not installed on PATH.");
    }
}
