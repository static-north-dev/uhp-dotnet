# Allocation and async verification

The requirement is zero avoidable allocations in repeated hot paths. HTTP messages/content, owned JSON snapshots, parsed result graphs, and UTF-8 SSE frames have explicit allocation budgets. Neither an entire request nor the SDK as a whole is claimed to allocate zero bytes.

Measurements below use the pinned .NET SDK 10.0.401 on macOS arm64. `AllocationTests` warms the serializers, reuses a caller-owned 16 KiB buffer and `Utf8JsonWriter`, and measures 256 synchronous writes with `GC.GetAllocatedBytesForCurrentThread`. Buffer creation/growth, model creation, assertions, logging, and awaits are outside the measured interval. Tests run without competing test workloads.

| Writer/input | Before | Current warmed allocation |
|---|---:|---:|
| Nonempty edited response, dictionary metadata and raw extensions | about 5.4 KiB/write | 0 B/write |
| Nonempty edited response, frozen metadata and raw extensions | 5,440 B/write | 0 B/write |
| Catalog with backend/model/root extensions | 2,520 B/write | 0 B/write |
| Discovery, harness, harness-model catalog, error, cancellation | Not recorded | 0 B/write each |
| Edited parsed event with raw extensions | Not recorded | 0 B/write |
| Printable idempotency key and JSON content-type checks | Not recorded | 0 B/check batch |

The writer checks cover nonempty collections, modified parsed records, nested extensions, and both dictionary/frozen metadata paths. Arbitrary caller-provided collection implementations can allocate their own enumerators; the zero-byte writer guarantee covers the concrete dictionary/frozen and indexed-list paths exercised by these tests. There is no reflection-based discovery of caller collections.

Required operations use a separate warmed 128-operation measurement, with payloads and delegates created before the interval:

| Required operation and test payload | Measured | Test upper budget |
|---|---:|---:|
| Owned text request snapshot with harness/trace metadata | 360 B/operation | 4,096 B |
| Parsed response with session metadata from an owned input | 2,184 B/operation | 8,192 B |
| `JsonContent` for a generated-metadata payload | 248 B/operation | 1,024 B |
| Generated UTF-8 response-created SSE frame | 240 B/operation | 4,096 B |

These are fixture-specific allocation figures and regression ceilings, not production RSS, latency, or maximum payload sizes. Parsing includes source-generated structural validation and an immutable result graph; payload size and output collections change its required costs. SSE owns one byte array per frame because ASP.NET Core's standard SSE result consumes byte arrays. It writes that array as JSON text without an intermediate DOM, runtime polymorphic lookup, or second serialization. Request bodies use `JsonContent` with generated metadata, avoiding the former UTF-16 string and re-encoding copy.

The SDK also reuses a stream's inactivity deadline between reads, caches immutable ignored-field combinations, validates task budgets as a value type, reads headers without temporary arrays, and uses indexed collection loops in hot writers. The fixture replays events with an index rather than allocating batch snapshots and finishes only after publishing the terminal event. Ownership copies required for independent execution and principal isolation remain. All 98 local tests, including these allocation assertions, also pass as a Native AOT executable.

Network/stream I/O and generator file/network I/O use async APIs with cancellation. JSON encoding, validation, and hashing stay synchronous CPU work. The implementation introduces no blocking waits or `Task.Run` wrappers. Schema directory metadata operations use the available platform APIs.

Run the warmed checks with:

```sh
dotnet run --project tests/StaticNorth.Uhp.Client.Tests --configuration Release --no-build --no-restore -- --treenode-filter '/*/*/AllocationTests/*' --minimum-expected-tests 11 --report-trx --results-directory artifacts/performance
```

TRX files retain per-test measured output. The complete suite and packed native HTTP/SSE scenario provide the behavioral and AOT checks alongside these local allocation measurements.
