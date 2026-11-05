# uhp-dotnet

[![CI on develop](https://github.com/static-north-dev/uhp-dotnet/actions/workflows/ci.yml/badge.svg?branch=develop&event=push)](https://github.com/static-north-dev/uhp-dotnet/actions/workflows/ci.yml?query=branch%3Adevelop+event%3Apush)

Community .NET SDK for connecting to UHP servers, with shared contracts and an ASP.NET Core server integration. Targets .NET 10 and supports Native AOT. Package versions come from release tags and are independent of negotiated protocol dates.

The [official Unified Harness Protocol documentation](https://unifiedharnessprotocol.org/) is the source of truth for protocol behavior, schemas, and conformance requirements. Target date: **2026-10-19**. Tested fixture compatibility dates: **2026-09-28** and **2026-09-12**. Inputs are pinned to upstream commit `80d5d369a1ddf14cad8bd9cf3a1a5891d15616b6` in [spec/upstream](spec/upstream).

## Packages

| Package | Purpose | Dependencies |
|---|---|---|
| `StaticNorth.Uhp.Core` | Protocol models, typed events, owned extension JSON, source-generated serialization | .NET runtime |
| `StaticNorth.Uhp.Client` | Discovery, negotiation, HTTP requests, streaming, recovery and cancellation | Core |
| `StaticNorth.Uhp.Server` | ASP.NET Core endpoints and a host-owned backend contract | Core, Valiant Endpoints and Validation 1.2.1 |

The client and Core have no ASP.NET Core or Valiant dependency. Protocol models live in `StaticNorth.Uhp.Core`; client entry points and transport exceptions live in `StaticNorth.Uhp.Client`. All project, assembly, package, and namespace identities use the `StaticNorth.Uhp` prefix. See [migration notes](docs/migration.md) and [allocation evidence](docs/performance.md).

The server package provides transport and backend integration. The host supplies an agent runtime, persistence, and principal isolation. The deterministic fixture is test infrastructure. These packages do not contain a production agent runtime.

See the [Codex provider preview](docs/codex-provider.md), [Claude Code provider preview](docs/claude-code-provider.md), [OpenCode provider preview](docs/opencode-provider.md), and [harness adapter roadmap](docs/harness-adapters-plan.md).

## Client usage

After installing `StaticNorth.Uhp.Client` from a published release or the local package feed:

```csharp
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.Core;

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
{
    BaseAddress = baseUri,
    Timeout = Timeout.InfiniteTimeSpan
};

var client = await UhpClient.ConnectAsync(http, new UhpClientOptions
{
    GetBearerTokenAsync = _ => ValueTask.FromResult(token)
}, ct);

var request = new UhpTaskRequest("Summarize the README in three bullets.")
{
    HarnessId = harnessId,
    Store = true
};

string? responseId = null;

await foreach (var update in client.StreamAsync(request, ct))
{
    switch (update)
    {
        case UhpResponseCreated created:
            responseId = created.Response.Id;
            break;
        case UhpTextDelta delta:
            Console.Write(delta.Text);
            break;
    }
}

var result = await client.GetResponseAsync(
    responseId ?? throw new InvalidOperationException("Missing response ID."), ct);

if (result.Status == UhpResponseStatus.Completed)
{
    var answer = await client.RunAsync(new UhpTaskRequest("Expand on the second bullet.")
    {
        PreviousResponseId = result.Id
    }, ct);

    Console.WriteLine(answer.Status);
}
```

`ConnectAsync` discovers anonymously and selects the newest mutually supported date. Set `ProtocolVersion` to require an exact supported date. If that date differs from the server default, discovery runs again for its capabilities. Missing capability flags are false.

The base URL is the deployment root, such as `http://localhost:3000/api/harness`; both trailing-slash forms preserve the prefix. HTTPS is required except on loopback. The caller owns `HttpClient`. Credentials and negotiated version headers are set per authenticated request. Configure the transport without default credentials, credential-injecting discovery handlers, credential-forwarding redirects, automatic POST retries, or hedging.

The SDK includes harness and model catalogs, foreground and background tasks, streaming, continuation, cancellation, and response lookup. It also exposes typed methods for file transfers, sessions and sharing, harness management, plugins, environments, memories, and interactive answers. The deployment determines which capabilities are available. Use `UhpUserTextMessage` for text message arrays or `UhpTaskRequest.FromInput` for structured inputs. Unknown statuses preserve their original `Value`.

## Recovery, errors and cancellation

A task stream is single-use. Enumerating it twice fails before a second POST. Breaking enumeration or cancelling a local token closes the HTTP read; it does not send a remote cancellation command. Use `CancelResponseAsync` or `CancelSessionAsync` explicitly, then poll `GetResponseAsync` if terminal confirmation is required.

```csharp
catch (UhpStreamException exception) when (exception.ResponseId is { } id)
{
    var snapshot = await client.GetResponseAsync(id, ct);
    RenderSnapshot(snapshot);
}
```

Replace any partial display with that authoritative snapshot; poll while it is in progress. A `store: false` lookup error is propagated. The SDK never reconstructs missed events or resubmits automatically. `UhpStreamException` retains the response ID, last accepted sequence, idempotency key, and cause. Caller cancellation remains `OperationCanceledException`. Set `StreamInactivityTimeout` for a byte-based timeout that includes keepalives; there is no SDK overall streaming deadline.

An uncertain non-streaming POST throws `UhpSubmissionException`, retaining the cause and supplied key. HTTP errors throw `UhpApiException` with status, error fields, owned detail, raw `Retry-After`, and expected/received protocol dates. Version-drift errors retain `unsupported_protocol_version` and its advertised alternatives. Non-JSON proxy errors retain status without copying their body into exceptions. Failed tasks returned with HTTP 200 remain ordinary `UhpResponse` values with partial output.

For a deliberate manual replay, assign an `IdempotencyKey` before the first submission and reuse the identical request, stream mode and key. Keyed submissions are rejected locally unless discovery advertises idempotency. Use independent keys for independent user actions. Prefer a GET whenever the response ID is known. Handle `session_busy`, `rate_limited`, and `quota_exhausted` by code; the SDK supplies no retry policy. The libraries log no task text, tool payloads, bodies, or credentials.

## Build and verify

The SDK is pinned in `global.json`. Valiant Endpoints and Validation `1.2.1` restore from NuGet.org; no sibling checkout or local Valiant package is required. Run .NET commands directly so diagnostics remain visible:

```sh
dotnet restore StaticNorth.Uhp.slnx --locked-mode
dotnet run --project tools/StaticNorth.Uhp.SchemaGen --configuration Release -- --check
dotnet build StaticNorth.Uhp.slnx --configuration Release --no-restore
dotnet run --project tests/StaticNorth.Uhp.Client.Tests --configuration Release --no-build --no-restore -- --minimum-expected-tests 179
dotnet format StaticNorth.Uhp.slnx --no-restore --verify-no-changes
dotnet pack src/StaticNorth.Uhp.Core --configuration Release --no-build
dotnet pack src/StaticNorth.Uhp.Client --configuration Release --no-build
dotnet pack src/StaticNorth.Uhp.Server --configuration Release --no-build
python3 tools/package-smoke.py
```

The smoke gate checks package metadata/notices/symbols, installs both sample consumers from the local feed with no SDK project references and a fresh package cache, publishes both with Native AOT and reflection-based JSON disabled, and runs the shared lifecycle scenario. It selects the patched .NET runtime/compiler `10.0.14`; override with `--runtime-version` when updating that pin. On an offline workstation with the dependencies cached, pass `--offline-cache /path/to/nuget/packages`. Native AOT requires the platform's native compiler toolchain.

After packing, run the console sample against a configured server with `UHP_BASE_URL`, `UHP_TOKEN`, and `UHP_HARNESS_ID` in the environment:

```sh
dotnet run --project samples/StaticNorth.Uhp.Console --configuration Release
```

The console sample restores SDK packages from `artifacts/packages`. The reference sample uses that feed when `UseUhpPackages=true`. The packed smoke test uses SDK packages for both consumers, and the reference host and fixture share non-packable implementation code.

Read [server integration](docs/server.md), [repository tools](tools/README.md), and [interoperability evidence](docs/interoperability.md).

The [Codex reference deployment](docs/conformance-deployment.md) passed the unmodified official UHP **Full** suite for `2026-10-19`: **104 checks passed, zero skips, failures, or errors**. See the [verification record](docs/verification.md) for the tested host, report, and commands, and the [review plan](docs/full-conformance-plan.md) for scope and protocol decisions.

The [in-memory reference provider](tests/StaticNorth.Uhp.TestSupport/README.md) supplies stateful memory storage for testing. It lives in a non-packable support project; the published SDK packages retain pluggable backend boundaries.

## Publish to NuGet.org

The [release workflow](.github/workflows/release.yml) builds and tests the tagged commit, then publishes Core, Client, and Server using the tag version with the leading `v` removed. To publish the first preview, push the tag `v0.1.0-preview.1` from a verified commit that contains the workflow.

Before the first tag push, set the GitHub Actions variable `NUGET_USER` to the NuGet.org username. On NuGet.org, configure [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) for `static-north-dev/uhp-dotnet`, workflow `release.yml`, and environment `production`. Scope the policy to `StaticNorth.Uhp.*` and allow publishing new packages and new versions.

Apache-2.0. Vendored upstream license, attribution, and hashes are under `spec/upstream` and included in each package.
