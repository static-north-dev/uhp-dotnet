# Preview implementation plan

The supplied specification is implemented with the subsequently agreed package split: StaticNorth.Uhp.Core, StaticNorth.Uhp.Client, and StaticNorth.Uhp.Server. Shared public types use StaticNorth.Uhp.Core; client consumers add that using alongside StaticNorth.Uhp.Client. Server endpoints use Valiant Endpoints and validation. Mapping is used only where a distinct source and destination justify it.

1. Vendor the six schema inputs, license, notice, and SHA-256 provenance at upstream commit 80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6. Generate internal preview wire models deterministically with the pinned .NET SDK; a checkout builds without upstream access.
2. Build Core models/owned extension JSON/source-generated serialization and Client discovery, prefix-safe transport, per-message auth/versioning, explicit continuation, response/error mapping, single-use SSE, inactivity timeout, recovery, and cancellation. Distinguish local failures, uncertain POST outcomes, caller cancellation, and remote task failure.
3. Build Server ASP.NET Core integration with internal Valiant endpoints and generated validation, UHP errors/headers, anonymous discovery, principal-aware backend contracts, and JSON/SSE transport. The host supplies execution, retention, idempotency and principal isolation; HTTP disconnect must never cancel remote work.
4. Build implementation before adding TUnit coverage. Verify all three protocol dates, URL/auth/version behavior, wire preservation, fragmented SSE, gaps/duplicates/unknown events, request counts, explicit cancellation, and client/server integration with a deterministic backend.
5. Pack all three packages, install the client sample from a local feed without project references, and compile/publish/run Native AOT client and server consumers against an HTTP/SSE fixture. Add CI regeneration, tests, packaging and AOT checks; credentialed interoperability remains a separate release gate.
6. Document public usage, backend responsibilities, security/retry/recovery semantics, unsupported preview features, package validation, and truthful interoperability evidence. Historical upstream reports for CE 0.25.6 and SuperQode 2.3.1 are candidate pins, not SDK results. Do not publish to NuGet until live gates pass.

Plan review incorporated immutable connection configuration, validation before submission, unknown status preservation, selected-version rediscovery validation, error-version drift preservation, atomic stream enumeration, cancellation token composition, and byte-based inactivity handling.

## StaticNorth, async, allocation, and TUnit migration

The accepted allocation target is zero avoidable allocations in repeated hot paths, with measured budgets for required HTTP objects, owned payloads, and immutable results. It does not claim that network operations allocate zero bytes.

1. Rename all eight projects, their paths, namespaces, package identities, solution, references, sample executables, scripts, CI, and docs to the `StaticNorth.Uhp` prefix. Preserve public `Uhp*` type names, protocol literals, upstream inputs, the archived original specification, and existing project properties.
2. Migrate both test projects to TUnit 1.79.0 and Microsoft.Testing.Platform, preserving every original method/data row, exact exception checks, and skipped interoperability reports. Use the generated executable runners and isolate timing-sensitive cases and allocation benchmarks.
3. Add async/AOT/source-generation/allocation/constants rules to `AGENTS.md`. Keep CPU-only operations synchronous and propagate cancellation through network, stream, and tooling file I/O.
4. Replace UTF-16 request round trips, temporary header arrays, per-request ignored-field arrays, repeated timeout sources, boxed collection enumeration, and intermediate JSON serialization documents. Use source-generated value-type request/snapshot adapters, direct JSON converters, generated Valiant validation, shared protocol/event/JSON constants, and explicitly generated SSE frame metadata.
5. Preserve immutable snapshots, modified parsed records, nested raw extensions, property omission, independent execution lifetime, authentication, cancellation, and route-group isolation. Add warmed allocation checks and required-payload budgets alongside regression coverage.
6. Verify locked restore, warning-free build, all TUnit cases, source-generation determinism/provenance, format and class structure, new package identities/symbols, and fresh-cache packed Native AOT client/server execution with reflection-based JSON disabled. Keep real-server checks unverified until configured.

The separate skeptical review required explicit generated SSE metadata, ordinary JSON byte frames, ownership/extension tests, complete test-case matrix comparison, and measured required allocations. Those changes were incorporated before implementation.
