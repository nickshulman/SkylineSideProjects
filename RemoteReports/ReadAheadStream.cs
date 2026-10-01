using System.IO.Pipelines;

namespace RemoteReports;

/// <summary>
/// Reads another stream on a dedicated thread, ahead of the consumer, buffering up to
/// <see cref="MaxBufferedBytes"/>. For a zip entry on a server, that thread makes the HTTP requests
/// and inflates the data, so downloading overlaps with parsing instead of alternating with it.
/// Takes ownership of the source stream.
/// </summary>
public sealed class ReadAheadStream : Stream
{
    public const int MaxBufferedBytes = 64 * 1024 * 1024;
    private const int ReadSize = 256 * 1024;

    private readonly Stream _source;
    private readonly Pipe _pipe = new(new PipeOptions(pauseWriterThreshold: MaxBufferedBytes,
        resumeWriterThreshold: MaxBufferedBytes / 2, useSynchronizationContext: false));
    private readonly Stream _reader;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Thread _thread;

    public ReadAheadStream(Stream source)
    {
        _source = source;
        _reader = _pipe.Reader.AsStream();
        _thread = new Thread(Download) { IsBackground = true, Name = "Download" };
        _thread.Start();
    }

    private void Download()
    {
        var writer = _pipe.Writer;
        try
        {
            while (true)
            {
                int n = _source.Read(writer.GetMemory(ReadSize).Span);
                if (n == 0)
                    break;
                writer.Advance(n);
                // Blocks while the buffer is full; completes as "IsCompleted" if the reader has gone away.
                if (writer.FlushAsync(_cancel.Token).AsTask().GetAwaiter().GetResult().IsCompleted)
                    break;
            }
            writer.Complete();
        }
        catch (Exception e)
        {
            // The reader rethrows this from its next Read.
            writer.Complete(e);
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _reader.Read(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        _reader.ReadAsync(buffer, offset, count, ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        _reader.ReadAsync(buffer, ct);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Stop the download: a blocked flush is canceled, and the next one sees the reader is done.
            // A request already in progress finishes first.
            _cancel.Cancel();
            _pipe.Reader.Complete();
            _thread.Join();
            _source.Dispose();
            _cancel.Dispose();
        }
        base.Dispose(disposing);
    }
}
