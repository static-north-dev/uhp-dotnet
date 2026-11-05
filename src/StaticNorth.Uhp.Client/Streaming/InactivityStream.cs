namespace StaticNorth.Uhp.Client.Streaming;

internal sealed class InactivityStream(Stream inner, TimeSpan? timeout) : Stream
{
    private CancellationTokenSource? _deadline;
    private CancellationToken _linkedToken;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (timeout is null)
        {
            return await inner
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_deadline is null || _linkedToken != cancellationToken || _deadline.IsCancellationRequested)
        {
            _deadline?.Dispose();
            _deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _linkedToken = cancellationToken;
        }

        var deadline = _deadline;
        deadline.CancelAfter(timeout.Value);

        try
        {
            return await inner
                .ReadAsync(buffer, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("No bytes arrived before the stream inactivity timeout.", exception);
        }
        finally
        {
            if (!deadline.IsCancellationRequested)
            {
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deadline?.Dispose();
        }

        base.Dispose(disposing);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
