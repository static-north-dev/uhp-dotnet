using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace StaticNorth.Uhp.ConformanceHost;

internal sealed class HostCredential(string token)
{
    public bool Matches(ReadOnlySpan<char> candidate) => CryptographicOperations.FixedTimeEquals(MemoryMarshal.AsBytes(candidate), MemoryMarshal.AsBytes(token.AsSpan()));
}
