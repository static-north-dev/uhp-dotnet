# Codex provider preview

`StaticNorth.Uhp.Harnesses.Codex` registers a UHP runtime with `AddUhpCodex`. It uses Codex App Server over stdio for model discovery, turns, continuation, and interruption. Hosts that need raw App Server calls can still use `CodexAppServerConnection` directly. [Server registration](server.md#register-harness-providers) · [Codex App Server](https://developers.openai.com/codex/app-server)

Build the preview with `dotnet pack StaticNorth.Uhp.slnx --configuration Release`. Install `StaticNorth.Uhp.Harnesses.Codex` from `artifacts/packages` in a .NET 10 consumer. The deployment supplies a trusted Codex executable, an isolated `CODEX_HOME`, and a working directory. Model-backed turns require credentials configured in that Codex home. The reference deployment pins Codex CLI `0.166.0` and the connection enables the experimental App Server API it uses.

```csharp
using System.Text.Json;
using StaticNorth.Uhp.Harnesses.Codex;

await using var codex = await CodexAppServerConnection
    .StartAsync(executablePath, codexHome, workingDirectory, ct);
var catalog = await codex
    .CallAsync("model/list", JsonElement.Parse("{}"), ct);
```

Use one sequential caller per connection. `CallAsync` queues other messages in arrival order for `NextAsync`. Respond to a server-initiated request with `RespondAsync`. Canceling a read keeps a partial frame available for the next operation, so the host can issue `turn/interrupt`. Canceling an RPC wait can leave the RPC running. Dispose the connection after an incomplete write. The connection owns the child process and pipes, and the host owns the directories it passed in.

The provider uses buffered UTF-8 frames and owned JSON results. On .NET 10.0.401 on macOS arm64, the warmed regression test averaged 753 allocated bytes per round trip. It sends an 8 KiB input for 64 calls after 16 warmup calls. The 4,096 byte ceiling includes the small parsed response and async I/O.

A macOS arm64 Native AOT consumer installed the local NuGet package with reflection-based JSON disabled and listed nine models from Codex CLI `0.166.0`.
