using CommandLine;

namespace ResourcesOrganizer
{
    public class Options
    {
        [Option("db", Default = "resources.db")]
        public string DbFile { get; set; }

        [Value(0, MetaName = "files", Required = true)]
        public IEnumerable<string> Files { get; set; }
    }

    [Verb("add")]
    public class AddOptions : Options
    {
    }

    [Verb("subtract")]
    public class SubtractOptions : Options
    {
    }

    [Verb("intersect")]
    public class IntersectOptions : Options
    {
    }
}
