using System.Text;

namespace StaticNorth.Uhp.Client.Tests;

internal sealed class PacketStream(string[] packets, int chunkSize = int.MaxValue, int delayMilliseconds = 0, bool blockAtEnd = false, bool failAtEnd = false) : Stream
{
    private readonly byte[][] _packets = packets
        .Select(Encoding.UTF8.GetBytes)
        .ToArray();
    private int _packet;
    private int _offset;

    public bool WasDisposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_packet == _packets.Length)
        {
            if (blockAtEnd)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (failAtEnd)
            {
                throw new IOException("Interrupted transport.");
            }

            return 0;
        }

        if (_offset == 0 && delayMilliseconds > 0)
        {
            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        var packet = _packets[_packet];
        var count = Math.Min(Math.Min(buffer.Length, packet.Length - _offset), chunkSize);
        packet
            .AsMemory(_offset, count)
            .CopyTo(buffer);
        _offset += count;

        if (_offset == packet.Length)
        {
            _packet++;
            _offset = 0;
        }

        return count;
    }

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
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
