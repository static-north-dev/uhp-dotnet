using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Harnesses.ClaudeCode;
using StaticNorth.Uhp.Harnesses.Codex;
using StaticNorth.Uhp.Reference;
using StaticNorth.Uhp.Core.Providers;
using TUnit.Core.Exceptions;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ReferenceHarnessRuntimeTests
{
    [Test]
    public async Task RejectsTwoRegistrationsForTheSameProviderBase()
    {
        var directory = Path.GetTempPath();
        await using var services = new ServiceCollection()
            .AddUhpCodex(directory, directory)
            .AddUhpCodex(directory, directory)
            .BuildServiceProvider();

        await Assert
            .That(() => new UhpHarnessProviderRegistry(services.GetServices<IUhpHarnessProvider>()))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OneClientCanSelectBothRuntimesAndOnlyTheOwnerCanResumeClaude(bool subscription)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new SkipTestException("The reference harness fixtures use a POSIX shell.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var directory = Directory.CreateTempSubdirectory("uhp-reference-harnesses-");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "auth.json"), "{}", ct);
            var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            var claudeRoot = Path.Combine(directory.FullName, "claude");
            var executable = Path.Combine(fixtures, "reference-claude.sh");
            var profile = Path.Combine(directory.FullName, "profile");
            var projects = Path.Combine(profile, "projects", "fixture");
            var unrelated = Path.Combine(projects, "unrelated.jsonl");

            if (subscription)
            {
                Directory.CreateDirectory(projects);
                await File.WriteAllTextAsync(unrelated, "unrelated", ct);
                var runner = new ClaudeCodeRunner(executable, profile);
                await Assert
                    .That(await runner.GetSubscriptionProfileDirectoryAsync(directory.FullName, ct))
                    .IsEqualTo(profile);
            }

            await using var services = new ServiceCollection()
                .AddUhpCodex(Path.Combine(directory.FullName, "codex"), directory.FullName, Path.Combine(fixtures, "reference-codex.sh"))
                .AddUhpClaudeCode(executable, claudeRoot, subscription ? null : "fixture-key", "fixture-claude",
                    subscription ? profile : null, subscription ? profile : null)
                .BuildServiceProvider();
            var providers = new UhpHarnessProviderRegistry(services.GetServices<IUhpHarnessProvider>());
            await providers.InitializeAsync(ct);
            var claude = providers.ByBase["claude-code"];
            using var backend = new ReferenceBackend(providers, "codex");
            await using var host = await ServerTestHost.StartAsync(sessions: true, harnesses: true, memories: true, backend: backend);
            using var http = host.CreateHttp();
            var alice = await ServerTestHost.ConnectAsync(http);
            var bob = await ServerTestHost.ConnectAsync(http, "bob");
            var harnesses = await alice.ListHarnessesAsync(ct);
            var codexId = harnesses.Single(harness => harness.Base == "codex").Id;
            var claudeId = harnesses.Single(harness => harness.Base == "claude-code").Id;
            var models = await alice.GetModelsAsync(ct);
            var claudeModels = await alice.GetHarnessModelsAsync(claudeId, ct);

            await Assert
                .That(codexId)
                .IsEqualTo("chrn_fixture");
            await Assert
                .That(claudeId)
                .IsEqualTo("chrn_claude_code");
            await AssertErrorAsync(() => alice.RunAsync(new("missing provider") { HarnessId = "chrn_opencode" }, ct),
                HttpStatusCode.NotFound, "harness_not_found");
            await Assert
                .That(models.Backends["codex"].Default)
                .IsEqualTo("fixture-codex");
            await Assert
                .That(models.Backends["claude-code"].Default)
                .IsEqualTo("fixture-claude");
            await Assert
                .That(claudeModels.Models.Single().Available)
                .IsTrue();
            await Assert
                .That(claudeModels.Default)
                .IsEqualTo("fixture-claude");

            var codexResponse = await alice.RunAsync(new("codex task"), ct);
            await Assert
                .That(codexResponse.Status)
                .IsEqualTo(UhpResponseStatus.Completed);
            await Assert
                .That(codexResponse.Output[0].Text)
                .IsEqualTo("Codex response");

            await AssertErrorAsync(() => alice.RunAsync(new("start") { HarnessId = claudeId, Model = "missing" }, ct),
                HttpStatusCode.UnprocessableEntity, "model_unavailable");

            var first = await StreamClaudeAsync(alice, claudeId, claude.WorkspaceRoot, ct);
            var sessionId = first.SessionId!;
            var config = Path.Combine(claudeRoot, "sessions", sessionId);
            var workspace = Path.Combine(claude.WorkspaceRoot, sessionId);
            string? transcript = null;

            if (subscription)
            {
                var nativeId = await File.ReadAllTextAsync(Path.Combine(workspace, "native-id"), ct);
                transcript = Path.Combine(projects, $"{Guid.Parse(nativeId):D}.jsonl");
                await Assert
                    .That(File.Exists(transcript))
                    .IsTrue();
            }

            await Assert
                .That(File.Exists(Path.Combine(config, "task-instructions.txt")))
                .IsFalse();
            var follow = new UhpTaskRequest([new UhpUserTextMessage("follow")]) { PreviousResponseId = first.Id };
            await AssertErrorAsync(() => alice.RunAsync(follow with { HarnessId = codexId }, ct), HttpStatusCode.Conflict, "harness_mismatch");
            await AssertErrorAsync(() => bob.RunAsync(follow, ct), HttpStatusCode.NotFound, "response_not_found");

            var resumed = await alice.RunAsync(follow, ct);
            await Assert
                .That(resumed.Status)
                .IsEqualTo(UhpResponseStatus.Completed);
            await Assert
                .That(resumed.SessionId)
                .IsEqualTo(sessionId);
            await Assert
                .That(resumed.Output[0].Text)
                .IsEqualTo("Claude continuation");
            await Assert
                .That((await alice.GetSessionAsync(sessionId, ct: ct)).HarnessId)
                .IsEqualTo(claudeId);

            await CancelClaudeAsync(alice, resumed.Id, ct);
            await AssertErrorAsync(() => alice.RunAsync(follow, ct), HttpStatusCode.Conflict, "staticnorth_session_runtime_unavailable");

            await alice.DeleteSessionAsync(sessionId, ct: ct);
            await Assert
                .That(Directory.Exists(config))
                .IsFalse();
            await Assert
                .That(Directory.Exists(workspace))
                .IsFalse();

            if (transcript is not null)
            {
                await Assert
                    .That(File.Exists(transcript))
                    .IsFalse();
                await Assert
                    .That(await File.ReadAllTextAsync(unrelated, ct))
                    .IsEqualTo("unrelated");
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<UhpResponse> StreamClaudeAsync(UhpClient client, string harnessId, string workspaceRoot, CancellationToken ct)
    {
        var request = new UhpTaskRequest("start") { HarnessId = harnessId, Instructions = "fixture instructions", TimeoutSeconds = 10 };
        var deltas = new List<string>();
        string? sessionId = null;

        await foreach (var update in client.StreamAsync(request, ct))
        {
            if (update is UhpResponseCreated created)
            {
                sessionId = created.Response.SessionId;
            }
            else if (update is UhpTextDelta delta)
            {
                deltas.Add(delta.Text);

                if (deltas.Count == 1)
                {
                    await Assert
                        .That(delta.Text)
                        .IsEqualTo("Claude ");
                    await File.WriteAllTextAsync(Path.Combine(workspaceRoot, sessionId!, "release"), "", ct);
                }
            }
            else if (update is UhpResponseCompleted completed)
            {
                await Assert
                    .That(deltas.Count)
                    .IsEqualTo(2);
                await Assert
                    .That(deltas[1])
                    .IsEqualTo("response");
                await Assert
                    .That(completed.Response.Output[0].Text)
                    .IsEqualTo("Claude response");
                return completed.Response;
            }
        }

        throw new InvalidOperationException("Claude did not send a completed response.");
    }

    private static async Task CancelClaudeAsync(UhpClient client, string previousResponseId, CancellationToken ct)
    {
        string? responseId = null;

        await foreach (var update in client.StreamAsync(new("hold") { PreviousResponseId = previousResponseId, TimeoutSeconds = 10 }, ct))
        {
            if (update is UhpResponseCreated created)
            {
                responseId = created.Response.Id;
            }
            else if (update is UhpTextDelta delta)
            {
                await Assert
                    .That(delta.Text)
                    .IsEqualTo("waiting");
                await client.CancelResponseAsync(responseId!, ct);
            }
            else if (update is UhpResponseFailed failed)
            {
                await Assert
                    .That(failed.Response.Status)
                    .IsEqualTo(UhpResponseStatus.Cancelled);
                return;
            }
        }

        throw new InvalidOperationException("Claude did not send a cancelled response.");
    }

    private static async Task AssertErrorAsync(Func<Task> action, HttpStatusCode status, string code)
    {
        var error = (await Assert
            .That(action)
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(error.StatusCode)
            .IsEqualTo(status);
        await Assert
            .That(error.Code)
            .IsEqualTo(code);
    }
}
