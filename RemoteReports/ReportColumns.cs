using Parquet;
using Parquet.Schema;

namespace RemoteReports;

/// <summary>
/// A report column this tool knows how to produce: the Skyline property path used in .skyr files,
/// the invariant column name Skyline's Parquet exporter writes, and how to write it.
/// </summary>
public sealed class ReportColumn
{
    public required string PropertyPath { get; init; }
    public required DataField Field { get; init; }
    public bool IsString { get; private init; }
    public required Func<ParquetRowGroupWriter, ReportRow[], int, Task> WriteAsync { get; init; }

    public string Name => Field.Name;

    public static ReportColumn Str(string propertyPath, string name, Func<ReportRow, string?> getter)
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
                    values[i] = getter(rows[i]);
                return rowGroup.WriteAsync(field, values);
            },
        };
    }

    public static ReportColumn Of<T>(string propertyPath, string name, Func<ReportRow, T?> getter) where T : struct
    {
        var field = new DataField(name, typeof(T?));
        return new ReportColumn
        {
            PropertyPath = propertyPath,
            Field = field,
            WriteAsync = (rowGroup, rows, count) =>
            {
                var values = new T?[count];
                for (int i = 0; i < count; i++)
                    values[i] = getter(rows[i]);
                return rowGroup.WriteAsync<T>(field, new ReadOnlyMemory<T?>(values));
            },
        };
    }
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
        ReportColumn.Str("Precursor.Peptide.Protein", "Protein", r => r.Protein),
        ReportColumn.Str("Precursor.Peptide.Protein.Accession", "ProteinAccession", r => r.ProteinAccession),
        ReportColumn.Str("Precursor.Peptide.Protein.Gene", "ProteinGene", r => r.ProteinGene),
        ReportColumn.Str("Precursor.Peptide", "Peptide", r => r.Peptide),
        ReportColumn.Str("Precursor.Peptide.ModifiedSequence.UnimodIds", "PeptideModifiedSequenceUnimodIds", r => r.PeptideModifiedSequenceUnimodIds),
        ReportColumn.Of("Precursor.Charge", "PrecursorCharge", r => r.PrecursorCharge),
        ReportColumn.Of("Precursor.Mz", "PrecursorMz", r => r.PrecursorMz),
        ReportColumn.Of(Results + "PrecursorResult.IsotopeDotProduct", "IsotopeDotProduct", r => r.IsotopeDotProduct),
        ReportColumn.Of(Results + "PrecursorResult.DetectionQValue", "DetectionQValue", r => r.DetectionQValue),
        ReportColumn.Str("FragmentIon", "FragmentIon", r => r.FragmentIon),
        ReportColumn.Of("ProductCharge", "ProductCharge", r => r.ProductCharge),
        ReportColumn.Of("ProductMz", "ProductMz", r => r.ProductMz),
        ReportColumn.Of(Results + "Area", "Area", r => r.Area),
        ReportColumn.Of(Results + "Background", "Background", r => r.Background),
        ReportColumn.Of(Results + "RetentionTime", "RetentionTime", r => r.RetentionTime),
        ReportColumn.Of(Results + "StartTime", "StartTime", r => r.StartTime),
        ReportColumn.Of(Results + "EndTime", "EndTime", r => r.EndTime),
        ReportColumn.Of(Results + "Fwhm", "Fwhm", r => r.Fwhm),
        ReportColumn.Of(Results + "ShapeCorrelation", "ShapeCorrelation", r => r.ShapeCorrelation),
        ReportColumn.Of(Results + "Truncated", "Truncated", r => r.Truncated),
        ReportColumn.Str(ResultFile + "Replicate.Name", "ReplicateName", r => r.ReplicateName),
        ReportColumn.Str(ResultFile + "FileName", "FileName", r => r.FileName),
        ReportColumn.Of(ResultFile + "TicArea", "TicArea", r => r.TicArea),
        ReportColumn.Of(ResultFile + "AcquiredTime", "AcquiredTime", r => r.AcquiredTime),
    ];

    private static readonly Dictionary<string, ReportColumn> ByPropertyPath = All.ToDictionary(c => c.PropertyPath);

    public static ReportColumn? Find(string propertyPath) => ByPropertyPath.GetValueOrDefault(propertyPath);
}
