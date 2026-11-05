namespace StaticNorth.Uhp.Reference.Environments;

internal readonly struct ReferenceEnvironmentLease(SemaphoreSlim? gate) : IDisposable
{
    public void Dispose() => gate?.Release();
}
