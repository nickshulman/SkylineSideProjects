using CommandLine;

namespace ResourcesOrganizer
{
    public class Options
    {
        [Option("db", Default = "resources.db")]
        public string DbFile { get; set; }

    }

    public class ImportOptions : Options
    {
        [Value(0, MetaName = "files", Required = true, HelpText = ".resx, directory, or resources.db")]
        public IEnumerable<string> Files { get; set; }
    }

    [Verb("add", HelpText = "Adds resources to a database")]
    public class AddOptions : ImportOptions
    {
    }

    [Verb("subtract", HelpText = "Removes resources from a database")]
    public class SubtractOptions : ImportOptions
    {
    }

    [Verb("intersect", HelpText = "Removes resources from a database except")]
    public class IntersectOptions : ImportOptions
    {
    }

    [Verb("export", HelpText = "Export .resx files to a .zip")]
    public class ExportOptions : Options
    {
        [Value(0, MetaName = "output", Default="resources.zip")]
        public string OutputFile { get; set; }
    }
}
