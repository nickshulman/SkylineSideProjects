using Parquet;
using Parquet.Schema;

namespace RemoteReports;

/// <summary>
/// A report column this tool knows how to produce: the Skyline property path used in .skyr files,
/// the invariant column name Skyline's Parquet exporter writes, and how to write it.
/// </summary>
/// <summary>Reads a value from a row in place; <see cref="ReportRow"/> is too big a struct to copy per column.</summary>
public delegate T RowGetter<out T>(in ReportRow row);

public sealed class ReportColumn
{
    public required string PropertyPath { get; init; }
    public required DataField Field { get; init; }
    public bool IsString { get; private init; }
    public required Func<ParquetRowGroupWriter, ReportRow[], int, Task> WriteAsync { get; init; }

    public string Name => Field.Name;

    public static ReportColumn Str(string propertyPath, string name, RowGetter<string?> getter)
    {
        var field = new DataField(name, typeof(string), isNullable: true);
        return new ReportColumn
        {
            PropertyPath = propertyPath,
            Field = field,
            IsString = true,
            WriteAsync = (rowGroup, rows, count) =>
            {
                var values = new string?[count];
                for (int i = 0; i < count; i++)
                    values[i] = getter(in rows[i]);
                return rowGroup.WriteAsync(field, values);
            },
        };
    }

    public static ReportColumn Of<T>(string propertyPath, string name, RowGetter<T?> getter,
        DataField? field = null) where T : struct
    {
        field ??= new DataField(name, typeof(T?));
        return new ReportColumn
        {
            PropertyPath = propertyPath,
            Field = field,
            WriteAsync = (rowGroup, rows, count) =>
            {
                var values = new T?[count];
                for (int i = 0; i < count; i++)
                    values[i] = getter(in rows[i]);
                return rowGroup.WriteAsync<T>(field, new ReadOnlyMemory<T?>(values));
            },
        };
    }

    /// <summary>
    /// A wall-clock time with no known time zone, such as AcquiredTime. Like Skyline's
    /// ParquetReportExporter, writes the TIMESTAMP logical type (not the deprecated INT96) with
    /// isAdjustedToUTC false and the digits unchanged, stored to the millisecond.
    /// </summary>
    public static ReportColumn WallClockTime(string propertyPath, string name, RowGetter<DateTime?> getter) =>
        Of(propertyPath, name, getter,
            new DateTimeDataField(name, DateTimeFormat.Timestamp, false, DateTimeTimeUnit.Millis, true));
}

/// <summary>
/// The columns supported for the Transition row source with the Results!* sublist. Names are the
/// invariant captions Skyline uses for Parquet (and for --report-invariant CSV).
/// </summary>
public static class ReportColumns
{
    private const string Results = "Results!*.Value.";
    private const string ResultFile = Results + "PrecursorResult.PeptideResult.ResultFile.";

    public static readonly IReadOnlyList<ReportColumn> All =
    [
        ReportColumn.Str("Precursor.Peptide.Protein", "Protein", (in r) => r.Protein),
        ReportColumn.Str("Precursor.Peptide.Protein.Accession", "ProteinAccession", (in r) => r.ProteinAccession),
        ReportColumn.Str("Precursor.Peptide.Protein.Gene", "ProteinGene", (in r) => r.ProteinGene),
        ReportColumn.Str("Precursor.Peptide", "Peptide", (in r) => r.Peptide),
        ReportColumn.Str("Precursor.Peptide.ModifiedSequence.UnimodIds", "PeptideModifiedSequenceUnimodIds", (in r) => r.PeptideModifiedSequenceUnimodIds),
        ReportColumn.Of("Precursor.Charge", "PrecursorCharge", (in r) => r.PrecursorCharge),
        ReportColumn.Of("Precursor.Mz", "PrecursorMz", (in r) => r.PrecursorMz),
        ReportColumn.Of(Results + "PrecursorResult.IsotopeDotProduct", "IsotopeDotProduct", (in r) => r.IsotopeDotProduct),
        ReportColumn.Of(Results + "PrecursorResult.DetectionQValue", "DetectionQValue", (in r) => r.DetectionQValue),
        ReportColumn.Str("FragmentIon", "FragmentIon", (in r) => r.FragmentIon),
        ReportColumn.Of("ProductCharge", "ProductCharge", (in r) => r.ProductCharge),
        ReportColumn.Of("ProductMz", "ProductMz", (in r) => r.ProductMz),
        ReportColumn.Of(Results + "Area", "Area", (in r) => r.Area),
        ReportColumn.Of(Results + "Background", "Background", (in r) => r.Background),
        ReportColumn.Of(Results + "RetentionTime", "RetentionTime", (in r) => r.RetentionTime),
        ReportColumn.Of(Results + "StartTime", "StartTime", (in r) => r.StartTime),
        ReportColumn.Of(Results + "EndTime", "EndTime", (in r) => r.EndTime),
        ReportColumn.Of(Results + "Fwhm", "Fwhm", (in r) => r.Fwhm),
        ReportColumn.Of(Results + "ShapeCorrelation", "ShapeCorrelation", (in r) => r.ShapeCorrelation),
        ReportColumn.Of(Results + "Truncated", "Truncated", (in r) => r.Truncated),
        ReportColumn.Str(ResultFile + "Replicate.Name", "ReplicateName", (in r) => r.ReplicateName),
        ReportColumn.Str(ResultFile + "FileName", "FileName", (in r) => r.FileName),
        ReportColumn.Of(ResultFile + "TicArea", "TicArea", (in r) => r.TicArea),
        ReportColumn.WallClockTime(ResultFile + "AcquiredTime", "AcquiredTime", (in r) => r.AcquiredTime),
    ];

    private static readonly Dictionary<string, ReportColumn> ByPropertyPath = All.ToDictionary(c => c.PropertyPath);

    public static ReportColumn? Find(string propertyPath) => ByPropertyPath.GetValueOrDefault(propertyPath);
}
