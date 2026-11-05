# Full UHP conformance review and implementation plan

Reviewed on 4 November 2026 against repository commit `40cf92016fee81d5ecacc982d1f8ee084b7c0d4d`. This is the original review and implementation plan. See the [verification record](verification.md) for the completed Full run and [deployment notes](conformance-deployment.md) for the actual host.

The agreed target is a clean official **Full** report, including the optional capabilities needed to avoid skips. This supersedes the text-only scope in the [preview plan](implementation-plan.md), not the immutable [original request](original-implementation-specification.txt).

Implementation now covers the typed SDK surface and the real Codex reference host with in-memory memory storage. The pinned official suite passed 104/104 on both the managed host and the packed Native AOT host. The findings and milestones below describe the baseline and agreed scope; the [current evidence](verification.md) records verification and remaining external release checks.

## Target and evidence

- Protocol: `2026-10-19`.
- Upstream specification, schemas and suite: HarnessRouter/harnessrouter commit `80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6`, which matches the existing schema manifest and the upstream HEAD inspected during this review.
- Suite package version: `2026.10.19.post5`.
- Pinned registry: **104 unique checks**: 41 Core, 9 Extended and 54 additional Full checks. The website's conformance table still says 101; the executable registry is the coverage inventory. [Pinned checks](https://github.com/HarnessRouter/harnessrouter/blob/80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6/protocol/conformance/uhp_conformance/checks.py).
- Machine-readable review: [conformance-checks.json](conformance-checks.json). The inventory is a source audit and retains `not_run` placeholders. Actual results are in the original suite report linked in [verification](verification.md).
- The pinned OpenAPI has 61 paths and 83 operations; the baseline server mapped nine concrete operations plus its fallback. This is a scope inventory, not a claim that every schema operation is mandatory for every deployment. Normative prose also names operations absent from OpenAPI, notably session deletion.

The acceptance deployment must use the packed .NET server, real harness execution and a connected memory provider. Running the suite against CE or SuperQode alone proves properties of those servers, not this SDK. Their separate purpose is independent client interoperability. [Official server guidance](https://unifiedharnessprotocol.org/serving).

The strict report must have `requested_class: full`, `protocol_version: 2026-10-19`, `conformant: true`, `highest_class_passed: full`, complete coverage, no missing/duplicate checks, and zero failures, errors or skips. Check the JSON, not just process exit status. Archive the unmodified report with suite SHA/version, SDK/package commit, host/backend/runtime/provider versions and capability configuration. [Official report semantics](https://unifiedharnessprotocol.org/conformance).

## Review findings

| Priority | Finding and evidence | Required change |
|---|---|---|
| P1 | Discovery hardcodes `core`, while background requests are explicitly rejected. See `DiscoveryEndpoint.cs:23`, `UhpJson.cs:186` and `UhpHttp.cs:89`. T-11 and the setup of C-03 require background acceptance. | Carry background execution through Core, Client and Server; return the accepted snapshot immediately and retain execution independently. Derive advertised class/capabilities from configured support and verify each deployed class. |
| P1 | `UhpJsonWriter.cs:36,46,52,63` omits null response fields, including unknown usage. T-05 explicitly requires usage with an object or null; the response table also requires other nullable fields and retention metadata. | Emit required wire fields explicitly, without invented values. Add a field-presence/null matrix independent of our parser. |
| P1 | Error serialization omits null `param` and `detail` at `UhpJsonWriter.cs:79`, although the errors chapter requires both. The pinned schema and E-01 are weaker. | Follow normative prose as well as schema validation; audit all required/null/omitted fields and version differences. |
| P1 | `UhpHttp.cs:49` collects only `tools` and `include`; other unknown top-level request fields disappear without ignored-field reporting. | Record every supplied field that was not acted on and preserve this information in stored/streamed responses. Never report a field that was absent. |
| P1 | All Extended families and most Full families have no client methods, server endpoints or backend contracts. `UhpTaskRequest` accepts only user text; `UhpHarness` has no typed configuration collections. | Implement both client and server surfaces, shared public models and actual backend behavior per family. Raw JSON retention alone is not feature support. |
| P1 | The generator selects 15 roots and deliberately leaves MCP servers, skills and plugins as `JsonElement` (`SchemaGenerator.cs:11,156`). The schema contains 92 definitions. | Expand deterministic generation and typed adapters for the planned families, including union, validation and ownership rules. |
| P1 | Existing CI runs local checks and a separate two-server client scenario, but never invokes the official server suite. The fixture produces canned events. | Add official-suite gates against the .NET conformance deployment, while keeping deterministic fixture tests distinct. |
| P2 | Capability defaults omit `harness_idempotency` and `harness_retirement`; options reject all newly needed true capabilities (`UhpServerOptions.cs:11,49`). | Add explicit supported/unsupported capability values and registration validation. Capability declarations must match runtime, provider and endpoint support. |
| P2 | Advanced query binding, binary transfer, plugin execution, builds and provider adapters have no AOT/allocation evidence. Existing AOT proof covers the preview. | Prove these paths incrementally from packed consumers, with generated metadata and measured allocation budgets. |

These are source-level findings. No official check has been recorded as passed or failed in this review. Existing local verification is historical evidence in [verification.md](verification.md), not a fresh test run.

## Scope decisions

The [architecture](https://unifiedharnessprotocol.org/spec/2026-10-19/architecture) defines cumulative classes. Plugins, environments, memories and interactive input are optional chapters, but declining them causes skips in this suite. The user selected the stronger zero-skip target. Therefore the conformance deployment must enable these families, session sharing and harness idempotency, and connect a provider whose declared features let the ME checks actually execute.

Not every optional variation needs to be enabled. Harness retirement can stay false, preserving history. Free memory queries, provider-specific operations, PDF conversion and imported history need honest capability/unsupported behavior; the plan must not invent support merely to make discovery look complete. Package checking is optional in prose but EN-09 must run for this target. Add local coverage for untested normative rules, rather than treating 104 green checks as the entire specification.

Resolve and document upstream inconsistencies before freezing public contracts:

1. Tasks' field table mentions top-level `environment`, while Environments §5, the pinned schema and EN checks use `metadata.environment`. Use the latter canonical form and document the discrepancy; do not silently create a second meaning.
2. Architecture places sharing in Full; Sessions §5 calls it optional and the suite skips when disabled. Implement it for the accepted strict target.
3. Prose requires some fields/operations that schemas do not: null error fields, session deletion, and required response fields. Add a prose-to-test ledger alongside operation/schema coverage.
4. Streaming §5 mentions a harness event feed; OpenAPI has no such path. Record whether it is an additional required surface or optional feed before declaring protocol coverage complete. Do not invent a `/responses/{id}/events` route. `Last-Event-ID` resumption is optional; stored response recovery is required.

## Package and ownership design

Keep `StaticNorth.Uhp.Core`, `.Client` and `.Server` as the public packages. Keep provider/runtime implementation details outside their wire models.

| Layer | Responsibility | Planned modules |
|---|---|---|
| Core | Public protocol models, immutable ownership, constants, version-aware JSON and generated wire metadata | `Models/Tasks`, `Harnesses`, `Sessions`, `Files`, `Plugins`, `Environments`, `Memories`; matching `Serialization` modules |
| Client | Async, cancellation-aware methods for every selected family; feature checks; streaming uploads/downloads; pagination | Family-specific partial `UhpClient` files or existing-class extraction only where useful; preserve existing text APIs |
| Server | Valiant groups/registration/validation, named typed handlers, errors/auth/version transport, bounded streaming | `Endpoints/<Family>`, `Validation/<Family>`, `Transport`, family backend interfaces |
| Host/backend | Principal isolation, atomic mutations, storage, execution, MCP/tool integration, builds, provider access | Retain existing execution backend; add focused service contracts for sessions, files, management, sharing, plugins, environments and memories |
| Conformance deployment | Reproducible integration of packed packages with actual configured execution/provider services | `samples/StaticNorth.Uhp.ConformanceHost` and deployment configuration; fixture remains a deterministic test double |

Do not grow `IUhpServerBackend` into an interface with every optional operation. Add family contracts without forcing Core-only hosts to implement unrelated features; reject inconsistent registration at startup. Shared policy belongs in the existing Valiant group/filter. Use Valiant-generated validation and mapping only where distinct models need mapping. Preserve method-group endpoints, separate handlers and concrete `TypedResults`.

All I/O remains async with cancellation. Request cancellation detaches readers; accepted work lives until completion, explicit cancel or host shutdown. Keep HTTP Core free of a chosen database, queue or agent engine. A Full claim belongs to a named host/backend configuration, not automatically to every host installing the package.

## Ordered implementation milestones

### M0 — Reproducible coverage and a real integration boundary

Add a separate suite provenance manifest and pinned installation/runner under `eng/conformance`; do not rewrite historical schema provenance. Extract the registry and compare it with the check inventory, including IDs/classes and schema availability. Capture strict report semantics in a validator, with negative cases for exit-zero-with-skips, subset runs, duplicates, missing schema and version mismatch. Lock transitive suite dependencies as well as its Git revision.

Deliver an operation/prose coverage ledger in M0/M1: method/path or normative rule, source/version, capability condition, Core/Client/Server/backend owner, implementation milestone and independent test. Include all 83 schema operations, prose-only operations and unresolved source conflicts. Unsupported conditional features need an explicit specified disposition; absence from the official registry does not remove a requirement.

Create the initial Core conformance-host skeleton consuming packed packages. Inventory configured local runtime, build executor and memory provider by safe capability probes, without printing credentials. Write `docs/conformance-deployment.md` as the M0 decision record: selected real harness base, build executor and provider, exact versions, probed capabilities, adapter ownership, storage/retention and deployment limits. The provider must support revision/history, entities/graph and changing facts natively or honestly through an adapter; merely declaring them false leaves skips. Probe a real task, persistent session, cancellable long task, file creation and provider read/write at their existing integration boundaries before building the later UHP families. If an existing backend cannot supply the accepted target, record the concrete integration gap; do not replace it with prompt-specific fake output or a transparent HTTP proxy.

Add diagnostic suite runs immediately; fixture runs may aid debugging but cannot be promoted to real conformance evidence. Retain old CE/SuperQode pins only as historical candidates until freshly verified.

Exit: reproducible suite/check inventory, validated reporting, and a selected execution/provider architecture with explicit ownership and deployment versions.

### M1 — Complete shared contracts and prove AOT binding early

Extend `eng/StaticNorth.Uhp.SchemaGen`, Core models, writers, constants and JSON contexts for selected schema families. Generate internal DTOs deterministically; use public typed models for task messages/roles, text/images/files, standalone input responses, harness configuration, pages, shares, builds and memory records. Preserve unknown fields/statuses/types and caller ownership. Distinguish absent values from required nulls and test serialized bytes against schema plus prose rules. The nullable-field matrix must cover response usage/error/incomplete details/previous response, required metadata/store, and error param/detail. Keep public text constructors compatible.

Preserve support for all three protocol dates already promised by the preview, with version-specific serialization/behavior and discovery evidence. An older date can keep its compatible surface without claiming every new capability. Any removal requires a separately agreed breaking migration. Do not advertise identical Full coverage for all dates by assumption, and do not add `2026-10-04` merely because upstream supports it.

Prototype one paginated query endpoint and one streamed multipart/binary endpoint using Valiant registration under Native AOT. Query opt-in must not reintroduce Valiant's reflection binder. Use supported generated binding, or fix the generator before expanding endpoints. [ASP.NET Core Request Delegate Generator](https://github.com/dotnet/aspnetcore.docs/blob/main/aspnetcore/fundamentals/aot/request-delegate-generator/rdg.md) and [streaming uploads](https://github.com/dotnet/aspnetcore.docs/blob/main/aspnetcore/mvc/models/file-uploads.md).

Exit: deterministic generation, canonical wire vectors and packed Native AOT probes pass without warning suppression or reflection JSON.

### M2 — Close Core behavioral defects

Execution mode is selected by the client API and carried in the transport payload/parsed submission envelope; callers do not have to coordinate contradictory mode flags:

| Client API | Wire mode | Result |
|---|---|---|
| Existing `RunAsync` | `stream: false`, background omitted/false | Terminal response only; preserve current behavior |
| Existing `StreamAsync` | `stream: true`, background omitted/false | Progressive SSE; detaching the reader never cancels accepted work |
| New `SubmitBackgroundAsync` | `stream: false`, `background: true` | First acceptance returns `in_progress` with its response ID; explicit polling uses `GetResponseAsync` |

Persist the acceptance record before exposing its ID when `store` is true/default. With `store: false`, accepted work still runs independently, but readback may legitimately return `response_not_found`; do not silently force storage or invent a final result. An idempotency replay returns the first execution's result, waiting if it is still running, so the background API must also accept a terminal replay result. Keep idempotency retention separate from public response retention. Test first/replayed/concurrent calls, disconnects and both store modes. The client never produces `stream: true` together with `background: true`; record the server's handling of this unspecified combination in the source-conflict ledger before implementation.

Fix nullable wire emission and complete ignored-field reporting. Validate default harness/model selection, substitutions, actual usage and budget outcomes. Add response deletion and input-item readback where specified, preserving the distinction between deletion and cancellation. [Tasks](https://unifiedharnessprotocol.org/spec/2026-10-19/tasks), [Errors](https://unifiedharnessprotocol.org/spec/2026-10-19/errors).

Audit progressive streaming, item lifecycle ordering and tool-call correlation, exactly one terminal snapshot, persistent continuation, terminal immutability, session concurrency, task idempotency and disconnect recovery. Any error after SSE headers start must follow the stream contract; do not attempt to replace a partial stream with a JSON error response. Resolve the harness-feed question from the scope ledger. [Streaming](https://unifiedharnessprotocol.org/spec/2026-10-19/streaming), [Lifecycle](https://unifiedharnessprotocol.org/spec/2026-10-19/lifecycle).

Checks: D/V/A/E/H/T-01…11, S and C. Exit: full Core coverage in the official real-backend run, with no skips; local negative cases cover rules beyond the suite's weaker assertions.

### M3 — Session browsing, history and deletion

Add client methods, typed pages/turns, focused backend contract and Valiant endpoints for session listing/filtering/cursors, session readback, ordered turns and session deletion. Preserve explicit last-page cursors and response IDs. Deletion cancels in-flight work first, removes access to history/files/shares and releases accounted storage. Verify model changes during continuation and distinguish unknown versus expired sessions. [Sessions](https://unifiedharnessprotocol.org/spec/2026-10-19/sessions).

Checks: X-01…04 and F-08 foundations. Exit: pagination boundaries, exact-full pages, two-principal access and cancel/delete races pass through client and server, including Native AOT query binding.

### M4 — File and image input; artifacts

Implement inline data URLs and upload-by-ID for files/images, multipart upload, typed file metadata and citations, session artifact listing and raw-byte download. Bound uploads by bytes actually read, including absent/false Content-Length, and return the specified 413 error. Stream transfers asynchronously with explicit ownership; do not buffer whole large files or base64 encode uploaded files again. Validate media types, filenames and container membership, including traversal, encoded paths and links. Set `nosniff`; retain artifacts across turns and revoke access on session deletion. [Files](https://unifiedharnessprotocol.org/spec/2026-10-19/files).

Cover optional archive and preview operations in the operation ledger: implement bounded archive generation where useful; return documented preview-unavailable behavior when conversion is absent. Never forward bearer credentials to an arbitrary artifact URL; validate origin/redirect behavior in client download APIs.

Checks: X-05…09. Exit: real file input and artifact round trip, original bytes/headers, cross-principal refusal and bounded-memory packed Native AOT transfer.

### M5 — Harness management, skills, direct MCP and budgets

Add typed create/update/delete APIs and service contracts. Enforce supported bases/models, immutable identity/base/creation timestamp, atomic updates, full folder/binary skill round trips and config-time SKILL.md/path validation. Preserve direct MCP configuration and disabled tools; connect enabled servers only and degrade unreachable ones as documented. Keep sessions/history when deleting a harness (`harness_retirement: false`). Implement atomic principal-scoped harness idempotency, concurrent replay and changed-request conflicts, including recreation after deletion. [Harnesses](https://unifiedharnessprotocol.org/spec/2026-10-19/harnesses).

Enforce step/time budgets in real execution, with partial output and `incomplete` reasons. Where the specification permits an ignored budget, report it honestly, but the chosen conformance host should demonstrate actual bounded execution. Provision a locally controlled instance of the pinned suite's MCP fixture containing echo and step tools, and prove reachability from the agent sandbox. T-12 attaches this server directly to a managed harness; its proof does not depend on plugin implementation. Add task/harness idempotency race and retention tests, including concurrent background submissions under the same key.

Checks: F-01…07, F-09…10 and T-12. Exit: config survives unrelated edits; direct MCP/tool restriction behavior, observed bounded tool rounds and history retention work on the selected real base.

### M6 — Read-only, revocable session sharing

Add the share model, owner-authenticated create/read/revoke endpoints and a separately authorized share-view route. The existing filter authenticates every non-discovery endpoint: isolate the public view outside that bearer policy with narrowly scoped share-token validation, while retaining normal authentication/version/error handling for share administration and every other API route. Never introduce a group-wide anonymous bypass. A bodyless POST publishes. Derive links from the configured deployment prefix; validate relative URLs and proxy behavior. Link holders may read the published view only. Revoke every minted link on revocation/deletion; share IDs never become API bearer credentials. Redact credentials and unrelated principal data from the view.

Checks: R-01…08. Exit: anonymous read works, all mutations and cross-object escalation fail, and repeated mint/revoke/delete races cannot retain access.

### M7 — Plugins as real packages and runtime tools

Implement manifest schema discovery, complete package storage/readback, derived server/skill fields, collision validation and atomic installation. Preserve disabled plugins without contacting them, retain files during unrelated edits, and export reinstallable packages while recording removed credentials. Implement remote and stdio transports, sandbox containment, placeholder rules, persistent per-session plugin data and graceful component failures. [Plugins](https://unifiedharnessprotocol.org/spec/2026-10-19/plugins).

Pin the [Agent Plugins 1.0.0 specification/schema](https://agent-plugins.org/) referenced by UHP before implementing manifest validation. Reuse the MCP fixture provisioned in M5 to verify plugin-mediated tool access, not just direct attachment. No new dynamic plugin assembly loading is required by UHP.

Checks: P-01…11; rerun T-12 only if changed execution/composition paths warrant it. Exit: package round trip/export and an actual agent tool call return the fixture's nonce-derived result. Configuration-only passing is insufficient.

### M8 — Questions and secret answers

Implement typed `input_request`/`input_response`, `ask_user` integration, terminal incomplete questions and atomic pending-answer state. Cover choice/confirm/free-text/secret, expiration, malformed answers, duplicate answers, superseded questions and the required continuation behavior. Route secret values through a host secret reference rather than conversational state; prevent them entering responses, input-item/history reads, events, logs, shares or memory observations.

Checks: T-13…14. Exit: a real harness asks and resumes, invalid answers preserve the pending question, concurrent answers consume it once, and a secret nonce appears nowhere client-readable.

### M9 — Environments and builds

Add environment lifecycle, source file/directory operations, archive/git import, runtime/package discovery, asynchronous build records/logs, immutable version listings/activation and harness attachment. Validate paths, links, decompression limits and allowed repository origins. Isolate dependency installation/build scripts from host privileges and credentials. Preserve the old active version during failed/running builds; mount the selected version read-only with private session workspaces, correct dependencies and reported version. [Environments](https://unifiedharnessprotocol.org/spec/2026-10-19/environments).

Use a host build-executor contract and an actual sandbox/toolchain; HTTP endpoints alone cannot implement this chapter. Cover package checking because the zero-skip target includes EN-09. Test that source edits do not affect active sessions until build/activation, and that one environment cannot expose another or another session's working directory.

Checks: EN-01…09. Exit: real dependency build, rollback, preflight refusals and read-only execution succeed through the packed server and client.

### M10 — Memories, provider capabilities and agent integration

Add typed tree/provider/type/grant APIs; enforce inheritance, restricted cutoffs and principal privileges on every path. Implement records and ordered content/file parts, writer stamps, references, revision/history/as-of/snapshots, forget versus erase, subtree moves/deletion, scoped recall/filter/degradation, graph reads and changing facts. Preserve unknown record/content types. Select a connected provider with revision, graph, `holds` and prime behavior needed for the accepted report; report native versus emulated support truthfully. [Memories](https://unifiedharnessprotocol.org/spec/2026-10-19/memories).

Cover observe/jobs, named queries, provider-specific operations, consolidation records/changes/revert and honest unsupported free-query behavior in the operation ledger. Free queries cannot bypass principal scope. Integrate granted memories with the actual agent's tools, bounded standing-record priming, per-turn observations and external-provider disclosure. A task naming a memory grants no new access. Keep secret answers out of observation and recall.

Checks: ME-01…13. Exit: all ME checks run on the connected provider, with no capability/feature skips; local two-principal, restricted-subtree, concurrent fact-write and erase-history tests also pass. Memory quality evaluation remains separate from protocol correctness.

### M11 — Evidence, compatibility and release

Run the full pinned suite against the real packed Native AOT deployment, whose discovery actually declares `full` and the correct enabled capabilities. Resolve every fail/error/skip without editing upstream assertions or replacing observations with assumptions. A model that never exercises a required behavior leaves that run unverified; use a capable real model/configuration and preserve diagnostic runs. Do not fabricate a passing check or tune the server to recognize test prompts.

Run client interoperability against independently configured CE and SuperQode versions, checking only features actually advertised. Publish a per-server feature matrix; their historically measured classes do not establish support for every new optional family.

Extend CI with deterministic fixture/TUnit checks on PRs and explicit real-backend conformance release gates. Verify locked restore, generation/provenance, warning-free builds, formatting, package contents and fresh-cache consumers. Publish/run Native AOT for macOS arm64 and Linux x64 with reflection JSON disabled, no trim/AOT warning suppressions. Extend warmed allocation budgets for binary transfer, pages, package derivation, build polling and memory operations. Preserve the zero-avoidable-hot-path-allocation requirement without claiming zero required HTTP/owned-payload allocations.

Update README, server guide, migration/performance/verification/interoperability docs and per-family examples. Store SDK/server/client evidence separately. State exactly which served dates/configurations achieved which class; remeasure each release. Only then remove preview exclusions and publish a Full claim.

## Tests and changes by area

| Area | Add/modify | Verification |
|---|---|---|
| Schema/Core | Generator roots, typed models, JSON contexts/converters/writers, constants | Exact wire vectors; schema plus prose constraints; ownership/unknown-field round trips; required-null cases |
| Client | Family APIs, query construction, upload/download lifetimes, feature checks | Scripted HTTP edge cases plus real same-consumer runs; no automatic replay of uncertain mutations |
| Server | Family Valiant groups/endpoints/validators and focused backend contracts | TUnit integration against independent expectations; malformed body/query/multipart errors use UHP envelopes |
| Host | Execution/storage/build/provider adapters, lifecycle and retention | Actual progress/cancel/continuation/tool/build/provider behavior; restart/cleanup and concurrency boundaries |
| Security | Scoping, shares, secrets, file/package/import containment, remote origins and hosting | Two principals across every operation; TLS outside loopback; credential revocation; symlinks/encoded paths/archive traversal; no credential egress/leaks |
| Tooling/CI | Pinned official suite runner/report validator; expanded packed/AOT smoke | Registry completeness; fail-closed release checks; all platform artifacts retained |

Keep TUnit for .NET tests; the official Python suite remains upstream and unmodified. Add tests for each behavior after its implementation builds, following the existing workflow. Avoid increasing the current minimum test count as a substitute for behavioral coverage.

Exercise framework-generated failures through the deployed host: malformed query binding, multipart parsing, routing/method failures and body-size rejection must preserve UHP error envelopes and version headers before headers have started. Extend the existing two-principal/disconnect cases to new families and explicitly distinguish response deletion, session deletion and cancellation. Test the public share-view policy together with the normal group filter.

The eventual pinned official invocation uses a dedicated test deployment. Credentials are read by the suite from `UHP_API_KEY`; keep them out of command arguments and committed files:

```sh
artifacts/conformance/.venv/bin/uhp-conformance \
  --base-url "$UHP_CONFORMANCE_BASE_URL" \
  --harness-id "$UHP_CONFORMANCE_HARNESS_ID" \
  --model "$UHP_CONFORMANCE_MODEL" \
  --class full \
  --plugin-mcp-url "$UHP_CONFORMANCE_MCP_URL" \
  --json artifacts/conformance/full.json \
  --plain
```

M0's runner installs the exact suite revision and dependencies and validates the resulting report. This is a planned command; it has not been run as part of this review.

## Release acceptance checklist

- [ ] All reviewed schema operations and prose requirements have an implemented/conditionally unsupported/tested disposition, with no unresolved normative ambiguity.
- [ ] The full registry matches the pinned inventory; every official check executes and passes on the named `2026-10-19` Full deployment.
- [ ] Optional families needed by that run are enabled and exercised, not merely advertised.
- [ ] All strict report/coverage/version predicates pass; original JSON and deployment provenance are retained.
- [ ] Core-only and Extended configurations remain usable without unrelated services; their advertisements are truthful.
- [ ] Older advertised dates have their own compatibility evidence; unsupported combinations are not advertised.
- [ ] New client/server APIs, local security/concurrency cases and independent client interoperability pass.
- [ ] Packed macOS arm64 and Linux x64 Native AOT consumers run without reflection fallback or suppressed diagnostics.
- [ ] Allocation budgets, async cancellation/lifetimes, generated Valiant registration and repository style requirements remain satisfied.
- [ ] User-facing claims and package/release documentation describe the measured result and remaining optional limitations accurately.

## Review and limits of this planning work

This change contains review documentation and an inventory only. It does not change protocol behavior, claim new passing tests, or spend model/provider tokens. Integration selection and live official runs are M0/M11 work. The existing fixture and previous AOT results remain useful regression baselines.

Independent skeptical review: initially **approve with changes**, then **approved** after re-review. Incorporated: preserve all promised protocol dates, keep RunAsync terminal-only with separate background submission semantics, move the direct-MCP budget proof to M5, narrowly isolate anonymous share views, require an operation/prose ledger and versioned integration decision record, and expand nullable-field, hosting, idempotency and deletion tests. No review finding was rejected. No planning blocker remains to starting M0; implementation and live evidence remain outstanding.
