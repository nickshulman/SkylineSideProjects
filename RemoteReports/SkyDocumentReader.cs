using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using pwiz.Skyline.Model.Serialization;
using ProtoTransition = pwiz.Skyline.Model.Serialization.SkylineDocumentProto.Types.Transition;
using ProtoPeak = pwiz.Skyline.Model.Serialization.SkylineDocumentProto.Types.TransitionPeak;
using ProtoIonType = pwiz.Skyline.Model.Serialization.SkylineDocumentProto.Types.IonType;
using OptionalBool = pwiz.Skyline.Model.Serialization.SkylineDocumentProto.Types.OptionalBool;

namespace RemoteReports;

/// <summary>
/// Streams a Skyline .sky document with an <see cref="XmlReader"/> and produces the rows of the PRISM
/// report (row source Transition, sublist Results!*): one row per transition per peak, or a single
/// row with empty result columns for a transition with no peaks. Only the settings and one peptide's
/// rows are held in memory at a time.
///
/// This reimplements the relevant parts of Skyline's DocumentReader and report entities without using
/// Skyline code. Not handled: small molecules (&lt;molecule&gt; is skipped), crosslinked peptides
/// (linked peptides and crosslinkers are left out of Peptide and UnimodIds), and custom ion names.
/// </summary>
public sealed class SkyDocumentReader
{
    private readonly XmlReader _reader;
    private SkyDocumentSettings _settings = SkyDocumentSettings.Empty;
    private readonly List<ReportRow> _rows = [];

    private SkyDocumentReader(XmlReader reader) => _reader = reader;

    public static IEnumerable<ReportRow> ReadRows(Stream skyStream)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
        };
        using var xmlReader = XmlReader.Create(skyStream, settings);
        foreach (var row in new SkyDocumentReader(xmlReader).ReadDocument())
            yield return row;
    }

    private IEnumerable<ReportRow> ReadDocument()
    {
        var r = _reader;
        r.MoveToContent();
        if (r.LocalName != "srm_settings")
            throw new InvalidDataException($"Not a Skyline document: root element is <{r.LocalName}>");
        if (!EnterElement(r))
            yield break;
        while (NextChild(r))
        {
            switch (r.LocalName)
            {
                case "settings_summary":
                    _settings = SkyDocumentSettings.FromXml((XElement)XNode.ReadFrom(r));
                    break;
                case "protein":
                case "peptide_list":
                case "protein_group":
                    // Rows are collected one peptide at a time (see ReadPeptide) and handed out here,
                    // since an iterator cannot yield from the nested readers.
                    foreach (var _ in ReadPeptideGroup())
                    {
                        foreach (var row in _rows)
                            yield return row;
                        _rows.Clear();
                    }
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private sealed class ProteinInfo
    {
        public string? Name;
        public string? Accession;
        public string? Gene;
    }

    /// <summary>Yields once after each peptide, with that peptide's rows in <see cref="_rows"/>.</summary>
    private IEnumerable<bool> ReadPeptideGroup()
    {
        var r = _reader;
        string elementName = r.LocalName;
        var protein = new ProteinInfo();
        if (elementName == "protein")
        {
            protein.Name = r.GetAttribute("label_name") ?? r.GetAttribute("name");
        }
        else if (elementName == "peptide_list")
        {
            protein.Name = r.GetAttribute("label_name") ?? "";
        }
        protein.Accession = r.GetAttribute("accession");
        protein.Gene = r.GetAttribute("gene");
        string? groupName = elementName == "protein_group" ? r.GetAttribute("name") : null;
        var members = new List<(string? Name, string? Accession, string? Gene)>();

        if (!EnterElement(r))
            yield break;
        while (NextChild(r))
        {
            switch (r.LocalName)
            {
                case "protein" when elementName == "protein_group":
                    members.Add((r.GetAttribute("label_name") ?? r.GetAttribute("name"),
                        r.GetAttribute("accession"), r.GetAttribute("gene")));
                    r.Skip();
                    break;
                case "peptide":
                    if (elementName == "protein_group" && protein.Name == null)
                        SetProteinGroupInfo(protein, groupName, members);
                    ReadPeptide(protein);
                    yield return true;
                    break;
                default:
                    // <molecule>, <sequence>, notes, annotations
                    r.Skip();
                    break;
            }
        }
    }

    /// <summary>Mirrors ProteinGroupMetadata: member values joined with " / ".</summary>
    private static void SetProteinGroupInfo(ProteinInfo protein, string? groupName,
        List<(string? Name, string? Accession, string? Gene)> members)
    {
        string names = string.Join(" / ", members.Select(m => m.Name ?? ""));
        protein.Name = members.All(m => string.IsNullOrEmpty(m.Name)) ? groupName ?? "" : names;
        protein.Accession = JoinMembers(members.Select(m => m.Accession));
        protein.Gene = JoinMembers(members.Select(m => m.Gene));
    }

    private static string? JoinMembers(IEnumerable<string?> values)
    {
        var list = values.ToList();
        if (list.All(string.IsNullOrEmpty))
            return null;
        return string.Join(" / ", list.Select(v => string.IsNullOrEmpty(v) ? "Unknown" : v));
    }

    private sealed class PeptideInfo
    {
        public required ProteinInfo Protein;
        public required string Sequence;
        public List<(int IndexAa, string Name)> ExplicitStaticMods = [];
        /// <summary>
        /// An &lt;explicit_static_modifications&gt; element replaces the implicit static mods from the
        /// settings; &lt;variable_modifications&gt; adds to them.
        /// </summary>
        public bool HasExplicitStaticMods;
        public string? UnimodIds;
    }

    private void ReadPeptide(ProteinInfo protein)
    {
        var r = _reader;
        var peptide = new PeptideInfo { Protein = protein, Sequence = r.GetAttribute("sequence") ?? "" };
        if (!EnterElement(r))
            return;
        while (NextChild(r))
        {
            switch (r.LocalName)
            {
                case "variable_modifications":
                    ReadExplicitMods(peptide.ExplicitStaticMods);
                    break;
                case "explicit_modifications":
                    if (EnterElement(r))
                    {
                        while (NextChild(r))
                        {
                            if (r.LocalName == "explicit_static_modifications")
                            {
                                peptide.HasExplicitStaticMods = true;
                                ReadExplicitMods(peptide.ExplicitStaticMods);
                            }
                            else
                            {
                                r.Skip();
                            }
                        }
                    }
                    break;
                case "precursor":
                    peptide.UnimodIds ??= UnimodIdSequence.Format(peptide.Sequence, peptide.ExplicitStaticMods,
                        !peptide.HasExplicitStaticMods, _settings);
                    ReadPrecursor(peptide);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private void ReadExplicitMods(List<(int IndexAa, string Name)> mods)
    {
        var r = _reader;
        if (!EnterElement(r))
            return;
        while (NextChild(r))
        {
            if (r.LocalName is "variable_modification" or "explicit_modification")
            {
                string? index = r.GetAttribute("index_aa");
                string? name = r.GetAttribute("modification_name");
                if (index != null && name != null)
                    mods.Add((int.Parse(index, CultureInfo.InvariantCulture), name));
            }
            r.Skip();
        }
    }

    private sealed class PrecursorInfo
    {
        public required PeptideInfo Peptide;
        public int Charge;
        public double? Mz;
        /// <summary>Precursor peaks by (replicate index, file index, optimization step).</summary>
        public Dictionary<(int, int, int), (double? IsotopeDotProduct, double? QValue)> Peaks = [];
    }

    private void ReadPrecursor(PeptideInfo peptide)
    {
        var r = _reader;
        var precursor = new PrecursorInfo
        {
            Peptide = peptide,
            Charge = ParseInt(r.GetAttribute("charge")) ?? 0,
            Mz = ParseDouble(r.GetAttribute("precursor_mz")),
        };
        if (!EnterElement(r))
            return;
        while (NextChild(r))
        {
            switch (r.LocalName)
            {
                case "precursor_results":
                    ReadPrecursorResults(precursor);
                    break;
                case "transition_data":
                    var data = SkylineDocumentProto.Types.TransitionData.Parser.ParseFrom(
                        Convert.FromBase64String(r.ReadElementContentAsString()));
                    foreach (var transition in data.Transitions)
                        AddProtoTransitionRows(precursor, transition);
                    break;
                case "transition":
                    ReadTransition(precursor);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private void ReadPrecursorResults(PrecursorInfo precursor)
    {
        var r = _reader;
        if (!EnterElement(r))
            return;
        while (NextChild(r))
        {
            if (r.LocalName != "precursor_peak")
            {
                r.Skip();
                continue;
            }
            var key = GetResultKey(r.GetAttribute("replicate"), r.GetAttribute("file"), r.GetAttribute("step"));
            double? isotopeDotProduct = ParseFloat(r.GetAttribute("isotope_dotp"));
            double? qValue = ParseFloat(r.GetAttribute("qvalue"));
            // Older documents stored the q-value as an annotation, which takes precedence.
            if (EnterElement(r))
            {
                while (NextChild(r))
                {
                    if (r.LocalName == "annotation" && r.GetAttribute("name") == "annotation_QValue")
                    {
                        if (double.TryParse(r.ReadElementContentAsString(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double annotationValue))
                            qValue = annotationValue;
                    }
                    else
                    {
                        r.Skip();
                    }
                }
            }
            if (key != null)
                precursor.Peaks.TryAdd(key.Value, (isotopeDotProduct, qValue));
        }
    }

    /// <summary>The replicate/file/step a peak belongs to, or null if it does not match the settings.</summary>
    private (int, int, int)? GetResultKey(string? replicateName, string? fileId, string? step)
    {
        int replicateIndex = replicateName != null ? _settings.FindReplicateIndex(replicateName) : -1;
        if (replicateIndex < 0)
            return null;
        int fileIndex = _settings.Replicates[replicateIndex].FindFileIndex(fileId);
        if (fileIndex < 0)
            return null;
        return (replicateIndex, fileIndex, ParseInt(step) ?? 0);
    }

    private sealed class PeakInfo
    {
        public (int Replicate, int File, int Step) Key;
        public double? Area;
        public double? Background;
        public double? RetentionTime;
        public double? StartTime;
        public double? EndTime;
        public double? Fwhm;
        public double? ShapeCorrelation;
        public bool? Truncated;
    }

    private void ReadTransition(PrecursorInfo precursor)
    {
        var r = _reader;
        string fragmentType = r.GetAttribute("fragment_type") ?? "";
        int ordinal = ParseInt(r.GetAttribute("fragment_ordinal")) ?? 0;
        int massIndex = ParseInt(r.GetAttribute("mass_index")) ?? 0;
        int charge = ParseInt(r.GetAttribute("product_charge")) ?? precursor.Charge;
        double? lossMass = ParseDouble(r.GetAttribute("loss_neutral_mass"));
        string? customName = r.GetAttribute("measured_ion_name") ?? r.GetAttribute("custom_ion_name");
        bool hasLosses = false;
        double? productMz = null;
        var peaks = new List<PeakInfo>();

        if (EnterElement(r))
        {
            while (NextChild(r))
            {
                switch (r.LocalName)
                {
                    case "product_mz":
                        productMz = double.Parse(r.ReadElementContentAsString(), CultureInfo.InvariantCulture);
                        break;
                    case "losses":
                        hasLosses = true;
                        r.Skip();
                        break;
                    case "transition_results":
                        ReadTransitionPeaks(peaks);
                        break;
                    default:
                        r.Skip();
                        break;
                }
            }
        }

        string fragmentIon = FormatFragmentIon(fragmentType, ordinal, massIndex,
            hasLosses ? lossMass ?? 0 : null, customName);
        AddRows(precursor, fragmentIon, charge, productMz, peaks);
    }

    private void ReadTransitionPeaks(List<PeakInfo> peaks)
    {
        var r = _reader;
        if (!EnterElement(r))
            return;
        while (NextChild(r))
        {
            if (r.LocalName != "transition_peak")
            {
                r.Skip();
                continue;
            }
            var key = GetResultKey(r.GetAttribute("replicate"), r.GetAttribute("file"), r.GetAttribute("step"));
            double endTime = ParseFloat(r.GetAttribute("end_time")) ?? 0;
            var peak = new PeakInfo
            {
                Truncated = r.GetAttribute("truncated") is { } truncated ? XmlConvert.ToBoolean(truncated) : null,
            };
            // Skyline treats a peak with no end time as empty (no peak found in that file).
            if (endTime != 0)
            {
                peak.Area = Math.Max(0, ParseFloat(r.GetAttribute("area")) ?? 0);
                peak.Background = ParseFloat(r.GetAttribute("background")) ?? 0;
                peak.RetentionTime = ParseFloat(r.GetAttribute("retention_time")) ?? 0;
                peak.StartTime = ParseFloat(r.GetAttribute("start_time")) ?? 0;
                peak.EndTime = endTime;
                double fwhm = ParseFloat(r.GetAttribute("fwhm")) ?? 0;
                peak.Fwhm = double.IsNaN(fwhm) ? 0 : fwhm;
                // Skyline reads the peak shape attributes only on an empty <transition_peak/>: when the
                // peak has a note or annotations, it reads them after moving past the start tag and
                // loses them. Reproduced here so the output matches.
                if (r.IsEmptyElement && r.GetAttribute("std_dev") != null && r.GetAttribute("skewness") != null
                    && r.GetAttribute("kurtosis") != null)
                {
                    peak.ShapeCorrelation = ParseFloat(r.GetAttribute("shape_correlation")) ?? 1;
                }
            }
            r.Skip();
            if (key != null)
            {
                peak.Key = key.Value;
                peaks.Add(peak);
            }
        }
    }

    private void AddProtoTransitionRows(PrecursorInfo precursor, ProtoTransition transition)
    {
        string fragmentType = transition.FragmentType switch
        {
            ProtoIonType.A => "a",
            ProtoIonType.B => "b",
            ProtoIonType.C => "c",
            ProtoIonType.X => "x",
            ProtoIonType.Y => "y",
            ProtoIonType.Z => "z",
            ProtoIonType.ZH => "zh",
            ProtoIonType.ZHh => "zhh",
            ProtoIonType.Precursor => "precursor",
            _ => "custom",
        };
        string fragmentIon = FormatFragmentIon(fragmentType, transition.FragmentOrdinal, transition.MassIndex,
            transition.Losses.Count > 0 ? transition.LostMass : null,
            transition.MeasuredIonName ?? transition.CustomIonName);
        // A charge of 0 means the same adduct as the precursor.
        int charge = transition.Charge != 0 ? transition.Charge : precursor.Charge;

        var peaks = new List<PeakInfo>();
        if (transition.Results != null)
        {
            foreach (var protoPeak in transition.Results.Peaks)
            {
                var peak = FromProto(protoPeak);
                if (peak != null)
                    peaks.Add(peak);
            }
        }
        AddRows(precursor, fragmentIon, charge, Math.Round(transition.ProductMz, 6), peaks);
    }

    private PeakInfo? FromProto(ProtoPeak p)
    {
        if (p.ReplicateIndex < 0 || p.ReplicateIndex >= _settings.Replicates.Count
            || p.FileIndexInReplicate < 0 || p.FileIndexInReplicate >= _settings.Replicates[p.ReplicateIndex].Files.Count)
            return null;
        var peak = new PeakInfo
        {
            Key = (p.ReplicateIndex, p.FileIndexInReplicate, p.OptimizationStep),
            Truncated = p.Truncated switch
            {
                OptionalBool.True => true,
                OptionalBool.False => false,
                _ => null,
            },
        };
        if (p.EndRetentionTime != 0)
        {
            peak.Area = Math.Max(0, p.Area);
            peak.Background = p.BackgroundArea;
            peak.RetentionTime = p.RetentionTime;
            peak.StartTime = p.StartRetentionTime;
            peak.EndTime = p.EndRetentionTime;
            peak.Fwhm = float.IsNaN(p.Fwhm) ? 0 : p.Fwhm;
            peak.ShapeCorrelation = p.PeakShapeValues?.ShapeCorrelation;
        }
        return peak;
    }

    private void AddRows(PrecursorInfo precursor, string fragmentIon, int productCharge, double? productMz,
        List<PeakInfo> peaks)
    {
        var peptide = precursor.Peptide;
        var row = new ReportRow
        {
            Protein = peptide.Protein.Name,
            ProteinAccession = peptide.Protein.Accession,
            ProteinGene = peptide.Protein.Gene,
            Peptide = peptide.Sequence,
            PeptideModifiedSequenceUnimodIds = peptide.UnimodIds,
            PrecursorCharge = precursor.Charge,
            PrecursorMz = precursor.Mz,
            FragmentIon = fragmentIon,
            ProductCharge = productCharge,
            ProductMz = productMz,
        };
        if (peaks.Count == 0)
        {
            _rows.Add(row);
            return;
        }
        // Rows are ordered by replicate, and by document order within a replicate (stable sort).
        foreach (var peak in peaks.OrderBy(p => p.Key.Replicate))
        {
            var replicate = _settings.Replicates[peak.Key.Replicate];
            var file = replicate.Files[peak.Key.File];
            var resultRow = row;
            if (precursor.Peaks.TryGetValue(peak.Key, out var precursorPeak))
            {
                resultRow.IsotopeDotProduct = precursorPeak.IsotopeDotProduct;
                resultRow.DetectionQValue = precursorPeak.QValue;
            }
            resultRow.Area = peak.Area;
            resultRow.Background = peak.Background;
            resultRow.RetentionTime = peak.RetentionTime;
            resultRow.StartTime = peak.StartTime;
            resultRow.EndTime = peak.EndTime;
            resultRow.Fwhm = peak.Fwhm;
            resultRow.ShapeCorrelation = peak.ShapeCorrelation;
            resultRow.Truncated = peak.Truncated;
            resultRow.ReplicateName = replicate.Name;
            resultRow.FileName = file.FileName;
            resultRow.TicArea = file.TicArea;
            resultRow.AcquiredTime = file.AcquiredTime;
            _rows.Add(resultRow);
        }
    }

    /// <summary>
    /// Mirrors Transition.FragmentIon: "y7", "precursor", "y7 -98" (loss mass rounded to 0.1),
    /// and "precursor [M+1]" for precursor isotopes.
    /// </summary>
    private static string FormatFragmentIon(string fragmentType, int ordinal, int massIndex, double? lossMass,
        string? customName)
    {
        string ion = fragmentType switch
        {
            "precursor" => "precursor",
            "custom" => customName ?? "custom",
            _ => fragmentType + ordinal.ToString(CultureInfo.InvariantCulture),
        };
        if (lossMass.HasValue)
            ion += " -" + Math.Round(lossMass.Value, 1).ToString(CultureInfo.InvariantCulture);
        if (fragmentType == "precursor" && massIndex != 0)
            ion += " [M" + (massIndex > 0 ? "+" : "") + massIndex.ToString(CultureInfo.InvariantCulture) + "]";
        return ion;
    }

    /// <summary>
    /// Moves inside the current element. Returns false (having consumed it) if it is empty.
    /// </summary>
    private static bool EnterElement(XmlReader r)
    {
        if (r.IsEmptyElement)
        {
            r.Read();
            return false;
        }
        r.Read();
        return true;
    }

    /// <summary>
    /// Advances to the next child element of the element entered with <see cref="EnterElement"/>.
    /// Returns false after consuming the parent's end tag. The caller must consume each child
    /// (read it or Skip()) before calling again.
    /// </summary>
    private static bool NextChild(XmlReader r)
    {
        while (true)
        {
            switch (r.MoveToContent())
            {
                case XmlNodeType.Element:
                    return true;
                case XmlNodeType.EndElement:
                    r.Read();
                    return false;
                case XmlNodeType.None:
                    return false;
                default:
                    r.Read();
                    break;
            }
        }
    }

    private static int? ParseInt(string? s) => s == null ? null : int.Parse(s, CultureInfo.InvariantCulture);

    private static double? ParseDouble(string? s) =>
        s == null ? null : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// Skyline stores peak values as float, so parse as float and widen, to get the same double
    /// Skyline would export (e.g. 36.30489 becomes 36.304889678955078).
    /// </summary>
    private static double? ParseFloat(string? s) =>
        s == null ? null : float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>Mirrors ModifiedSequence.UnimodIds for the light (unlabeled) form of a peptide.</summary>
public static class UnimodIdSequence
{
    public static string Format(string sequence, List<(int IndexAa, string Name)> explicitMods, bool addImplicitMods,
        SkyDocumentSettings settings)
    {
        var mods = new List<(int IndexAa, StaticMod Mod)>();
        foreach (var (indexAa, name) in explicitMods)
            mods.Add((indexAa, settings.FindStaticMod(name) ?? StaticMod.Unknown(name)));

        if (addImplicitMods)
        {
            for (int i = 0; i < sequence.Length; i++)
            {
                foreach (var mod in settings.StaticMods)
                {
                    if (mod.IsExplicit || mod.IsVariable || mod.IsCrosslinker)
                        continue;
                    if (mod.Terminus == 'N' && i != 0 || mod.Terminus == 'C' && i != sequence.Length - 1)
                        continue;
                    if (!string.IsNullOrEmpty(mod.AminoAcids) && !mod.AminoAcids.Contains(sequence[i]))
                        continue;
                    mods.Add((i, mod));
                }
            }
        }

        if (mods.Count == 0)
            return sequence;
        var result = new StringBuilder();
        int done = 0;
        // Stable sort: explicit mods before implicit ones on the same residue, as in Skyline.
        foreach (var group in mods.Where(m => !m.Mod.IsZeroMass).OrderBy(m => m.IndexAa).GroupBy(m => m.IndexAa))
        {
            if (group.Key >= sequence.Length)
                continue;
            result.Append(sequence, done, group.Key + 1 - done);
            done = group.Key + 1;
            foreach (var (_, mod) in group)
                result.Append(mod.UnimodId.HasValue ? "(unimod:" + mod.UnimodId.Value + ")" : Bracket(mod.Name));
        }
        result.Append(sequence, done, sequence.Length - done);
        return result.ToString();
    }

    private static string Bracket(string s)
    {
        if (!s.Contains(']'))
            return "[" + s + "]";
        if (!s.Contains(')'))
            return "(" + s + ")";
        if (!s.Contains('}'))
            return "{" + s + "}";
        return "[" + s.Replace(']', '_') + "]";
    }
}
