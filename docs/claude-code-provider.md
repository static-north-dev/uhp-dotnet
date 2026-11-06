# Claude Code provider preview

`StaticNorth.Uhp.Harnesses.ClaudeCode` registers a UHP runtime with `AddUhpClaudeCode`. It runs one Claude Code process per turn. `ClaudeCodeRunner` is also available for direct access to the native event stream. Build the preview with `dotnet pack StaticNorth.Uhp.slnx -c Release` and install it from `artifacts/packages`. [Server registration](server.md#register-harness-providers) · [Claude Code headless streaming](https://code.claude.com/docs/en/headless#stream-responses)

```csharp
using StaticNorth.Uhp.Harnesses.ClaudeCode;

var claude = new ClaudeCodeRunner(executablePath, configurationDirectory);

await foreach (var message in claude.RunAsync(prompt, workingDirectory, apiKey, resumeSessionId, ct))
{
    // Translate the native event into the host's response stream.
}
```

This slice supports text prompts, native JSONL events, and explicit UUID-based resume. Use a trusted executable and an isolated `CLAUDE_CONFIG_DIR` per principal. Authorize a resume UUID for the current principal and workspace before passing it in. Claude Code can resolve sessions across working directories in the same configuration. [Claude Code session resume](https://code.claude.com/docs/en/headless#continue-conversations)

The runner passes prompts through stdin and starts with an empty tool list and MCP configuration. `RunAsync` uses bare mode and supplies the API key only to the child environment. `RunWithSubscriptionAsync` uses the official CLI's saved sign-in with safe mode. Claude Code still applies organization-managed policy. [Claude Code CLI options](https://code.claude.com/docs/en/cli-reference)

Consume the stream to completion before marking a turn successful. The runner validates the result and exit code at EOF, and rejects mismatched session IDs before yielding events. Cancellation and early disposal terminate the local process tree. The host should treat a stopped turn as unfinished when deciding whether to resume it.

Verification used a POSIX CLI fixture and a macOS arm64 Native AOT consumer installed from the local NuGet package. The managed allocation test averaged 8,527 bytes per owned 8 KiB frame over 64 frames after warmup, against a 12,288-byte ceiling. CLI options were checked against Claude Code `2.1.316`.

## Use Claude Code alongside Codex

The reference host can use the Claude subscription already signed in on this machine. Sign in with `claude auth login --claudeai`, select your Team workspace, and run the host as the same OS user. Use the host's usual Codex login and `UHP_HOST_TOKEN`, and pass a canonical Claude model ID in `UHP_CLAUDE_MODEL`. [Claude Team sign-in](https://support.claude.com/en/articles/11845131-use-claude-code-with-your-team-or-enterprise-plan)

```sh
dotnet run --project tools/StaticNorth.Uhp.ConformanceHost -c Release -- \
  --urls http://127.0.0.1:5100 \
  --claude-auth subscription \
  --claude-executable /path/to/claude \
  --claude-model "$UHP_CLAUDE_MODEL"
```

Subscription mode binds to loopback for one local operator. The CLI uses its current profile, including `CLAUDE_CONFIG_DIR` if you signed in with one. The host checks the saved sign-in at startup and verifies the configured model against the CLI during inference. Turns use the signed-in plan's usage limits. [Claude subscription usage](https://support.claude.com/en/articles/17154008-monthly-api-credits-for-max-ultra-and-team-plans)

For API key authentication, use `--claude-auth api-key` or omit the flag and set `ANTHROPIC_API_KEY` in the host environment. In this mode, the host checks the configured model through Anthropic's Models API at startup.

Discover the two harnesses with `ListHarnessesAsync` and set `UhpTaskRequest.HarnessId` to `chrn_claude_code` to select Claude Code. Codex remains the default for new sessions. A continuation uses the harness of its previous response and requires the same caller. Claude keeps its native session and working directory per UHP session. If a turn fails or is interrupted, start a new session. Deleting a session removes its local Claude transcript and workspace.

The Claude harness is built in and serves one model. It accepts user text and task instructions. Files, environments, memories, and interactive answers are outside this preview. With Claude enabled, the host advertises the Core profile and disables file input across the host.

One UHP client exercised both runtimes through local CLI fixtures. The test covers model discovery, streaming, native resume, cancellation, session deletion, and isolation by harness and principal. A local smoke test also verified live streaming, continuation, and session deletion with a Claude Team login. The mixed deployment has no official conformance result.
