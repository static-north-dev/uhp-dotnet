# StaticNorth project and test migration

All eight projects, their assemblies, namespaces, package IDs, and directories now start with `StaticNorth.Uhp`. The solution is `StaticNorth.Uhp.slnx`.

Install or reference `StaticNorth.Uhp.Client`, `StaticNorth.Uhp.Core`, and `StaticNorth.Uhp.Server` as appropriate. Update using directives to `StaticNorth.Uhp.Client`, `StaticNorth.Uhp.Core`, and `StaticNorth.Uhp.Server`. Public `Uhp*` type and method names are retained. This is a source/package identity change; no compatibility aliases are provided for the previous preview identities. Protocol dates, routes, headers, JSON names, event types, and status strings are unchanged. Vendored upstream files and the archived original implementation specification remain byte-for-byte intact.

The two test projects now use TUnit 1.79.0 with generated discovery and executable runners. Every original method and data row is retained: 70 local cases and two real-server cases. The suite now has 98 local cases after adding allocation, ownership, extension, protocol, and terminal-publication regression coverage. Run it with the pinned SDK:

```sh
dotnet build StaticNorth.Uhp.slnx --configuration Release
dotnet run --project tests/StaticNorth.Uhp.Client.Tests --configuration Release --no-build --no-restore -- --minimum-expected-tests 98 --report-trx --results-directory artifacts/test-results
```

Use Microsoft.Testing.Platform's `--report-trx` options, rather than VSTest logger options. `global.json` selects that runner for SDK test commands. Timing-sensitive tests and allocation benchmarks are isolated from parallel execution; independent behavioral cases remain parallel.

Real-server tests write structured reports from `finally` even when skipped. The local `check-interop.py` script requires both reports and a pass for every operation.

The same 98 local cases also pass under Native AOT. Reproduce on macOS arm64 with the pinned SDK; CI uses `linux-x64`:

```sh
dotnet publish tests/StaticNorth.Uhp.Client.Tests --configuration Release --runtime osx-arm64 \
  -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.14 -p:EnableRequestDelegateGenerator=true \
  -p:NuGetLockFilePath=obj/native-tests.lock.json -p:TrimmerSingleWarn=false \
  --output artifacts/aot/StaticNorth.Uhp.Client.Tests
artifacts/aot/StaticNorth.Uhp.Client.Tests/StaticNorth.Uhp.Client.Tests --minimum-expected-tests 98 --report-trx --results-directory artifacts/aot-test-results
```

The separate lock-file path keeps RID/compiler dependencies out of the committed framework-only dependency locks. The request delegate generator is enabled for the test host's named health endpoint.

The server still uses Valiant 1.2.1 automatic group registration and named `TypedResults` handlers. JSON uses explicit generated metadata throughout. Event frames use ASP.NET Core's raw UTF-8 byte-array SSE path; their public wire data remains JSON.
