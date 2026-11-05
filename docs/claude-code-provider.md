# Claude Code provider preview

`StaticNorth.Uhp.Harnesses.ClaudeCode` runs one Claude Code process per turn using [headless streaming](https://code.claude.com/docs/en/headless#stream-responses). Build the preview with `dotnet pack src/StaticNorth.Uhp.Harnesses.ClaudeCode -c Release` and install it from the local feed at `artifacts/packages`.

```csharp
using StaticNorth.Uhp.Harnesses.ClaudeCode;

var claude = new ClaudeCodeRunner(executablePath, configurationDirectory);

await foreach (var message in claude.RunAsync(prompt, workingDirectory, apiKey, resumeSessionId, ct))
{
    // Translate the native event into the host's response stream.
}
```

This slice supports text prompts, native JSONL events, and explicit UUID-based resume. Use a trusted executable and an isolated `CLAUDE_CONFIG_DIR` per principal. Authorize a resume UUID for the current principal and workspace before passing it in. Claude Code can resolve sessions across working directories in the same configuration. [Claude Code session resume](https://code.claude.com/docs/en/headless#continue-conversations)

The runner uses bare mode with an empty tool list and MCP configuration. It passes the prompt through stdin and supplies the API key only to the child environment. Bare mode uses Anthropic API authentication. The host owns credentials, filesystem isolation, and session storage. [Bare mode](https://code.claude.com/docs/en/headless#start-faster-with-bare-mode)

Consume the stream to completion before marking a turn successful. The runner validates the result and exit code at EOF, and rejects mismatched session IDs before yielding events. Cancellation and early disposal terminate the local process tree. The host should treat a stopped turn as unfinished when deciding whether to resume it.

Verification used a POSIX CLI fixture and a macOS arm64 Native AOT consumer installed from the local NuGet package. The managed allocation test averaged 8,527 bytes per owned 8 KiB frame over 64 frames after warmup, against a 12,288-byte ceiling. CLI options were checked against Claude Code `2.1.316`. Authenticated turns and wiring into a UHP host are the next integration milestones.
