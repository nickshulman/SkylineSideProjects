using System.Globalization;
using System.Xml.Linq;

namespace RemoteReports;

/// <summary>
/// The parts of a .sky document's &lt;settings_summary&gt; that the PRISM report needs: the static
/// modifications (for UnimodIds) and the replicates and their files. The settings element always
/// comes before the first protein, and is small, so it is loaded as an <see cref="XElement"/>.
/// </summary>
public sealed class SkyDocumentSettings
{
    public required IReadOnlyList<StaticMod> StaticMods { get; init; }
    public required IReadOnlyList<Replicate> Replicates { get; init; }

    private Dictionary<string, StaticMod>? _staticModsByName;
    private Dictionary<string, int>? _replicateIndexByName;

    public static readonly SkyDocumentSettings Empty = new() { StaticMods = [], Replicates = [] };

    public StaticMod? FindStaticMod(string name)
    {
        // Called from the worker threads: EnsureInitialized publishes a single, fully built dictionary.
        return LazyInitializer.EnsureInitialized(ref _staticModsByName,
            () => StaticMods.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First())).GetValueOrDefault(name);
    }

    public int FindReplicateIndex(string name)
    {
        return LazyInitializer.EnsureInitialized(ref _replicateIndexByName, () => Replicates.Select((r, i) => (r.Name, i))
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().i)).GetValueOrDefault(name, -1);
    }

    public static SkyDocumentSettings FromXml(XElement settingsSummary)
    {
        var staticMods = settingsSummary.Element("peptide_settings")?.Element("peptide_modifications")
            ?.Element("static_modifications")?.Elements("static_modification").Select(StaticMod.FromXml).ToList() ?? [];

        var replicates = new List<Replicate>();
        var measuredResults = settingsSummary.Element("measured_results");
        if (measuredResults != null)
        {
            foreach (var replicateEl in measuredResults.Elements().Where(e => e.Name == "replicate" || e.Name == "chromatogram_group"))
            {
                var files = replicateEl.Elements()
                    .Where(e => e.Name == "sample_file" || e.Name == "replicate_file" || e.Name == "chromatogram_file")
                    .Select(SampleFile.FromXml).ToList();
                replicates.Add(new Replicate((string?)replicateEl.Attribute("name") ?? "", files));
            }
        }
        return new SkyDocumentSettings { StaticMods = staticMods, Replicates = replicates };
    }
}

public sealed record Replicate(string Name, IReadOnlyList<SampleFile> Files)
{
    /// <summary>
    /// Peaks name their file by sample_file id, and leave it out when the replicate has only one file.
    /// </summary>
    public int FindFileIndex(string? fileId)
    {
        if (fileId == null)
            return Files.Count > 0 ? 0 : -1;
        for (int i = 0; i < Files.Count; i++)
        {
            if (Files[i].Id == fileId)
                return i;
        }
        return -1;
    }
}

public sealed record SampleFile(string Id, string FileName, double? TicArea, DateTime? AcquiredTime)
{
    public static SampleFile FromXml(XElement el)
    {
        string? acquired = (string?)el.Attribute("acquired_time");
        string? tic = (string?)el.Attribute("tic_area");
        return new SampleFile(
            (string?)el.Attribute("id") ?? "",
            GetFileName((string?)el.Attribute("file_path") ?? ""),
            tic != null ? double.Parse(tic, CultureInfo.InvariantCulture) : null,
            acquired != null ? ParseAcquiredTime(acquired) : null);
    }

    /// <summary>
    /// Skyline writes acquired_time as "yyyy-MM-ddTHH:mm:ss" with no time zone. ProteoWizard reports the run
    /// start as UTC, but Skyline parses it into local time and saves that, so the value is the wall-clock
    /// time in the time zone of the computer that saved the document, which is not recorded. It is kept as
    /// is. Skyline's own export gets the true UTC instant from the .skyd instead, so the two differ by that
    /// computer's UTC offset. If a time zone is ever present, the value is converted to UTC.
    /// </summary>
    private static DateTime ParseAcquiredTime(string value)
    {
        var time = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        // RoundtripKind gives Unspecified with no zone, Utc for "Z", and Local (already converted) for an offset.
        return time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;
    }

    /// <summary>
    /// Mirrors MsDataFileUri.GetFileName: drop "?" URL parameters (lockmass, centroiding) and the
    /// "|sample|index" suffix of multi-sample files, then take the file name. Remote URLs
    /// (unifi:, ardia:, waters_connect:) end in the file name as well.
    /// </summary>
    private static string GetFileName(string filePath)
    {
        int cut = filePath.IndexOf('?');
        if (cut >= 0)
            filePath = filePath[..cut];
        cut = filePath.IndexOf('|');
        if (cut >= 0)
            filePath = filePath[..cut];
        int slash = filePath.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? filePath[(slash + 1)..] : filePath;
    }
}

/// <summary>A &lt;static_modification&gt; from the document settings.</summary>
public sealed record StaticMod(string Name, string? AminoAcids, char? Terminus, bool IsVariable, bool IsExplicit,
    bool IsCrosslinker, int? UnimodId, bool IsZeroMass)
{
    public static StaticMod FromXml(XElement el)
    {
        string? terminus = (string?)el.Attribute("terminus");
        int? unimodId = (int?)el.Attribute("unimod_id");
        return new StaticMod(
            (string?)el.Attribute("name") ?? "",
            (string?)el.Attribute("aminoacid"),
            string.IsNullOrEmpty(terminus) ? null : terminus[0],
            (bool?)el.Attribute("variable") ?? false,
            (bool?)el.Attribute("explicit_decl") ?? false,
            el.Element("crosslinker") != null,
            unimodId == -1 ? null : unimodId,
            IsZeroMassMod(el));
    }

    /// <summary>
    /// Skyline drops modifications with no mass from modified sequences. That covers a mod that only
    /// defines neutral losses, or one whose mass is explicitly 0. A formula is assumed to be non-zero
    /// (computing its mass would need a full element table).
    /// </summary>
    private static bool IsZeroMassMod(XElement el)
    {
        if (!string.IsNullOrEmpty((string?)el.Attribute("formula")))
            return false;
        if (el.Attributes().Any(a => a.Name.LocalName.StartsWith("label_") && (bool)a))
            return false;
        string? mass = (string?)el.Attribute("massdiff_monoisotopic");
        return mass == null || double.Parse(mass, CultureInfo.InvariantCulture) == 0;
    }

    public static StaticMod Unknown(string name) => new(name, null, null, false, true, false, null, false);
}
