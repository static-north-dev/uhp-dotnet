# Official conformance tooling

The source and suite pin is in `manifest.json`. `uv.lock` here locks the complete Python 3.13 environment, including the official MCP fixture. Upstream's own lock omits its current fixture extra; upstream files remain unmodified. No dependency on this environment is added to the NuGet packages.

Create a checkout at `artifacts/conformance/upstream` and check out exactly the manifest commit. Then run from the repository root:

```sh
dotnet run --project eng/StaticNorth.Uhp.Conformance --configuration Release -- verify-suite . artifacts/conformance/upstream
uv sync --locked --project eng/conformance
uv run --frozen --project eng/conformance uhp-conformance --help
```

The verifier checks the upstream source/schema/license/lock hashes and matches every check ID/class to `docs/conformance-checks.json`. The inventory itself is pinned by hash. Review and update that hash when deliberately changing inventory metadata or upgrading the suite.

To run the official suite, supply a dedicated real .NET server deployment, configured harness/model and agent-reachable MCP fixture URL. Supply the bearer credential through `UHP_API_KEY`; never commit it or put it in command arguments:

```sh
uv run --frozen --project eng/conformance uhp-conformance \
  --base-url "$UHP_CONFORMANCE_BASE_URL" \
  --harness-id "$UHP_CONFORMANCE_HARNESS_ID" \
  --model "$UHP_CONFORMANCE_MODEL" \
  --plugin-mcp-url "$UHP_CONFORMANCE_MCP_URL" \
  --class full --json artifacts/conformance/full.json --plain
dotnet run --project eng/StaticNorth.Uhp.Conformance --configuration Release -- validate . artifacts/conformance/full.json
```

The CLI accepts a base URL with a deployment prefix. The final gate independently checks every check ID/class/outcome, totals, full coverage, version fields, timestamp and strict verdict; an exit-zero run with skips is rejected. This validates a report, not the identity of a remote server. Retain deployment provenance separately, including the SDK/backend/runtime/provider versions and enabled capabilities.

For local verification, follow the [reference deployment instructions](../../docs/conformance-deployment.md). The runner creates its own loopback credentials, launches the managed or packed Native AOT Codex host, and runs the locked official suite. Unit tests also use synthetic in-memory reports to exercise gate rejection; these are never saved as live conformance evidence.

The manually dispatched `Official Full conformance` workflow requires the `uhp-conformance` environment, secret `UHP_CONFORMANCE_TOKEN`, and variables `UHP_CONFORMANCE_BASE_URL`, `UHP_CONFORMANCE_HARNESS_ID`, `UHP_CONFORMANCE_MODEL`, and `UHP_CONFORMANCE_MCP_URL`. It runs against a separately configured deployment and fails on missing configuration.
