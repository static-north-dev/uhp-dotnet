# Agent instructions

## Protocol documentation

- The [official Unified Harness Protocol documentation](https://unifiedharnessprotocol.org/) is the source of truth for protocol behavior, schemas, and conformance requirements.
- Consult the relevant official specification before implementing or changing protocol behavior.

## Coding conventions

- Use `var` for local variables when the compiler can infer the intended type without changing behavior.
- Format fluent method chains with the receiver on its own line and each chained call on a separate, indented line, including the first call.
- Always use full braced blocks for conditionals and loops, including single-statement bodies.
- Add empty lines between logical code blocks.
- Prioritize clean, readable code and a performance-first approach.
- Use the latest language and framework features.
- Use `StaticNorth.Uhp.*` project, package, assembly, and namespace names. Preserve the protocol's wire names and fields.
- All I/O must use asynchronous APIs with cancellation propagated end to end. Keep CPU-only work synchronous; do not simulate asynchronous I/O with `Task.Run`, blocking waits, `.Result`, or `.GetAwaiter().GetResult()`.
- Native AOT compatibility is mandatory. Use source-generated JSON metadata, Valiant registration/validation, and TUnit discovery wherever supported. Keep reflection-based serialization disabled in consumers and never suppress trimming/AOT warnings to pass verification.
- Zero avoidable allocations in repeated hot paths is mandatory. Remove temporary strings, arrays, closures, boxed enumerators, and intermediate serialization documents where the API does not require ownership. Measure required HTTP/payload/buffer allocations with warmed allocation tests and record their budgets; never claim that the entire SDK allocates zero bytes.
- Use indexed loops for hot `IReadOnlyList<T>` paths and concrete dictionary enumerators where available. Do not replace these with interface-based `foreach` loops without allocation measurements.
- Use shared constants for repeated protocol headers, routes, media types, and other meaningful repeated values. Keep constants with their owning protocol or component.
- Write tests with TUnit's generated runner and asynchronous assertions. Preserve behavioral coverage, exact exception checks, data-driven cases, and truthful skipped interoperability reports.
- Do not use lambdas for endpoint handling, including lambdas passed to shared transport wrappers. Map a separate named handler method, such as `HandleAsync`, using a method group.
- Use standard ASP.NET Core `TypedResults` with concrete typed return values for endpoints. Put shared protocol checks in endpoint filters; do not wrap ordinary endpoint handlers in raw `RequestDelegate` callbacks or return serialized `JsonElement` in place of their response models.
- Create a separate file for each class and organize files into a logical folder structure.
- The only exception to the separate-file rule is an interface with a single implementation class: they may share one file, with the class first and the interface below it.
