using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server.Transport;

internal sealed class LimitedReadStream(Stream inner, long maximumBytes, string errorCode = UhpProtocol.InvalidInput) : Stream
{
    private long _received;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = await inner
            .ReadAsync(buffer[..(int)Math.Min(buffer.Length, maximumBytes - _received + 1)], cancellationToken)
            .ConfigureAwait(false);
        _received += count;

        if (_received > maximumBytes)
        {
            throw new UhpServerException(413, new(UhpProtocol.InvalidRequestError, errorCode, "The request body exceeds the configured size limit.")
            {
                Detail = UhpJson.CreateElement(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("max_bytes", maximumBytes);
                    writer.WriteEndObject();
                })
            });
        }

        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
