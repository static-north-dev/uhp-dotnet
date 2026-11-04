# Server integration

Install `Uhp.Server`. Configure an ASP.NET Core authentication scheme and implement the singleton `IUhpServerBackend`:

```csharp
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        // Configure the host's issuer, audience and signing-key validation.
    });

builder.Services.AddUhpServer<MyBackend>(options =>
{
    options.AuthenticationScheme = "Bearer";
    options.AuthorizationPolicy = "RunAgents"; // Optional host policy.
});

var app = builder.Build();
app.MapUhp("/api/harness");
```

JWT bearer authentication in that example is supplied by the host's authentication package. The SDK does not issue tokens or choose an issuer. Configure TLS and trusted proxy forwarding in the host. The fixture uses a separate loopback-only testing authentication handler with the non-secret credentials `fixture-alice` and `fixture-bob`.

`MapUhp` creates the host-selected deployment prefix and invokes only this assembly's source-generated Valiant registrar, so unrelated host endpoints are not mapped under the UHP prefix. Every UHP endpoint belongs to the `[ValiantEndpointGroup]` class `UhpEndpointGroup`, which owns the shared metadata and protocol filter. Valiant's group mapping policies and conventions therefore apply to UHP as well as its endpoint policies and conventions. Endpoint classes are internal and map named handlers returning standard typed results such as `Ok<UhpHarnessModels>`. Shared version, authentication and UHP error handling run in the group's endpoint filter. Public protocol models have source-generated JSON metadata and wire-preserving converters, so handlers return models directly. Discovery remains anonymous. Unrelated application routes retain their own policies and responses.

Valiant Endpoints `1.2.1` fixes the unused reflection-based query binder being retained by generated registration. Without query-binding opt-in, its generator now selects a mapping path with no reference to that binder while retaining policies and conventions. UHP has no query-bound request models and no manual endpoint list or registration adapter. No trimming warnings are suppressed; the packed server consumer verifies automatic registration under Native AOT. Both Valiant dependencies restore from NuGet.org.

The server exposes discovery, harness/model catalogs, `POST /v1/responses`, response lookup, response cancellation, and session cancellation. Task submission returns the standard union of an `Ok<UhpResponse>` or a `ServerSentEventsResult<JsonElement>`. SSE payloads retain their complete protocol JSON, including unknown fields. Task body parsing remains explicit to keep malformed-body errors inside the UHP envelope. Generated Valiant validation checks task budgets and translates failures to UHP field names. There is no separate domain DTO layer requiring a mapper.

## Backend contract

The backend must own all accepted executions outside HTTP request scopes. `SubmitAsync` receives a principal/version/key context and the host shutdown token. It returns `UhpExecution` with an initial response, independent completion task, and a replayable progress subscription. Cancellation of a progress subscription detaches that reader only. It must never cancel or start execution. Non-streaming clients wait on the independent completion task.

The backend owns:

- Principal-scoped harnesses, responses, sessions and idempotency keys. Reads, continuations, and both cancellation scopes return 404 for another principal's object.
- Atomic acceptance, persistent retention when requested, session continuation, and `session_busy` concurrency enforcement. Store the response before readers need to recover it.
- A replayable progress history from `response.created` at sequence zero through contiguous events and exactly one terminal event. Acceptance and subscription must not lose events when execution completes immediately. Bound slow-reader buffering and retention in the host.
- Output-item ordering and tool `call_id` correlation. Tools execute on the server. Lifecycle events keep one response ID; terminal events carry the complete terminal snapshot and preserve partial output. Cancellation is represented by `response.failed` with embedded status `cancelled`.
- Explicit remote cancellation and idempotent cancellation of terminal work. An acknowledgement may precede the final response. HTTP disconnects, local timeouts and request-scope disposal leave work running.
- Reporting every `UhpSubmission.IgnoredFields` entry under the stored response's `metadata.ignored_fields`; reserved `tools`/`include` never execute. Report model fallback and ignored budgets as required by the specification.
- Task idempotency only when explicitly advertised. Scope keys to the principal, atomically deduplicate simultaneous identical submissions, and reject changed requests under a reused key.

The transport validates event sequences/response IDs, writes and flushes SSE progressively, checks terminal snapshots, and disables response buffering. It does not implement a production store, queue, agent runtime, or billing system. `UhpServerException` maps a safe `UhpError` and HTTP status; unexpected backend failures return a generic 500 without exception bodies or task content.

Dates and required Core capabilities are validated during registration. Optional features are not advertised by default; the preview rejects advertising endpoints it does not implement. `MaximumRequestBodyBytes` defaults to 1 MiB and JSON depth is bounded to 64. The host must also apply appropriate connection and request limits.

The sample `Uhp.FixtureServer` is a deterministic in-memory protocol fixture. Its fixed event histories, retention and testing credentials are not production implementations. Passing its tests does not establish a UHP server conformance class; upstream conformance applies to a running server and its actual backend.
