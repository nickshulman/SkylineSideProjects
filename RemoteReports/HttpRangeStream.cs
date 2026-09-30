using System.Net;
using System.Net.Http.Headers;

namespace RemoteReports;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> over a remote file, backed by HTTP Range requests.
/// Lets <see cref="System.IO.Compression.ZipArchive"/> read just the central directory and the
/// entries that are opened, instead of downloading the whole archive.
///
/// Besides the tail of the file (see <see cref="_tail"/>), data is held in a single cached window. A read that continues where the previous window ended
/// is treated as sequential, and the next window doubles in size (up to <see cref="MaxChunkSize"/>),
/// so streaming a large entry takes a handful of requests rather than one per 8KB decompressor read.
/// A seek elsewhere resets the window size to <see cref="MinChunkSize"/>.
/// </summary>
public sealed class HttpRangeStream : Stream
{
    public const int MinChunkSize = 64 * 1024;
    public const int MaxChunkSize = 8 * 1024 * 1024;

    private readonly HttpClient _client;
    private readonly Uri _uri;
    private readonly long _length;

    // The last 64KB of the file, fetched on open and kept for the life of the stream: it holds a zip's
    // central directory, which ZipArchive returns to, and often small entries such as the .sky itself.
    private readonly long _tailStart;
    private readonly byte[] _tail;

    private long _position;
    private long _windowStart;
    private byte[] _window;
    private int _nextChunkSize = MinChunkSize;

    public long RequestCount { get; private set; }
    public long BytesFetched { get; private set; }

    private HttpRangeStream(HttpClient client, Uri uri, long length, long tailStart, byte[] tail)
    {
        _client = client;
        _uri = uri;
        _length = length;
        _tailStart = _windowStart = tailStart;
        _tail = _window = tail;
    }

    /// <summary>
    /// Opens the remote file. The length comes from the Content-Range of an initial ranged GET for
    /// the tail of the file (HEAD is not reliable; LabKey WebDAV answers it with 401 for anonymous
    /// users).
    /// </summary>
    public static async Task<HttpRangeStream> OpenAsync(HttpClient client, Uri uri, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue { Ranges = { new RangeItemHeaderValue(null, MinChunkSize) } };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureRangeResponse(response, uri);

        var contentRange = response.Content.Headers.ContentRange!;
        long length = contentRange.Length
            ?? throw new IOException($"Server did not report the total length of {uri}");
        long start = contentRange.From ?? 0;
        byte[] tail = await response.Content.ReadAsByteArrayAsync(ct);

        return new HttpRangeStream(client, uri, length, start, tail)
        {
            RequestCount = 1,
            BytesFetched = tail.Length,
        };
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(newPosition, nameof(offset));
        _position = newPosition;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!TryPrepareRead(buffer.Length, out long fetchStart, out int fetchLength))
            return 0;
        if (fetchLength > 0)
            SetWindow(fetchStart, FetchAsync(fetchStart, fetchLength, sync: true, default).GetAwaiter().GetResult());
        return CopyFromWindow(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (!TryPrepareRead(buffer.Length, out long fetchStart, out int fetchLength))
            return 0;
        if (fetchLength > 0)
            SetWindow(fetchStart, await FetchAsync(fetchStart, fetchLength, sync: false, ct));
        return CopyFromWindow(buffer.Span);
    }

    /// <summary>
    /// Returns false at end of stream. Otherwise says what to fetch, with a length of 0 when the
    /// current position is already inside the cached window.
    /// </summary>
    private bool TryPrepareRead(int count, out long fetchStart, out int fetchLength)
    {
        fetchStart = _position;
        fetchLength = 0;
        if (count == 0 || _position >= _length)
            return false;
        if (_position >= _windowStart && _position < _windowStart + _window.Length)
            return true;
        if (_position >= _tailStart)
        {
            SetWindow(_tailStart, _tail);
            return true;
        }

        bool sequential = _position == _windowStart + _window.Length;
        _nextChunkSize = sequential ? Math.Min(_nextChunkSize * 2, MaxChunkSize) : MinChunkSize;
        // Stop at the cached tail rather than fetching it again.
        fetchLength = (int)Math.Min(Math.Max(count, _nextChunkSize), _tailStart - _position);
        return true;
    }

    private void SetWindow(long start, byte[] data)
    {
        _windowStart = start;
        _window = data;
    }

    private int CopyFromWindow(Span<byte> buffer)
    {
        int offsetInWindow = (int)(_position - _windowStart);
        int n = Math.Min(buffer.Length, _window.Length - offsetInWindow);
        _window.AsSpan(offsetInWindow, n).CopyTo(buffer);
        _position += n;
        return n;
    }

    private async Task<byte[]> FetchAsync(long start, int length, bool sync, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(start, start + length - 1);

        using var response = sync
            ? _client.Send(request, HttpCompletionOption.ResponseHeadersRead, ct)
            : await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureRangeResponse(response, _uri);
        // LabKey's ETags are weak, which If-Range does not allow, so a changed total length is the
        // best available sign that the file was replaced while we were reading it.
        var contentRange = response.Content.Headers.ContentRange!;
        if (contentRange.Length != _length || contentRange.From != start)
            throw new IOException($"Unexpected Content-Range '{contentRange}' for {_uri}; the file may have changed");

        var data = new byte[length];
        await using (var body = sync ? response.Content.ReadAsStream(ct) : await response.Content.ReadAsStreamAsync(ct))
        {
            if (sync)
                body.ReadExactly(data);
            else
                await body.ReadExactlyAsync(data, ct);
        }

        RequestCount++;
        BytesFetched += length;
        return data;
    }

    private static void EnsureRangeResponse(HttpResponseMessage response, Uri uri)
    {
        if (response.StatusCode == HttpStatusCode.OK)
            throw new IOException($"Server ignored the Range request for {uri}");
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange == null)
            throw new IOException($"Unexpected response {(int)response.StatusCode} to a Range request for {uri}");
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
