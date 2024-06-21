using CommandLine;

namespace ResourcesOrganizer
{
    public class Options
    {
        [Option("dbfile", Default = "resources.db")]
        public string DbFile { get; set; }
    }

    [Verb("import")]
    public class ImportOptions : Options
    {
        [Option("tag", Required = true)]
        public string Tag { get; set; }
    }

    [Verb("exportnew")]
    public class ExportNew : Options
    {
        [Option("oldtag", Required = true)]
        public string OldTag { get; set; }

        [Option("newtag", Required= true )]
        public string NewTag { get; set; }
    }
}
