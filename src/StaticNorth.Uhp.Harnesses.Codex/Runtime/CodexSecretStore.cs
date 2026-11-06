using System.Security.Cryptography;
using System.Text;

namespace StaticNorth.Uhp.Harnesses.Codex;

internal sealed class CodexSecretStore : IDisposable
{
    private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

    public string Store(string value)
    {
        var reference = $"secret_{Guid.NewGuid():N}";
        _values.Add(reference, Encoding.UTF8.GetBytes(value));
        return reference;
    }

    public string? ConsumeFingerprint(string reference)
    {
        if (!_values.Remove(reference, out var bytes))
        {
            return null;
        }

        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Dispose()
    {
        foreach (var bytes in _values.Values)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        _values.Clear();
    }
}
