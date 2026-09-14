using System;
using System.IO;
using AlegacyWebPanel.Modules.FileManager.Exceptions;

namespace AlegacyWebPanel.Modules.FileManager.Services;

public sealed class BoundedStream(Stream innerStream, long maxLength) : Stream
{
    private long _bytesRead;

    public override bool CanRead => innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => innerStream.Length;
    public override long Position 
    { 
        get => innerStream.Position; 
        set => throw new NotSupportedException(); 
    }

    public override void Flush() => innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = innerStream.Read(buffer, offset, count);
        TrackBytes(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        TrackBytes(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await innerStream.ReadAsync(buffer, cancellationToken);
        TrackBytes(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void TrackBytes(int bytes)
    {
        if (bytes <= 0) return;
        _bytesRead += bytes;
        if (_bytesRead > maxLength)
        {
            throw new FileTooLargeException($"File size exceeds the limit of {maxLength} bytes.");
        }
    }
}
