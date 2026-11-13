# Full conformance reference deployment

The reference host runs the .NET UHP server with a real local Codex runtime and an in-memory memory provider. It passed all 104 checks in the official UHP 2026-10-19 Full suite. The [verification record](verification.md) links the original report and documents the build and test evidence.

| Component | Reference deployment |
|---|---|
| Server | `StaticNorth.Uhp.Server` and `StaticNorth.Uhp.Core` 0.1.0-preview.1; automatic Valiant endpoint group registration |
| Runtime | Codex CLI 0.166.0 using the [app-server stdio protocol](https://developers.openai.com/codex/app-server) and an existing local login |
| Environments | Codex sandbox builds with local Python/npm, registry access restricted to approved hosts, versioned snapshots and a read-only task mount |
| Memories | `InMemoryMemoryProvider` with per-container isolation, plus the reference UHP facade for grants, recall, history, graph and standing records |
| Storage | Process-local task, session, harness, share and memory state; filesystem staging for artifacts and environments |
| Access | Loopback host; ephemeral bearer credential; separate private Codex home and workspace per session |

The three SDK packages provide contracts, transport, and pluggable backend interfaces. The reference host builds the Codex adapter and UHP task mapping from the reference sample and uses the in-memory provider from test support. The recorded Full run predates the transport extraction. This local deployment keeps state in process. Provider-specific derivation and queries, apt provisioning, and PDF conversion are unavailable. The host declares or returns those limits through UHP. [Codex adapter](codex-provider.md)

The host invokes native Codex inference, streaming, continuation, and interruption. It binds installed MCP servers and materializes file inputs and plugins in scoped workspaces. A pending question ends the turn; the next accepted answer starts a continuation. A secret is retained outside the native conversation and exposed to the runtime only through a scoped opaque reference. The memory facade evaluates grants as the harness agent, primes standing records as untrusted data, and checks grants on every tool call.

For a mixed runtime preview, the same host can also serve a built-in Claude Code harness. The recorded Full result applies to the Codex-only deployment. [Enable the Claude Code preview](claude-code-provider.md#use-claude-code-alongside-codex)

## Reproduce

Use a Unix machine with .NET 11, its native compiler, Codex 0.166.0 authenticated locally, Python, npm if building Node environments, and `uv`. Set `CODEX_HOME` if the login lives outside the default local Codex home. The runner configures the origin from its loopback URL. A manually run host may use `--public-origin` for absolute artifact links. Keep provider credentials local.

From the repository root, restore/build, pack the three SDK projects, and prepare the suite from the [locked runner instructions](../tools/conformance/README.md). Then run:

```sh
python3 tools/package-smoke.py --reference-host
python3 tools/run-reference-conformance.py --class full --probe-runtime \
  --host-executable artifacts/aot/StaticNorth.Uhp.ConformanceHost/StaticNorth.Uhp.ConformanceHost \
  --report artifacts/conformance/reference-full.json
dotnet run --project tools/StaticNorth.Uhp.Conformance -c Release --no-build -- \
  validate . artifacts/conformance/reference-full.json
```

The runner creates a temporary loopback listener and bearer credential, shuts down its process group, and removes copied authentication state. `--probe-runtime` adds independent checks for actual secret use agent-granted memory priming and writes, and artifact citation, download, and deletion; it leaves the official suite and report untouched. Omit `--host-executable` to use a previously built managed Release host.

The [official UHP documentation](https://unifiedharnessprotocol.org/) is authoritative. [Suite provenance](../tools/conformance/manifest.json), [Codex schema hashes](../tools/conformance/codex-manifest.json), and the [source inventory](conformance-checks.json) pin what was tested. [Independent client interoperability](interoperability.md) with HarnessRouter CE and SuperQode is separate and remains unverified without configured instances.
