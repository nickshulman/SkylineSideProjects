using System.IO.Compression;
using Parquet;
using Parquet.Schema;

namespace RemoteReports;

/// <summary>
/// Writes <see cref="ReportRow"/>s to Parquet in the same shape Skyline's own report exporter produces:
/// invariant column names, every column OPTIONAL, DateTime as TIMESTAMP(MILLIS), Zstd.
/// Rows are buffered only up to one row group, so memory is bounded regardless of document size.
/// A full row group is encoded and compressed on a background task while the next one fills, so the
/// caller (and the threads producing rows for it) need not wait for it; that takes two buffers.
/// </summary>
public sealed class ParquetReportWriter : IAsyncDisposable
{
    /// <summary>
    /// Skyline sizes row groups at about 2.16M rows for the PRISM report; a smaller default keeps the
    /// buffered row group (~260 bytes per row) to a modest amount of memory.
    /// </summary>
    public const int DefaultRowsPerGroup = 500_000;

    private readonly IReadOnlyList<ReportColumn> _columns;
    private readonly ParquetWriter _writer;
    private ReportRow[] _buffer;
    // The buffer of the row group being written by _pendingWrite, and the buffer after that.
    private ReportRow[] _spareBuffer;
    private Task _pendingWrite = Task.CompletedTask;
    private int _count;

    public long RowsWritten { get; private set; }

    private ParquetReportWriter(IReadOnlyList<ReportColumn> columns, ParquetWriter writer, int rowsPerGroup)
    {
        _columns = columns;
        _writer = writer;
        _buffer = new ReportRow[rowsPerGroup];
        _spareBuffer = new ReportRow[rowsPerGroup];
    }

    public static async Task<ParquetReportWriter> CreateAsync(Stream output, IReadOnlyList<ReportColumn> columns,
        int rowsPerGroup = DefaultRowsPerGroup)
    {
        var schema = new ParquetSchema(columns.Select(c => (Field)c.Field).ToArray());
        // Same settings as Skyline's Parquet.Net 6 exporter: Zstd level 3 and dictionary encoding
        // for string columns, which are highly repetitive (protein, peptide, replicate, file).
        var options = new ParquetOptions
        {
            CompressionMethod = CompressionMethod.Zstd,
            CompressionLevel = CompressionLevel.Optimal,
            DictionaryEncodingSampleSize = 10000,
        };
        foreach (var column in columns.Where(c => c.IsString))
            options.ColumnEncodingHints[column.Field.Path.ToString()] = EncodingHint.Dictionary;

        var writer = await ParquetWriter.CreateAsync(schema, output, options);
        return new ParquetReportWriter(columns, writer, rowsPerGroup);
    }

    public async ValueTask AddAsync(ReportRow row)
    {
        _buffer[_count++] = row;
        if (_count == _buffer.Length)
            await StartRowGroupAsync();
    }

    /// <summary>
    /// Waits for the previous row group to be written, then starts writing the buffered rows on a
    /// background task and switches to the other buffer.
    /// </summary>
    private async Task StartRowGroupAsync()
    {
        await _pendingWrite;
        if (_count == 0)
            return;
        var rows = _buffer;
        int count = _count;
        _pendingWrite = Task.Run(() => WriteRowGroupAsync(rows, count));
        _buffer = _spareBuffer;
        _spareBuffer = rows;
        _count = 0;
    }

    private async Task WriteRowGroupAsync(ReportRow[] rows, int count)
    {
        using (var rowGroup = _writer.CreateRowGroup())
        {
            foreach (var column in _columns)
                await column.WriteAsync(rowGroup, rows, count);
            rowGroup.CompleteValidate();
        }
        RowsWritten += count;
        // Drop the strings so they can be collected before the buffer is reused.
        Array.Clear(rows, 0, count);
    }

    public async ValueTask DisposeAsync()
    {
        await StartRowGroupAsync();
        await _pendingWrite;
        await _writer.DisposeAsync();
    }
}
