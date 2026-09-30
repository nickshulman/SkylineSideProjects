namespace RemoteReports;

/// <summary>
/// One row of the PRISM report: a transition paired with one of its results (replicate + file).
/// Result-level fields are null when the transition has no peak in that result.
/// </summary>
public struct ReportRow
{
    public string? Protein;
    public string? ProteinAccession;
    public string? ProteinGene;
    public string? Peptide;
    public string? PeptideModifiedSequenceUnimodIds;
    public int? PrecursorCharge;
    public double? PrecursorMz;
    public double? IsotopeDotProduct;
    public double? DetectionQValue;
    public string? FragmentIon;
    public int? ProductCharge;
    public double? ProductMz;
    public double? Area;
    public double? Background;
    public double? RetentionTime;
    public double? StartTime;
    public double? EndTime;
    public double? Fwhm;
    public double? ShapeCorrelation;
    public bool? Truncated;
    public string? ReplicateName;
    public string? FileName;
    public double? TicArea;
    public DateTime? AcquiredTime;
}
