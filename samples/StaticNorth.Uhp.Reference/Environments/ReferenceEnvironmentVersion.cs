using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

internal sealed class ReferenceEnvironmentVersion(string root, Wire.EnvironmentBuild build)
{
    public string Root { get; } = root;
    public Wire.EnvironmentBuild Build { get; set; } = build;
    public Task Completion { get; set; } = Task.CompletedTask;
}
