# Agent instructions

## Protocol documentation

- The [official Unified Harness Protocol documentation](https://unifiedharnessprotocol.org/) is the source of truth for protocol behavior, schemas, and conformance requirements.
- Consult the relevant official specification before implementing or changing protocol behavior.

## Coding conventions

- Always use full braced blocks for conditionals and loops, including single-statement bodies.
- Add empty lines between logical code blocks.
- Prioritize clean, readable code and a performance-first approach.
- Use the latest language and framework features.
- Do not use lambdas for endpoint handling, including lambdas passed to shared transport wrappers. Map a separate named handler method, such as `HandleAsync`, using a method group.
- Use standard ASP.NET Core `TypedResults` with concrete typed return values for endpoints. Put shared protocol checks in endpoint filters; do not wrap ordinary endpoint handlers in raw `RequestDelegate` callbacks or return serialized `JsonElement` in place of their response models.
- Create a separate file for each class and organize files into a logical folder structure.
- The only exception to the separate-file rule is an interface with a single implementation class: they may share one file, with the class first and the interface below it.
