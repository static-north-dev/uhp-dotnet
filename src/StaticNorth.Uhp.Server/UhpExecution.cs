using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server;

/// <summary>An accepted execution owned by the backend, independently of HTTP request lifetimes.</summary>
/// <remarks>Subscribe must replay from response.created through the terminal event for each reader. Reader cancellation only detaches that reader.</remarks>
public sealed record UhpExecution(UhpResponse InitialResponse, Task<UhpResponse> Completion, Func<CancellationToken, IAsyncEnumerable<UhpEvent>> Subscribe);
