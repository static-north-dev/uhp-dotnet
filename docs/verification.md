# Local verification, 4 November 2026

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
