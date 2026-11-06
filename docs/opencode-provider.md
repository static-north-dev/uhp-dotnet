# OpenCode provider preview

`StaticNorth.Uhp.Harnesses.OpenCode` registers a UHP runtime with `AddUhpOpenCode`. Build the preview with `dotnet pack StaticNorth.Uhp.slnx -c Release` and install it from `artifacts/packages`. The runtime and its underlying `OpenCodeConnection` use a caller-owned `HttpClient` with an OpenCode base address. Disable automatic redirects and automatic retries for POST requests. [Server registration](server.md#register-harness-providers) · [OpenCode server API](https://opencode.ai/docs/server/)

## Run it alongside Codex and Claude Code

Start a dedicated OpenCode server on the same machine as the reference host. Configure the model and its credentials in OpenCode. Use trusted OpenCode settings with `"share": "disabled"`, no plugins or MCP servers, and an isolated working directory. The host sends an absolute workspace path for each native session, so place `--state-root` outside repositories with OpenCode project settings. [OpenCode configuration](https://opencode.ai/docs/config/) · [Provider setup](https://opencode.ai/docs/providers/)

```sh
opencode serve --hostname 127.0.0.1 --port 4096
```

Start the UHP reference host with its usual Codex login and `UHP_HOST_TOKEN`. Set `UHP_OPENCODE_MODEL` to a configured OpenCode `provider/model` ID and `UHP_STATE_ROOT` to a private directory. If the OpenCode server uses Basic authentication, supply the same `OPENCODE_SERVER_PASSWORD` and optional `OPENCODE_SERVER_USERNAME` to both processes. [OpenCode server authentication](https://opencode.ai/docs/server/#authentication)

```sh
dotnet run --project tools/StaticNorth.Uhp.ConformanceHost -c Release -- \
  --urls http://127.0.0.1:5100 \
  --state-root "$UHP_STATE_ROOT" \
  --opencode-url http://127.0.0.1:4096 \
  --opencode-model "$UHP_OPENCODE_MODEL"
```

Select `chrn_opencode` using `UhpTaskRequest.HarnessId`. The host checks at startup that the configured model appears under a connected provider. Each UHP session gets a private workspace and a native OpenCode session. Continue with `PreviousResponseId`, cancel through the normal UHP cancellation endpoints, or delete the session to remove its native history and workspace. A confirmed native abort leaves the session available for continuation. Start a new session if the host cannot confirm how an interrupted turn ended.

The preview accepts user text and task instructions, and creates native sessions that deny tools. OpenCode runs in a separate process and the operator controls its settings and filesystem access. Enabling OpenCode advertises the Core profile and disables file input on the reference host.

The macOS arm64 Native AOT reference host passed all 41 checks in the official UHP 2026-10-19 Core suite with OpenCode 1.15.10 and a local Ollama model (Qwen 3.8 27B). Separate tests with a local mock model verified live event delivery, cancellation, continuation after cancellation, and deletion through UHP. The repository tests also cover unrelated events, the final HTTP reply arriving before all text events, and an abort that races native submission.
