namespace Uhp.Client;

public sealed class UhpClientOptions
{
    public string? ProtocolVersion { get; init; }
    public Func<CancellationToken, ValueTask<string>>? GetBearerTokenAsync { get; init; }
    public TimeSpan? StreamInactivityTimeout { get; init; }
}
