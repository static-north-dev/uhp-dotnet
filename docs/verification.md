# Local verification, 4 November 2026

Environment: UHP .NET SDK 10.0.401, Valiant .NET SDK 10.0.400, macOS arm64. Native consumers use runtime/compiler 10.0.14. UHP packages are built as `0.1.0-preview.1`; the fixed Valiant Endpoints package is built as `1.2.1-preview.1`. These local builds have not been published.

| Gate | Evidence |
|---|---|
| Release build | All six solution projects build without warnings or errors |
| Schema provenance | Six versioned inputs and upstream license/notice match manifest SHA-256 hashes |
| Deterministic generation | 20 checked-in wire/context files match the pinned-schema generator |
| MSTest | 68 tests pass, covering client wire/transport/SSE and real loopback client/server scenarios on all three dates |
| Endpoint style | Named method groups and standard TypedResults; shared protocol checks in an endpoint filter |
| Automatic registration | UHP invokes its Valiant-generated registrar; no manual endpoint list or registration adapter |
| Valiant regression suite | 556 tests pass, including generated mapping selection, query-enabled behavior, mapping policies and generator incrementality |
| Valiant Native AOT | Packed smoke passes with explicit-assembly DI registration and automatic mapping of standalone and grouped endpoints |
| Structure | One top-level type per source file |
| Package contents | Three NuGet packages and three symbol packages; README, repository, Apache-2.0 and upstream notices verified |
| Packed consumers | Client and fixture server restore from the local feed without project references using a fresh package cache |
| Native AOT | Client and server publish for osx-arm64 with reflection-based JSON disabled; no trimming/AOT warnings suppressed |
| Native HTTP/SSE scenario | Discovery, catalogs, run, streaming, stored readback, continuation, response cancellation and session cancellation pass |
| HarnessRouter CE | Skipped/unverified: no pinned server configuration or credential supplied |
| SuperQode | Skipped/unverified: no pinned server configuration or credential supplied |

Valiant's generator now selects a mapping path that does not reference its reflection-based query binder when query binding is not opted into. Registrar metadata also preserves constructors needed by dependency injection. The native runtime does not support `Assembly.GetCallingAssembly()`, so Valiant's parameterless and setup-only discovery overloads now report an AOT diagnostic; Native AOT hosts use the existing explicit-assembly overload. UHP directly invokes its own generated registrar to keep the deployment prefix scoped to UHP routes. The complete native scenario passed with the workaround removed and no trimming/AOT warnings suppressed.

Machine-readable local artifacts are under `artifacts/test-results`, `artifacts/aot/result.json`, and `artifacts/interop`. They are ignored build artifacts, not upstream server certification. The Linux CI workflow needs the fixed Valiant version available in its feed before restore can succeed; no remote CI run has been observed. Local bootstrap instructions are in the root README. Real-server checks remain a publishing gate.
