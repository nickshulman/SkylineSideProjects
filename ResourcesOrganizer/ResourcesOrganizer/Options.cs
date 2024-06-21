using CommandLine;

namespace ResourcesOrganizer
{
    public class Options
    {
        [Option("db", Default = "resources.db")]
        public string DbFile { get; set; }

    }

    [Verb("add")]
    public class AddOptions : Options
    {
        [Value(0, MetaName = "files", Required = true)]
        public IEnumerable<string> Files { get; set; }
    }

    [Verb("subtract")]
    public class SubtractOptions : Options
    {
        [Value(0, MetaName = "files", Required = true)]
        public IEnumerable<string> Files { get; set; }
    }

    [Verb("intersect")]
    public class IntersectOptions : Options
    {
        [Value(0, MetaName = "files", Required = true)]
        public IEnumerable<string> Files { get; set; }
    }

    [Verb("export")]
    public class ExportOptions : Options
    {
        [Option("output", Default="resources.zip")]
        public String OutputFile { get; set; }
    }
}
