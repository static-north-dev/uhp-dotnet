using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using StaticNorth.Uhp.Harnesses.Codex;
using TUnit.Core.Exceptions;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class CodexAppServerConnectionTests
{
    private const string Handshake = """
        set -eu
        test "$#" -eq 1
        test "$1" = app-server
        test -d "$CODEX_HOME"
        IFS= read -r line
        case "$line" in
          '{"id":1,"method":"initialize","params":{'*) ;;
          *) exit 1 ;;
        esac
        printf '%s\n' '{"id":1,"result":{}}'
        IFS= read -r line
        test "$line" = '{"method":"initialized","params":{}}'
        """;

    [Test]
    public async Task QueuesInterleavedMessagesAndAnswersServerRequests()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = Directory.CreateTempSubdirectory("uhp-codex-");

        try
        {
            var executable = await WriteServerAsync(directory.FullName, Handshake + "\n" + """
                printf '%s\n' 'test diagnostic' >&2
                IFS= read -r line
                test "$line" = '{"id":2,"method":"model/list","params":{}}'
                printf '%s\n' '{"method":"notice","params":{}}' '{"id":"tool","method":"tool/call","params":{}}'
                printf '%s%08192d%s\n' '{"id":2,"result":{"model":"café","padding":"' 0 '"}}'
                IFS= read -r line
                test "$line" = '{"id":"tool","result":{"success":true}}'
                IFS= read -r line
                test "$line" = '{"id":3,"method":"unknown","params":{}}'
                printf '%s\n' '{"id":3,"error":{"message":"Unknown method"}}'
                """, timeout.Token);
            await using var connection = await CodexAppServerConnection.StartAsync(executable, directory.FullName, directory.FullName, timeout.Token);
            var empty = JsonElement.Parse("{}");
            var result = await connection.CallAsync("model/list", empty, timeout.Token);
            var model = result
                .GetProperty("model")
                .GetString();
            await Assert
                .That(model)
                .IsEqualTo("café");
            var padding = result
                .GetProperty("padding")
                .GetString()!;
            await Assert
                .That(padding.Length)
                .IsEqualTo(8192);

            var notification = await connection.NextAsync(timeout.Token);
            var notificationMethod = notification.GetProperty("method");
            await Assert
                .That(notificationMethod.ValueEquals("notice"))
                .IsTrue();

            var request = await connection.NextAsync(timeout.Token);
            var requestMethod = request.GetProperty("method");
            await Assert
                .That(requestMethod.ValueEquals("tool/call"))
                .IsTrue();
            await connection.RespondAsync(request.GetProperty("id"), JsonElement.Parse("""{"success":true}"""), timeout.Token);

            var error = (await Assert
                .That(() => connection.CallAsync("unknown", empty, timeout.Token))
                .ThrowsExactly<IOException>())!;
            await Assert
                .That(error.Message)
                .IsEqualTo("Codex refused RPC method: unknown: Unknown method");
            await Assert
                .That(async () => await connection.NextAsync(timeout.Token))
                .ThrowsExactly<EndOfStreamException>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task CancelingEventWaitPreservesTheFrameForNativeInterruption()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = Directory.CreateTempSubdirectory("uhp-codex-");

        try
        {
            var executable = await WriteServerAsync(directory.FullName, Handshake + "\n" + """
                printf '%s' '{"method":"turn/completed","params":'
                : > "$CODEX_HOME/partial"
                IFS= read -r line
                test "$line" = '{"id":2,"method":"turn/interrupt","params":{"turnId":"turn"}}'
                printf '%s\n' '{"status":"interrupted"}}' '{"id":2,"result":{}}'
                """, timeout.Token);
            await using var connection = await CodexAppServerConnection.StartAsync(executable, directory.FullName, directory.FullName, timeout.Token);
            await WaitForFileAsync(Path.Combine(directory.FullName, "partial"), timeout.Token);
            using var cancellation = new CancellationTokenSource();
            var waiting = connection
                .NextAsync(cancellation.Token)
                .AsTask();
            await cancellation.CancelAsync();
            await Assert
                .That(() => waiting)
                .Throws<OperationCanceledException>();

            await connection.CallAsync("turn/interrupt", JsonElement.Parse("""{"turnId":"turn"}"""), timeout.Token);
            var completed = await connection.NextAsync(timeout.Token);
            var method = completed.GetProperty("method");
            var status = completed
                .GetProperty("params")
                .GetProperty("status")
                .GetString();
            await Assert
                .That(method.ValueEquals("turn/completed"))
                .IsTrue();
            await Assert
                .That(status)
                .IsEqualTo("interrupted");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task CancelingInitializationStopsTheChildProcess()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var directory = Directory.CreateTempSubdirectory("uhp-codex-");

        try
        {
            var executable = await WriteServerAsync(directory.FullName, """
                IFS= read -r line
                printf '%s\n' "$$" > "$CODEX_HOME/pid.tmp"
                mv "$CODEX_HOME/pid.tmp" "$CODEX_HOME/pid"
                IFS= read -r line
                """, timeout.Token);
            var startup = CodexAppServerConnection.StartAsync(executable, directory.FullName, directory.FullName, cancellation.Token);
            var pidFile = Path.Combine(directory.FullName, "pid");
            await WaitForFileAsync(pidFile, timeout.Token);
            var pid = int.Parse(await File.ReadAllTextAsync(pidFile, timeout.Token), CultureInfo.InvariantCulture);
            using var process = Process.GetProcessById(pid);
            await cancellation.CancelAsync();
            await Assert
                .That(async () =>
                {
                    await startup.WaitAsync(timeout.Token);
                })
                .Throws<OperationCanceledException>();
            await Assert
                .That(startup.IsCanceled)
                .IsTrue();
            await Assert
                .That(process.HasExited)
                .IsTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [NotInParallel]
    public async Task WarmedWritesDoNotAllocateAnotherCopyOfThePayload()
    {
        const int iterations = 64;
        const int bytesPerCallBudget = 4096;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var directory = Directory.CreateTempSubdirectory("uhp-codex-");

        try
        {
            var executable = await WriteServerAsync(directory.FullName, Handshake + "\n" + """
                id=1
                while IFS= read -r line; do
                  id=$((id + 1))
                  printf '{"id":%s,"result":{}}\n' "$id"
                done
                """, timeout.Token);
            await using var connection = await CodexAppServerConnection.StartAsync(executable, directory.FullName, directory.FullName, timeout.Token);
            var parameters = JsonElement.Parse("{\"text\":\"" + new string('x', 8192) + "\"}");

            for (var index = 0; index < 16; index++)
            {
                await connection.CallAsync("turn/start", parameters, timeout.Token);
            }

            var before = GC.GetTotalAllocatedBytes(precise: true);

            for (var index = 0; index < iterations; index++)
            {
                await connection.CallAsync("turn/start", parameters, timeout.Token);
            }

            var bytesPerCall = (GC.GetTotalAllocatedBytes(precise: true) - before) / iterations;
            Console.WriteLine($"Codex App Server allocated {bytesPerCall} B/round trip after warmup against a {bytesPerCallBudget} B ceiling.");
            await Assert
                .That(bytesPerCall)
                .IsLessThanOrEqualTo(bytesPerCallBudget);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task WaitForFileAsync(string path, CancellationToken ct)
    {
        while (!File.Exists(path))
        {
            await Task.Delay(10, ct);
        }
    }

    private static async Task<string> WriteServerAsync(string directory, string script, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new SkipTestException("The deterministic Codex fixture uses a POSIX shell.");
        }

        var executable = Path.Combine(directory, "codex");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\n" + script + "\n", ct);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return executable;
    }
}
