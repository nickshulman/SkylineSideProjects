using System.Globalization;
using Parquet;
using Parquet.Schema;

namespace RemoteReports;

/// <summary>
/// Compares two Parquet files column by column (schema, then values by row position) and prints the
/// differences. Holds one row group of one column per file in memory at a time.
/// </summary>
public static class ParquetCompare
{
    public static async Task<int> RunAsync(string expectedPath, string actualPath, int maxDiffsPerColumn = 5)
    {
        await using var expectedStream = File.OpenRead(expectedPath);
        await using var actualStream = File.OpenRead(actualPath);
        await using var expected = await ParquetReader.CreateAsync(expectedStream);
        await using var actual = await ParquetReader.CreateAsync(actualStream);

        var expectedFields = expected.Schema.GetDataFields();
        var actualFields = actual.Schema.GetDataFields();
        int problems = 0;
        Console.WriteLine($"Rows: expected {await CountRows(expected):N0}, actual {await CountRows(actual):N0}; " +
                          $"row groups: expected {expected.RowGroupCount}, actual {actual.RowGroupCount}");

        for (int i = 0; i < Math.Max(expectedFields.Length, actualFields.Length); i++)
        {
            var e = i < expectedFields.Length ? expectedFields[i] : null;
            var a = i < actualFields.Length ? actualFields[i] : null;
            if (Describe(e) != Describe(a))
            {
                Console.WriteLine($"Schema column {i}: expected {Describe(e)}, actual {Describe(a)}");
                problems++;
            }
        }

        foreach (var e in expectedFields)
        {
            var a = actualFields.FirstOrDefault(f => f.Name == e.Name);
            if (a == null)
                continue;
            // The files' row groups need not line up, so each side is read one row group at a time.
            var expectedValues = new ColumnCursor(expected, e);
            var actualValues = new ColumnCursor(actual, a);
            long diffs = 0;
            for (long row = 0; ; row++)
            {
                var (eHas, ev) = await expectedValues.NextAsync();
                var (aHas, av) = await actualValues.NextAsync();
                if (!eHas && !aHas)
                    break;
                if (!eHas)
                    ev = "<missing row>";
                if (!aHas)
                    av = "<missing row>";
                if (ev == av)
                    continue;
                if (diffs < maxDiffsPerColumn)
                    Console.WriteLine($"  {e.Name} row {row}: expected {ev ?? "null"}, actual {av ?? "null"}");
                diffs++;
            }
            if (diffs > 0)
            {
                Console.WriteLine($"{e.Name}: {diffs:N0} differing rows");
                problems++;
            }
        }
        Console.WriteLine(problems == 0 ? "Files match." : $"{problems} column(s) differ.");
        return problems == 0 ? 0 : 1;
    }

    private static string Describe(DataField? f) =>
        f == null ? "<none>" : $"{f.Name} {f.ClrType.Name}{(f.IsNullable ? "?" : "")}" + (f is DateTimeDataField d
            ? $" ({d.DateTimeFormat}{(d.Unit is { } unit ? " " + unit : "")}{(d.IsAdjustedToUTC ? " UTC" : "")})"
            : "");

    private static async Task<long> CountRows(ParquetReader reader)
    {
        long rows = 0;
        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using var rowGroup = reader.OpenRowGroupReader(g);
            rows += rowGroup.RowCount;
        }
        return rows;
    }

    /// <summary>Reads one column's values across row groups, one row group at a time.</summary>
    private sealed class ColumnCursor(ParquetReader reader, DataField field)
    {
        private int _nextGroup;
        private string?[] _values = [];
        private int _index;

        public async ValueTask<(bool HasValue, string? Value)> NextAsync()
        {
            while (_index >= _values.Length)
            {
                if (_nextGroup >= reader.RowGroupCount)
                    return (false, null);
                using var rowGroup = reader.OpenRowGroupReader(_nextGroup++);
                _values = await ReadRowGroup(rowGroup, field);
                _index = 0;
            }
            return (true, _values[_index++]);
        }
    }

    /// <summary>Reads a column chunk as round-trip strings, so values of any type compare exactly.</summary>
    private static async Task<string?[]> ReadRowGroup(ParquetRowGroupReader rowGroup, DataField field)
    {
        int n = (int)rowGroup.RowCount;
        var t = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        if (t == typeof(string) || t == typeof(ReadOnlyMemory<char>))
        {
            var values = new string?[n];
            await rowGroup.ReadAsync(field, values.AsMemory());
            return values;
        }
        if (t == typeof(double)) return await Read<double>(rowGroup, field, n, v => v.ToString("R", CultureInfo.InvariantCulture));
        if (t == typeof(float)) return await Read<float>(rowGroup, field, n, v => v.ToString("R", CultureInfo.InvariantCulture));
        if (t == typeof(int)) return await Read<int>(rowGroup, field, n, v => v.ToString(CultureInfo.InvariantCulture));
        if (t == typeof(long)) return await Read<long>(rowGroup, field, n, v => v.ToString(CultureInfo.InvariantCulture));
        if (t == typeof(bool)) return await Read<bool>(rowGroup, field, n, v => v.ToString());
        // Just the stored digits: the reader sets Kind from the column's encoding (whether the TIMESTAMP
        // is adjusted to UTC), which the schema comparison already covers.
        if (t == typeof(DateTime)) return await Read<DateTime>(rowGroup, field, n, v => v.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        throw new NotSupportedException($"Column {field.Name} has unsupported type {field.ClrType}");
    }

    private static async Task<string?[]> Read<T>(ParquetRowGroupReader rowGroup, DataField field, int n, Func<T, string> format)
        where T : struct
    {
        var strings = new string?[n];
        if (field.IsNullable)
        {
            var values = new T?[n];
            await rowGroup.ReadAsync(field, values.AsMemory());
            for (int i = 0; i < n; i++)
                strings[i] = values[i].HasValue ? format(values[i]!.Value) : null;
        }
        else
        {
            var values = new T[n];
            await rowGroup.ReadAsync(field, values.AsMemory());
            for (int i = 0; i < n; i++)
                strings[i] = format(values[i]);
        }
        return strings;
    }
}
