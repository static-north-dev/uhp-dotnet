using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Harnesses.Codex;

internal sealed class CodexRuntimeSession(CodexAppServerConnection connection, string threadId, string model, string home, List<CodexMcpBinding> bindings)
{
    public CodexAppServerConnection Connection { get; } = connection;
    public string ThreadId { get; set; } = threadId;
    public string Model { get; } = model;
    public string Home { get; } = home;
    public List<CodexMcpBinding> Bindings { get; } = bindings;
    public CodexSecretStore Secrets { get; } = new();
    public bool Executing { get; set; }
    public bool MemoryPrimed { get; set; }
    public IUhpHarnessEnvironment? Environment { get; set; }
}
