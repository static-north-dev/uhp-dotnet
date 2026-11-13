# Verification

## .NET 10 and .NET 11 package compatibility

SDK `11.0.100` builds Core, Client, and Server as `net10.0`. On macOS arm64, the same three packages restored from a local feed and passed the HTTP/SSE lifecycle in standalone managed consumers on .NET 10 and .NET 11. Fresh-cache Native AOT consumers passed the same scenario with runtime/compiler `10.0.15` and `11.0.0`. CI runs the packed Native AOT scenario for both targets.

## .NET 11 upgrade

With SDK `11.0.100` and runtime/compiler `11.0.0`, locked restore, the Release build, formatting, schema generation, and pinned suite checks passed. All 199 local TUnit tests passed on managed macOS arm64 and as Native AOT executables on macOS arm64 and in an emulated Linux x64 SDK container, including the [allocation checks](performance.md). The packages in that run contained `net11.0` assemblies. A fresh package cache restored the client, fixture server, and conformance host for Native AOT on macOS arm64, and the packed client and fixture server passed the HTTP/SSE lifecycle.

The macOS native toolchain emitted debug-symbol warnings during publish. The two external interoperability cases skipped because their pinned server configurations were absent. The Full conformance evidence below is from the .NET 10 reference deployment.

## UHP 2026-10-19 Full reference host

The unmodified official suite `2026.10.19.post5` at upstream commit `80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6` passed **104/104** on the packed osx-arm64 Native AOT Codex reference host, with **0 failed, 0 skipped, 0 errors**, and `highest_class_passed: full`. The strict validator independently verified complete coverage. See the [original JSON report](../artifacts/conformance/reference-full.json), [build and suite provenance](../artifacts/conformance/reference-full.provenance.json), and [earlier managed run](../artifacts/conformance/reference-managed-full.json).

The host used real Codex CLI 0.166.0 and the process-local in-memory provider. Independent probes also confirmed that a secret can be consumed without appearing in the returned task or readback, and that the harness agent reads standing records and writes to its granted default memory. They also verified that an artifact citation downloads the exact bytes and returns 404 after session deletion. [Reproduction and deployment details](conformance-deployment.md).

All **179 TUnit tests** passed on managed .NET and as an osx-arm64 Native AOT executable, with .NET SDK 10.0.401 and native runtime/compiler 10.0.14. The packed client and fixture server passed the HTTP/SSE lifecycle smoke test. Locked restore, the warning-free Release build, formatting, generated code verification, and pinned suite hash validation passed. The [allocation report](performance.md) records the warmed measurements and payload budgets.

HarnessRouter CE and SuperQode client interoperability remain unverified because no pinned targets are configured. The Full result applies to the reference deployment, not to arbitrary SDK backends or the upstream implementations. Packages have not been published.

The following sections preserve historical evidence from 4 November 2026; their scope and counts predate the Full host.

## In-memory reference provider update

The user selected process-local test/reference storage rather than a production database adapter. `StaticNorth.Uhp.TestSupport` is non-packable, BCL-only and not referenced by the published projects. It implements owner-scoped memory records, owned content/attributes/references, source-generated text normalization, append-only revision/history, forgetting, erasure and memory deletion. It does not enable the unfinished HTTP `memories` capability.

All **144** local TUnit cases pass managed and as an osx-arm64 Native AOT executable using runtime/compiler `10.0.14`, with no warning suppression. The 30 new cases cover owner/container isolation, input ownership, malformed-write atomicity, history snapshots, deletion/forget/erase distinctions, cancellation, instance independence, concurrent revisions, file references and allocation budgets. Warmed reads measure **0 B/read**; retained small text writes measure **748 B/write** in both managed and native runs against a 2,048 B ceiling.

Locked restore and formatting checks pass. Independent implementation review identified validation and temporary-ID-string issues; both were fixed and re-reviewed. This verifies the reference store, not the unimplemented memory facade, graph/recall/priming semantics or official Full conformance. Details are in [test support](../tests/StaticNorth.Uhp.TestSupport/README.md).

## Full-conformance groundwork update

The new conformance utility and required-null wire corrections build without warnings or errors. All **114** local TUnit cases pass managed and as an osx-arm64 Native AOT executable, including the existing warmed allocation budgets. No trimming/AOT warnings were suppressed. Locked solution restore succeeds. Formatting succeeds with the MSBuild host permitted to open its local pipe.

All three packages and symbol packages were rebuilt. Fresh-cache packed client and fixture-server consumers publish with Native AOT/runtime `10.0.14` and pass the HTTP/SSE lifecycle (catalogs, execution, streaming, readback, continuation and both cancellation scopes). The offline cache lacked the app-host package, so the successful smoke run restored it from NuGet.org rather than changing the runtime pin.

The unmodified upstream suite at `80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6` matches 16 pinned source/schema/license/lock hashes and all 104 reviewed check IDs/classes. A separately locked Python 3.13 environment installs suite `2026.10.19.post5` plus its MCP fixture; the official CLI help runs. Upstream's lock omits the fixture extra, so its files remain unchanged and `tools/conformance/uv.lock` pins the complete environment instead.

Fourteen new report-gate cases exercise complete input and rejection of misleading green reports; two new wire cases require explicit null response/error fields. These tests do not establish official protocol conformance. No live official suite run, Linux AOT run, Full host or connected memory provider has been verified. Codex is the selected harness runtime; app-server initialization/catalog discovery and a real ephemeral read-only inference turn with a text delta passed. See [deployment decisions](conformance-deployment.md) for the remaining probes.

The earlier preview verification below is retained as historical evidence; its 98-case counts and scope precede this update.

## Earlier preview baseline

Environment: UHP .NET SDK 10.0.401, macOS arm64. Native consumers use runtime/compiler 10.0.14. UHP packages are built as `0.1.0-preview.1` and have not been published. Valiant Endpoints and Validation use the published NuGet.org version `1.2.1`, whose repository metadata points to commit `622b47012f5e833267a31a8f54e3dcca9454f25c`, including the AOT fix. The Valiant regression suite was verified with its pinned .NET SDK 10.0.400.

| Gate | Evidence |
|---|---|
| Release build | All six solution projects build without warnings or errors |
| Schema provenance | Six versioned inputs and upstream license/notice match manifest SHA-256 hashes |
| Deterministic generation | 20 checked-in wire/context files match the pinned-schema generator |
| TUnit | 98 tests pass with TUnit 1.79.0 and its generated executable runner; all 70 original local cases and two external case definitions are retained |
| Native TUnit | The same 98 tests publish and pass as an osx-arm64 Native AOT executable without trimming/AOT warning suppressions |
| Allocations | Eight protocol/event writer types measure 0 B/write after warmup; required request/response/HTTP/SSE allocations have explicit tested budgets in `docs/performance.md` |
| Names and async | Eight projects/packages/namespaces use `StaticNorth.Uhp.*`; runtime and generator I/O use async APIs and generated JSON metadata |
| Endpoint style | Named method groups and standard TypedResults; shared protocol checks in an endpoint filter |
| Automatic registration | Every UHP endpoint belongs to `UhpEndpointGroup`; Valiant's generated registrar applies group policies/conventions and maps the endpoints without a manual list or adapter |
| Group integration | Regression cases verify group exclusion and group-level host restrictions while unrelated routes remain independent |
| Valiant regression suite | 556 tests pass, including generated mapping selection, query-enabled behavior, mapping policies and generator incrementality |
| Valiant Native AOT | Packed smoke passes with explicit-assembly DI registration and automatic mapping of standalone and grouped endpoints |
| Structure | One top-level type per source file |
| Package contents | Three NuGet packages and three symbol packages; README, repository, Apache-2.0 and upstream notices verified |
| Packed consumers | UHP client and fixture server restore from the local feed without project references using a fresh package cache; both Valiant 1.2.1 dependencies resolve from NuGet.org |
| Native AOT | Client and server publish for osx-arm64 with reflection-based JSON disabled; no trimming/AOT warnings suppressed |
| Native HTTP/SSE scenario | Discovery, catalogs, run, streaming, stored readback, continuation, response cancellation and session cancellation pass |
| Terminal publication | Concurrent and late fixture subscribers receive the complete contiguous history through the terminal event for completion and cancellation |
| HarnessRouter CE | Skipped/unverified: no pinned server configuration or credential supplied |
| SuperQode | Skipped/unverified: no pinned server configuration or credential supplied |

Valiant's generator now selects a mapping path that does not reference its reflection-based query binder when query binding is not opted into. Registrar metadata also preserves constructors needed by dependency injection. The native runtime does not support `Assembly.GetCallingAssembly()`, so Valiant's parameterless and setup-only discovery overloads now report an AOT diagnostic; Native AOT hosts use the existing explicit-assembly overload. UHP directly invokes its own generated registrar to keep the deployment prefix scoped to UHP routes. The complete native scenario passed with the workaround removed and no trimming/AOT warnings suppressed.

Machine-readable local artifacts are under `artifacts/test-results`, `artifacts/aot/result.json`, and `artifacts/interop`. They are ignored build artifacts, not upstream server certification. The solution restores from NuGet.org without a local Valiant feed. The Linux CI workflow uses the same published dependency versions; no remote CI run has been observed. Real-server checks remain a publishing gate.
