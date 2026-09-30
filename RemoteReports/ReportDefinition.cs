using System.Xml.Linq;

namespace RemoteReports;

/// <summary>
/// A report definition (a &lt;view&gt; from a Skyline .skyr file), validated against what this tool
/// can produce: the Transition row source with the Results!* sublist, and only columns listed in
/// <see cref="ReportColumns"/>. Anything else (other row sources, filters, sorting, custom captions,
/// unknown columns) is rejected rather than silently producing different output than Skyline would.
/// </summary>
public sealed class ReportDefinition
{
    public const string TransitionRowSource = "pwiz.Skyline.Model.Databinding.Entities.Transition";
    public const string ResultsSublist = "Results!*";

    public required string Name { get; init; }
    public required IReadOnlyList<ReportColumn> Columns { get; init; }

    /// <summary>The PRISM report (Skyline-PRISM.skyr), used when no .skyr is given.</summary>
    public static ReportDefinition Prism { get; } = new()
    {
        Name = "PRISM",
        Columns = ReportColumns.All.Where(c => c.Name != "Background").ToList(),
    };

    public static ReportDefinition Load(string skyrPath, string? reportName)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(skyrPath);
        }
        catch (System.Xml.XmlException e)
        {
            throw new ReportDefinitionException($"{skyrPath} is not valid XML: {e.Message}");
        }
        var views = doc.Root?.Name == "views" ? doc.Root.Elements("view").ToList() : [];
        if (views.Count == 0)
            throw new ReportDefinitionException($"{skyrPath} does not contain any reports (<views><view>)");

        XElement view;
        if (reportName != null)
        {
            view = views.FirstOrDefault(v => (string?)v.Attribute("name") == reportName)
                ?? throw new ReportDefinitionException($"{skyrPath} has no report named '{reportName}'. Reports: {NameList(views)}");
        }
        else if (views.Count == 1)
        {
            view = views[0];
        }
        else
        {
            throw new ReportDefinitionException($"{skyrPath} contains {views.Count} reports; choose one with --report-name: {NameList(views)}");
        }
        return FromXml(view);
    }

    private static string NameList(IEnumerable<XElement> views) =>
        string.Join(", ", views.Select(v => $"'{(string?)v.Attribute("name")}'"));

    public static ReportDefinition FromXml(XElement view)
    {
        string name = (string?)view.Attribute("name") ?? "";
        var errors = new List<string>();

        foreach (var attr in view.Attributes())
        {
            switch (attr.Name.LocalName)
            {
                case "name":
                    break;
                case "rowsource":
                    if (attr.Value != TransitionRowSource)
                        errors.Add($"Row source '{attr.Value}' is not supported; only '{TransitionRowSource}' is.");
                    break;
                case "sublist":
                    if (attr.Value != ResultsSublist)
                        errors.Add($"Sublist '{attr.Value}' is not supported; only '{ResultsSublist}' is.");
                    break;
                case "uimode":
                    // UI mode changes some invariant column names; only the proteomic names are known here.
                    if (attr.Value != "proteomic")
                        errors.Add($"UI mode '{attr.Value}' is not supported; only 'proteomic' is.");
                    break;
                default:
                    errors.Add($"Report attribute '{attr.Name}' is not supported.");
                    break;
            }
        }
        if (view.Attribute("rowsource") == null)
            errors.Add($"The report has no row source; only '{TransitionRowSource}' is supported.");
        if (view.Attribute("sublist") == null)
            errors.Add($"The report has no sublist; only '{ResultsSublist}' is supported.");

        var columns = new List<ReportColumn>();
        foreach (var child in view.Elements())
        {
            if (child.Name != "column")
            {
                // <filter> and anything else that would change which rows appear
                errors.Add($"<{child.Name}> elements are not supported.");
                continue;
            }
            string? path = (string?)child.Attribute("name");
            foreach (var attr in child.Attributes().Where(a => a.Name != "name"))
                errors.Add($"Column '{path}': attribute '{attr.Name}' (e.g. captions, sorting, hidden columns) is not supported.");
            if (path == null)
            {
                errors.Add("A <column> has no name.");
                continue;
            }
            var column = ReportColumns.Find(path);
            if (column == null)
                errors.Add($"Column '{path}' is not supported.");
            else if (columns.Contains(column))
                errors.Add($"Column '{path}' appears more than once.");
            else
                columns.Add(column);
        }
        if (errors.Count == 0 && columns.Count == 0)
            errors.Add("The report has no columns.");

        if (errors.Count > 0)
        {
            throw new ReportDefinitionException($"Report '{name}' cannot be produced by this tool:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(e => "  " + e)) + Environment.NewLine
                + "Supported columns:" + Environment.NewLine
                + string.Join(Environment.NewLine, ReportColumns.All.Select(c => "  " + c.PropertyPath)));
        }
        return new ReportDefinition { Name = name, Columns = columns };
    }
}

public sealed class ReportDefinitionException(string message) : Exception(message);
