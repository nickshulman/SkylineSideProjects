using System.IO.Compression;
using Parquet;
using Parquet.Schema;

namespace RemoteReports;

/// <summary>
/// Writes <see cref="ReportRow"/>s to Parquet in the same shape Skyline's own report exporter produces:
/// invariant column names, every column OPTIONAL, DateTime as INT96, Zstd.
/// Rows are buffered only up to one row group, so memory is bounded regardless of document size.
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
    private readonly ReportRow[] _buffer;
    private int _count;

    public long RowsWritten { get; private set; }

    private ParquetReportWriter(IReadOnlyList<ReportColumn> columns, ParquetWriter writer, int rowsPerGroup)
    {
        _columns = columns;
        _writer = writer;
        _buffer = new ReportRow[rowsPerGroup];
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
            await FlushRowGroupAsync();
    }

    private async Task FlushRowGroupAsync()
    {
        if (_count == 0)
            return;
        using (var rowGroup = _writer.CreateRowGroup())
        {
            foreach (var column in _columns)
                await column.WriteAsync(rowGroup, _buffer, _count);
            rowGroup.CompleteValidate();
        }
        RowsWritten += _count;
        Array.Clear(_buffer, 0, _count);
        _count = 0;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushRowGroupAsync();
        await _writer.DisposeAsync();
    }
}
