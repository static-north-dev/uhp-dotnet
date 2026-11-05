namespace StaticNorth.Uhp.Server;

public interface IUhpSharedViewBackend
{
    Task<UhpSharedSessionView> GetSharedViewAsync(string shareId, CancellationToken ct);
}
