using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Reference;

namespace StaticNorth.Uhp.FixtureServer;

public sealed class FixtureRuntime : IReferenceRuntime
{
    public string Base => "fixture";
    public string DefaultModel => "fixture-model";
    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "uhp-fixture");

    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct) => Task.FromResult(new UhpModelCatalog(new Dictionary<string, UhpModelBackend>
    {
        [Base] = new(DefaultModel, [new UhpModel(DefaultModel, true) { IsDefault = true, Backend = Base }])
    }));

    public async Task<ReferenceRuntimeResult> RunAsync(ReferenceRuntimeRequest request, Func<string, ValueTask> textDelta, CancellationToken ct)
    {
        await textDelta("Fixture response.")
            .ConfigureAwait(false);
        var input = request.Task.Input.ToString();
        var hold = input.Contains("hold", StringComparison.OrdinalIgnoreCase);
        var slow = input.Contains("slow", StringComparison.OrdinalIgnoreCase);
        await Task
            .Delay(hold ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(slow ? 400 : 40), ct)
            .ConfigureAwait(false);
        return new(DefaultModel, UhpResponseStatus.Completed);
    }
}
