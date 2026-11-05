namespace StaticNorth.Uhp.Reference.Memory;

internal readonly struct ReferenceMemoryLease(SemaphoreSlim gate) : IDisposable
{
    public void Dispose() => gate.Release();
}
