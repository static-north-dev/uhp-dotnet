# Interoperability and release evidence

The [upstream measured-implementations page](https://unifiedharnessprotocol.org/conformance#measured-implementations) lists historical server conformance evidence:

| Candidate server | Upstream measured protocol | Upstream result | This SDK against that server |
|---|---|---|---|
| HarnessRouter CE 0.25.6 | 2026-09-12 | Full, 76/76, measured 27 September 2026 | Unverified: no pinned instance/credential configured |
| SuperQode 2.3.1 | 2026-09-12 | Core; overall report 42 passed, 9 failed, 23 skipped, measured 13 September 2026 | Unverified: no pinned instance/credential configured |

The reports above belong to the servers and upstream suite. They are not measurements of this SDK. SuperQode can serve its native harness with `superqode serve uhp`; HarnessRouter CE exposes the UHP deployment root at `/api/harness`. Its hosted service is a separate implementation/deployment. The public endpoint named in an archived report does not supply current credentials or prove its present version.

The shared public-consumer scenario runs against either server by changing configuration. For each prefix `UHP_HARNESSROUTER_` and `UHP_SUPERQODE_`, configure:

- `BASE_URL`: deployment root, without `/v1`.
- `TOKEN`: credential supplied through the local environment or trusted CI secrets.
- `HARNESS_ID`: a configured harness that can execute tasks.
- `SERVER_VERSION`: the pinned implementation version, verified by the operator.
- `PROTOCOL_VERSION`: optional exact protocol date; use the date advertised by that pinned server.

```sh
UHP_INTEROP_RESULTS="$PWD/artifacts/interop" dotnet test tests/Uhp.Client.InteropTests --configuration Release
python3 eng/check-interop.py artifacts/interop
```

Missing configuration is recorded as skip/unverified. Reports contain configured and advertised server versions, negotiated date, discovery capabilities, and pass/fail/skip for each operation. If discovery advertises a different implementation version than the configured pin, the run fails. Real tasks can consume provider credits. Use a dedicated trusted test environment. The manual `Real server release gate` workflow runs outside pull requests using its environment's configuration/secrets. It does not publish packages.

The regular PR gate uses deterministic fixtures: all three dates, wire shapes and errors, byte-fragmented SSE, unknown data, sequence failures, cancellation, request counts, principal isolation, manual idempotent replay, and stored recovery. The package gate runs the same lifecycle using packed Native AOT client and server consumers with reflection-based JSON disabled. These establish the tested SDK behavior; they do not replace credentialed interoperability.

Release `0.1.0-preview.1` only after the Release build/tests, deterministic regeneration, package installation, warning-free Native AOT smoke, and both pinned real-server matrices pass. Record optional capabilities separately from exercised preview operations. No server conformance or upstream certification is claimed. Once released, the SDK may be proposed for the upstream directory with its SDK role, protocol dates, public release URL and maintainer contact.
