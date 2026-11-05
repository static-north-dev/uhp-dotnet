# Repository tools

This directory contains the schema generator, package smoke tests, and UHP conformance tooling. The [conformance guide](conformance/README.md) covers setup and report validation.

Use the .NET SDK pinned by `global.json`. The generator has no external dependencies; the SDK is its pinned tooling. A clean library checkout builds from checked-in generated files without downloading upstream sources.

`spec/upstream/manifest.json` identifies the repository, exact commit, upstream file paths and SHA-256 hashes for six schema inputs and the upstream license/notice. The checked-in inputs are byte-for-byte copies. OpenAPI defines routes and JSON Schema defines wire models. Versioned chapters define behavior. In particular, tasks use `POST /v1/responses`; the introductory guide's `/v1/tasks` is not the implemented route.

```sh
# Verify input hashes and deterministic generated output.
dotnet run --project tools/StaticNorth.Uhp.SchemaGen -- --check

# Regenerate after an intentional generator change.
dotnet run --project tools/StaticNorth.Uhp.SchemaGen

# Re-download the exact manifest pin, verify hashes, then regenerate.
dotnet run --project tools/StaticNorth.Uhp.SchemaGen -- --refresh
```

The preview generator derives property names, types, requiredness and nullability from the target JSON Schema and emits one internal class per file plus a JSON source-generation context. String enums remain open strings so unknown statuses survive. The string-or-array input is preserved as JSON at the public request boundary. A small handwritten output-item converter preserves new item kinds even when their properties have unfamiliar shapes. A second pass generates public typed contract DTOs and client/server operations from the pinned OpenAPI, including harness, plugin, session, file, environment, and memory APIs. The generated endpoints use Valiant registration and standard typed ASP.NET Core results. Unsupported generator shapes fail rather than guessing. Schema constraints beyond generated structural types are checked at the relevant protocol boundary.

To change the upstream pin, deliberately review all three dates' inputs, update the manifest commit and expected hashes, regenerate, and run the full verification and real-server gates. Do not silently update hashes during ordinary generation or builds. NuGet dependency changes require refreshing and reviewing `packages.lock.json` files.

`package-smoke.py` requires Python 3.13 in CI and uses only its standard library. It checks all three NuGet packages and symbols, restores the samples into a fresh temporary package cache, compiles/publishes them with Native AOT, runs the scenario against the native fixture server, and stops that server. Pass `--reference-host` to also publish the real Codex host against the packed SDK. Evidence is written under `artifacts/aot`.

The samples consume freshly rebuilt local preview packages, so they do not commit content-hash lock files for those unpublished artifacts. The smoke gate creates its own isolated lock files. Library, tooling and test dependencies retain checked-in lock files.

`check-interop.py` verifies that both pinned real-server reports exist and every operation passed. Run it after the [local interoperability tests](../docs/interoperability.md).
