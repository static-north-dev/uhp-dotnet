# Reference test support

This project is **not packable** and is not referenced by Core, Client or Server. It is shared implementation support for tests and the eventual reference host; application hosts such as Kult supply their own backend implementations.

`Memory/InMemoryMemoryProvider` implements the support-only `IReferenceMemoryProvider` contract using process-local dictionaries and a lock. It has no database, network or Core dependency. Use one instance for the lifetime of a reference host. Releasing it discards all stored state.

Implemented behavior:

- Owner-scoped memory containers and record reads/writes/listing/deletion.
- Owned immutable record snapshots: ordered content parts, frozen attributes and references survive disposal or mutation of caller inputs.
- Source-generated text-shorthand normalization, validated text/file parts, and unchanged opaque `x.*` extension parts. File parts retain references and metadata; this store does not resolve or download their bytes.
- Trusted caller identity stamps, untrusted content, injected `TimeProvider`, and atomic increasing revisions. History is returned as a snapshot; earlier caller snapshots remain unchanged.
- Forgetting hides a record from ordinary reads/listing and appends its forgotten version. Erasure removes all versions from provider storage. Deleting a memory removes all its records.
- Cancellation before mutation, serialized concurrent revisions and completed `ValueTask` operations. All work is CPU-only; no fake asynchronous I/O or thread-pool wrappers are used.

The owner argument comes from trusted host identity resolution, never directly from an unauthenticated request. This private-container implementation does not implement the server facade's memory tree, inherited grants, shared-member policies, cross-memory reference redaction or HTTP error mapping. Its record snapshots are support models, not wire DTOs. Recall, graph/fact semantics, observation/priming and the remaining UHP memory operations are later Full-conformance work. The SDK therefore continues to advertise `memories: false`.

TUnit covers isolation across all store operations, container boundaries, input ownership, version snapshots, malformed-write atomicity, forget/erase/delete behavior, cancellation, independent instances, concurrent revisions and file references. Warmed reads measure **0 B/read**. A small retained text record measures **748 B/write** on the managed macOS arm64 run, including required IDs, snapshots and amortized dictionary growth, against a 2,048 B/write ceiling. This is not a whole-provider zero-allocation claim.
