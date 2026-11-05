# UHP harness adapter roadmap

Start with an opt-in Codex NuGet package, then implement Claude Code to test the shared runtime boundary. Evaluate OpenCode next as an HTTP-based harness with support for multiple model providers. Each package represents a specific agent runtime and plugs into a UHP server. Applications connecting to an existing UHP deployment use the client SDK. [UHP harness specification](https://unifiedharnessprotocol.org/spec/2026-10-19/harnesses)

## Candidate runtimes

| Order | Harness | Integration and rationale |
| --- | --- | --- |
| 1 | **Codex** · `StaticNorth.Uhp.Harnesses.Codex` | Extract the existing Codex App Server integration over stdio. The reference deployment already exercises real turns, streaming, continuation, and interruption. [Codex App Server](https://developers.openai.com/codex/app-server) · [Reference deployment](conformance-deployment.md) |
| 2 | **Claude Code** · `StaticNorth.Uhp.Harnesses.ClaudeCode` | Prototype the documented headless CLI with streaming JSON and session resume. Check cancellation and permission callbacks before choosing whether to use the CLI directly or bridge to the Agent SDK. [Programmatic Claude Code](https://code.claude.com/docs/en/headless) · [Agent SDK](https://code.claude.com/docs/en/agent-sdk/overview) |
| 3 | **OpenCode** · `StaticNorth.Uhp.Harnesses.OpenCode` | Evaluate its headless HTTP server, SSE events, and session abort. This tests the adapter boundary with a different transport and gives a host access to OpenCode's configurable model providers. [OpenCode server API](https://opencode.ai/docs/server/) · [OpenCode model providers](https://opencode.ai/docs/providers/) |

Demand and support for unattended deployments determine the order for later candidates.

- **GitHub Copilot** has a .NET SDK with session events. Verify Native AOT compatibility and the supported server authentication and runtime setup before committing to a package. [GitHub Copilot SDK](https://github.com/github/copilot-sdk) · [Session events](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/streaming-events)
- **Google Antigravity CLI** exposes headless JSON streaming and multi-turn input. Evaluate it for Google consumer accounts, which moved from Gemini CLI to Antigravity CLI. [Antigravity headless mode](https://antigravity.google/docs/cli/headless/) · [Google's migration announcement](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/)
- **Hermes Agent** exposes HTTP runs and Responses endpoints. Check how its session, event, and cancellation behavior maps to UHP before selecting an integration. [Hermes API server](https://hermes-agent.nousresearch.com/docs/user-guide/features/api-server)

## Package boundaries

- Each adapter translates runtime configuration, sessions, and events and invokes native continuation and interruption. The deployment supplies the runtime executable or service and its credentials.
- The UHP backend owns task acceptance, idempotency, replay, durable state, and principal isolation. The host supplies scoped workspaces and authorizes access to files, secrets, and tools. [Server backend contract](server.md#backend-contract)
- The existing reference runtime interface is the starting point. It currently takes reference environment and memory types. Extract the runtime-specific code and use both Codex and Claude Code to settle the public interface before marking it stable. [Reference runtime request](../samples/StaticNorth.Uhp.Reference/Runtime/ReferenceRuntimeRequest.cs)

## Implementation and release gates

1. **Extract Codex.** Pack the adapter as a preview candidate and wire the existing conformance host through it. Keep the reference state and authentication setup in the host. Rerun the official UHP Full suite for the named Codex deployment.
2. **Prove Claude Code.** Exercise task submission, streaming, continuation, and cancellation with the same UHP client. Run both adapters in one host, select each by its harness ID, and verify that continuation stays bound to the original harness and principal.
3. **Validate each package before publishing.** Install it from a local NuGet feed in a fresh host and publish that host with Native AOT. Document tested runtime versions and platforms, authentication requirements, and supported capabilities. Exercise advertised capabilities against the real runtime and report official conformance results per deployment. [UHP conformance suite](https://unifiedharnessprotocol.org/conformance)

The recorded **104/104 Full** result covers the current Codex reference deployment. Use it as the baseline for the extraction and record separate results for each new configuration. [Verification record](verification.md)

## Versioning and release cadence

| Version | Policy |
| --- | --- |
| **NuGet package** | Use one SemVer for Core, Client, Server, and the adapters released together during preview. Test that exact package set from a local feed. Once the adapter contract is stable, an adapter can release independently against a tested SDK dependency range. [NuGet package versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning) |
| **UHP protocol** | Negotiate a published date such as `2026-10-19`. Review and pin the upstream spec, schemas, and suite for each new date. Test every supported date, preserve exact-date negotiation, and keep the previous supported revision for at least six months after the new date is published. Announce retirements and treat dropping an advertised date as a breaking SDK change. [UHP versioning rules](https://unifiedharnessprotocol.org/versioning) |
| **Harness runtime** | Record the exact runtime versions and platforms tested with each adapter release. Add a new runtime version to that list after testing the advertised capabilities against it. |

Use `v0.1.0-preview.1` for the initial SDK release tag after package verification. Increment the preview suffix for iterations on that milestone. Use a new minor preview such as `0.2.0-preview.1` for the first adapter release. Call out preview breaking changes in migration notes. Cut `1.0.0` when the public contracts are ready to support. From then on, use patch releases for compatible fixes, minor releases for compatible additions, and major releases for breaking changes. [Publishing setup](../README.md#publish-to-nugetorg) · [Semantic Versioning](https://semver.org/spec/v2.0.0.html)

Develop in small pull requests against `main`. Review protocol-date adoption separately from adapter changes so wire behavior and runtime compatibility can be verified independently. Before the first version bump, have packaging, the smoke runner, and packed samples read the shared version from `Directory.Build.props` or receive it from the release build. Tag the verified commit, publish its artifacts once, and record the supported UHP dates, runtime versions, and conformance results in the release notes.
