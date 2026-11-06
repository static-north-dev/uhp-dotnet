namespace StaticNorth.Uhp.Core.Providers;

/// <summary>A read-only environment the host keeps mounted for the duration of a turn.</summary>
public interface IUhpHarnessEnvironment
{
    string Id { get; }
    long Version { get; }
    string Root { get; }
    string Mount { get; }
    string? Entry { get; }
}
