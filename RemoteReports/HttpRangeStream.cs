using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace RemoteReports;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> over a remote file, backed by HTTP Range requests.
/// Lets <see cref="System.IO.Compression.ZipArchive"/> read just the central directory and the
/// entries that are opened, instead of downloading the whole archive.
///
/// Besides the tail of the file (see <see cref="_tail"/>), a random-access read fetches a small cached
/// window of <see cref="WindowSize"/> bytes. A read that continues where the previous one ended is
/// treated as sequential: it opens one request running up to the tail and keeps reading its response,
/// so streaming a large entry pays the request latency (~0.3s on Panorama) once rather than per chunk.
/// If that response breaks or stalls, it is reopened at the current position.
/// </summary>
public sealed class HttpRangeStream : Stream
{
    public const int WindowSize = 64 * 1024;
    /// <summary>How many times in a row a sequential read tries to (re)open its response.</summary>
    private const int MaxAttempts = 5;
    /// <summary>How long a read of the open response may wait for data before reconnecting.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

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

    // The response being streamed for sequential reads, which next delivers the byte at _bodyPosition.
    private HttpResponseMessage? _bodyResponse;
    private Stream? _body;
    private long _bodyPosition;

    public long RequestCount { get; private set; }
    public long BytesFetched { get; private set; }
    /// <summary>Times a streamed response broke or stalled and was reopened.</summary>
    public int Reconnects { get; private set; }

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
        request.Headers.Range = new RangeHeaderValue { Ranges = { new RangeItemHeaderValue(null, WindowSize) } };
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
        // The open response, if any, is kept: a read back at _bodyPosition picks it up again.
        _position = newPosition;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCoreAsync(buffer.AsMemory(offset, count), sync: true, default).AsTask().GetAwaiter().GetResult();

    public override int Read(Span<byte> buffer)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int n = Read(rented, 0, buffer.Length);
            rented.AsSpan(0, n).CopyTo(buffer);
            return n;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadCoreAsync(buffer.AsMemory(offset, count), sync: false, ct).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        ReadCoreAsync(buffer, sync: false, ct);

    /// <summary>
    /// With <paramref name="sync"/>, requests are sent synchronously; reads of a streamed response
    /// are async either way, so that they can time out, and a sync caller blocks on them.
    /// </summary>
    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, bool sync, CancellationToken ct)
    {
        if (buffer.Length == 0 || _position >= _length)
            return 0;
        if (_position < _windowStart || _position >= _windowStart + _window.Length)
        {
            if (_position >= _tailStart)
            {
                SetWindow(_tailStart, _tail);
            }
            else if (_body != null && _position == _bodyPosition || _position == _windowStart + _window.Length)
            {
                return await ReadBodyAsync(buffer, sync, ct);
            }
            else
            {
                CloseBody();
                // Stop at the cached tail rather than fetching it again.
                int fetchLength = (int)Math.Min(Math.Max(buffer.Length, WindowSize), _tailStart - _position);
                SetWindow(_position, await FetchAsync(_position, fetchLength, sync, ct));
            }
        }
        return CopyFromWindow(buffer.Span);
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

    /// <summary>Reads at the current position from the streamed response, opening it if needed.</summary>
    private async ValueTask<int> ReadBodyAsync(Memory<byte> buffer, bool sync, CancellationToken ct)
    {
        int count = (int)Math.Min(buffer.Length, _tailStart - _position);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (_body == null || _bodyPosition != _position)
                {
                    CloseBody();
                    await OpenBodyAsync(sync, ct);
                }
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);
                int n = await _body!.ReadAsync(buffer[..count], stall.Token);
                if (n == 0)
                    throw new IOException($"The response from {_uri} ended early at byte {_position:N0}");
                _position += n;
                _bodyPosition = _position;
                BytesFetched += n;
                return n;
            }
            catch (Exception e) when (attempt < MaxAttempts && !ct.IsCancellationRequested && IsTransient(e))
            {
                CloseBody();
                Reconnects++;
                var delay = TimeSpan.FromSeconds(attempt);
                if (sync)
                    Thread.Sleep(delay);
                else
                    await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// A broken or stalled connection, or a server error, as opposed to a refusal or a changed file.
    /// </summary>
    private static bool IsTransient(Exception e) => e switch
    {
        RangeResponseException => false,
        HttpRequestException h => h.StatusCode is null or >= HttpStatusCode.InternalServerError,
        IOException or OperationCanceledException => true,
        _ => false,
    };

    private async Task OpenBodyAsync(bool sync, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(_position, _tailStart - 1);
        var response = sync
            ? _client.Send(request, HttpCompletionOption.ResponseHeadersRead, ct)
            : await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            EnsureRangeResponse(response, _uri);
            CheckContentRange(response, _position);
            _body = sync ? response.Content.ReadAsStream(ct) : await response.Content.ReadAsStreamAsync(ct);
        }
        catch
        {
            response.Dispose();
            throw;
        }
        _bodyResponse = response;
        _bodyPosition = _position;
        RequestCount++;
    }

    private void CloseBody()
    {
        // Disposing a response that has not been read to the end closes its connection.
        _body?.Dispose();
        _bodyResponse?.Dispose();
        _body = null;
        _bodyResponse = null;
    }

    private async Task<byte[]> FetchAsync(long start, int length, bool sync, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(start, start + length - 1);

        using var response = sync
            ? _client.Send(request, HttpCompletionOption.ResponseHeadersRead, ct)
            : await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureRangeResponse(response, _uri);
        CheckContentRange(response, start);

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

    /// <summary>
    /// LabKey's ETags are weak, which If-Range does not allow, so a changed total length is the
    /// best available sign that the file was replaced while we were reading it.
    /// </summary>
    private void CheckContentRange(HttpResponseMessage response, long start)
    {
        var contentRange = response.Content.Headers.ContentRange!;
        if (contentRange.Length != _length || contentRange.From != start)
            throw new RangeResponseException($"Unexpected Content-Range '{contentRange}' for {_uri}; the file may have changed");
    }

    private static void EnsureRangeResponse(HttpResponseMessage response, Uri uri)
    {
        if (response.StatusCode == HttpStatusCode.OK)
            throw new RangeResponseException($"Server ignored the Range request for {uri}");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(
                $"Access denied ({(int)response.StatusCode} {response.StatusCode}) for {uri.AbsoluteUri}",
                null, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange == null)
            throw new RangeResponseException($"Unexpected response {(int)response.StatusCode} to a Range request for {uri}");
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CloseBody();
        base.Dispose(disposing);
    }

    /// <summary>A response that retrying will not fix: the server cannot do ranges, or the file changed.</summary>
    private sealed class RangeResponseException(string message) : IOException(message);
}
