using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Reference.Environments;

public sealed class ReferenceEnvironmentSelection : IUhpHarnessEnvironment
{
    private readonly ReferenceEnvironmentNode _node;
    internal ReferenceEnvironmentSelection(string id, long version, string root, string mount, string? entry, ReferenceEnvironmentNode node)
    {
        Id = id; Version = version; Root = root; Mount = mount; Entry = entry; _node = node;
    }

    public string Id { get; }
    public long Version { get; }
    public string Root { get; }
    public string Mount { get; }
    public string? Entry { get; }

    internal async ValueTask<ReferenceEnvironmentLease> AcquireAsync(CancellationToken ct)
    {
        await _node.ExecutionGate
            .WaitAsync(ct)
            .ConfigureAwait(false);

        try
        {
            if (_node.Deleting)
            {
                throw ReferenceBackend.Failure(404, "environment_not_found", "The environment has been deleted.");
            }

            if (_node.MountedVersion != Version)
            {
                var temporary = Mount + ".staging";

                try
                {
                    await ReferenceEnvironmentBackend
                        .PublishSnapshotAsync(Root, temporary, ct)
                        .ConfigureAwait(false);

                    if (Directory.Exists(Mount))
                    {
                        Directory.Delete(Mount, true);
                    }

                    Directory.Move(temporary, Mount);
                    _node.MountedVersion = Version;
                }
                finally
                {
                    if (Directory.Exists(temporary))
                    {
                        Directory.Delete(temporary, true);
                    }
                }
            }

            return new(_node.ExecutionGate);
        }
        catch
        {
            _node.ExecutionGate.Release();
            throw;
        }
    }
}
