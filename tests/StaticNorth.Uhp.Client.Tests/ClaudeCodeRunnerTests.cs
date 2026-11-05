using System.Diagnostics;
using System.Text.Json;
using StaticNorth.Uhp.Harnesses.ClaudeCode;
using TUnit.Core.Exceptions;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ClaudeCodeRunnerTests
{
    private const string SessionId = "a71cc354-bbe8-4e8e-b3a4-cc994afab3c1";
    private const string ApiKey = "fixture-key";
    private const string Success = """{"type":"result","subtype":"success","is_error":false,"session_id":"a71cc354-bbe8-4e8e-b3a4-cc994afab3c1"}""";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StreamsOwnedEventsWithExplicitResume(bool resume)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = Directory.CreateTempSubdirectory("uhp-claude-");

        try
        {
            var expectedResumeCount = resume ? "2" : "0";
            var runner = await CreateRunnerAsync(directory.FullName, """
                set -eu
                test "$ANTHROPIC_API_KEY" = fixture-key
                test "$CLAUDE_CONFIG_DIR" -ef "$PWD"
                test "$1|$2|$3|$4|$5|$6|$7|$8|$9" = '--bare|--print|--output-format|stream-json|--verbose|--include-partial-messages|--tools||--permission-prompts'
                shift 9
                test "$1|$2|$3" = 'none|--strict-mcp-config|--mcp-config'
                test "$4" = '{"mcpServers":{}}'
                shift 4
                if test "$#" -ne 0; then
                  test "$#|$1|$2" = '2|--resume|a71cc354-bbe8-4e8e-b3a4-cc994afab3c1'
                fi
                test "$(cat)" = '--help
                café'
                printf '%s\n' 'discarded diagnostics' >&2
                printf '%s%08192d%s\n' '{"type":"stream_event","padding":"' 0 '"}'
                """ + "\ntest \"$#\" -eq " + expectedResumeCount + "\nprintf '%s\\n' '" + Success + "'", timeout.Token);
            var events = new List<JsonElement>();

            await foreach (var message in runner.RunAsync("--help\ncafé", directory.FullName, ApiKey, resume ? Guid.Parse(SessionId) : null, timeout.Token))
            {
                events.Add(message);
            }

            await Assert
                .That(events.Count)
                .IsEqualTo(2);
            var padding = events[0]
                .GetProperty("padding")
                .GetString()!;
            var id = events[1]
                .GetProperty("session_id")
                .GetString();
            await Assert
                .That(padding.Length)
                .IsEqualTo(8192);
            await Assert
                .That(id)
                .IsEqualTo(SessionId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments("exit 0", null, "Claude Code exited before sending a result.")]
    [Arguments("echo fixture-key >&2; exit 7", null, "Claude Code exited with code 7.")]
    [Arguments("printf '%s\\n' '{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"session_id\":\"a71cc354-bbe8-4e8e-b3a4-cc994afab3c1\"}'", null, "Claude Code reported a failed result.")]
    [Arguments("printf '%s\\n' '{\"type\":\"stream_event\",\"session_id\":\"a71cc354-bbe8-4e8e-b3a4-cc994afab3c1\"}'", "b502d03d-d252-43a1-bc53-b24ea1587483", "Claude Code returned an invalid or unexpected session ID.")]
    public async Task ReportsFailuresWithoutLeakingDiagnostics(string script, string? resume, string expectedMessage)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = Directory.CreateTempSubdirectory("uhp-claude-");

        try
        {
            var runner = await CreateRunnerAsync(directory.FullName, "cat >/dev/null\n" + script, timeout.Token);
            var error = await Assert
                .That(async () =>
                {
                    await foreach (var message in runner.RunAsync("prompt", directory.FullName, ApiKey, resume is null ? null : Guid.Parse(resume), timeout.Token))
                    {
                        if (resume is not null)
                        {
                            throw new InvalidOperationException("A mismatched session event reached the caller.");
                        }
                    }
                })
                .Throws<IOException>();
            await Assert
                .That(error!.Message)
                .IsEqualTo(expectedMessage);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StopsChildOnCancellationOrEarlyDisposal(bool cancel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = Directory.CreateTempSubdirectory("uhp-claude-");

        try
        {
            var runner = await CreateRunnerAsync(directory.FullName, """
                cat >/dev/null
                printf '{"type":"system","pid":%s}\n' "$$"
                exec sleep 60
                """, timeout.Token);
            var events = runner
                .RunAsync("prompt", directory.FullName, ApiKey)
                .GetAsyncEnumerator(timeout.Token);
            int pid;

            await using (events)
            {
                await Assert
                    .That(await events.MoveNextAsync())
                    .IsTrue();
                pid = events.Current
                    .GetProperty("pid")
                    .GetInt32();

                if (cancel)
                {
                    var pending = events
                        .MoveNextAsync()
                        .AsTask();
                    await timeout.CancelAsync();
                    await Assert
                        .That(() => pending)
                        .Throws<OperationCanceledException>();
                }
            }

            await Assert
                .That(() => Process.GetProcessById(pid))
                .ThrowsExactly<ArgumentException>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [NotInParallel]
    public async Task WarmedStreamingStaysWithinOwnedFrameBudget()
    {
        const int warmup = 16;
        const int iterations = 64;
        const int budget = 12 * 1024;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var directory = Directory.CreateTempSubdirectory("uhp-claude-");

        try
        {
            var runner = await CreateRunnerAsync(directory.FullName, """
                cat >/dev/null
                index=0
                while test "$index" -lt 80; do
                  printf '%s%08192d%s\n' '{"type":"stream_event","padding":"' 0 '"}'
                  index=$((index + 1))
                done
                """ + "\nprintf '%s\\n' '" + Success + "'", timeout.Token);
            var frames = 0;
            var before = 0L;
            var bytesPerFrame = 0L;

            await foreach (var message in runner.RunAsync("prompt", directory.FullName, ApiKey, ct: timeout.Token))
            {
                frames++;

                if (frames == warmup)
                {
                    before = GC.GetTotalAllocatedBytes(precise: true);
                }
                else if (frames == warmup + iterations)
                {
                    bytesPerFrame = (GC.GetTotalAllocatedBytes(precise: true) - before) / iterations;
                }
            }

            Console.WriteLine($"Claude Code allocated {bytesPerFrame} B/frame against a {budget} B ceiling.");
            await Assert
                .That(frames)
                .IsEqualTo(warmup + iterations + 1);
            await Assert
                .That(bytesPerFrame)
                .IsLessThanOrEqualTo(budget);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<ClaudeCodeRunner> CreateRunnerAsync(string directory, string script, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new SkipTestException("The deterministic Claude Code fixture uses a POSIX shell.");
        }

        var path = Path.Combine(directory, "claude-fixture.sh");
        await File.WriteAllTextAsync(path, script + "\n", ct);

        return new ClaudeCodeRunner(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-launcher.sh"), directory);
    }
}
