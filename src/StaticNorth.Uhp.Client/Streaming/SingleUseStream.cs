using System.Runtime.CompilerServices;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Streaming;

internal sealed class SingleUseStream(
    Func<CancellationToken, IAsyncEnumerable<UhpEvent>> factory,
    CancellationToken methodToken)
    : IAsyncEnumerable<UhpEvent>
{
    private int _enumerated;

    public IAsyncEnumerator<UhpEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return Interlocked.Exchange(ref _enumerated, 1) != 0
            ? throw new InvalidOperationException("A task stream can only be enumerated once.")
            : Enumerate(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private async IAsyncEnumerable<UhpEvent> Enumerate([EnumeratorCancellation] CancellationToken enumerationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(methodToken, enumerationToken);
        linked.Token.ThrowIfCancellationRequested();

        await foreach (var update in factory(linked.Token).WithCancellation(linked.Token).ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
