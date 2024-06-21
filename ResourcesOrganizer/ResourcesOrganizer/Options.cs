using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace ResourcesOrganizer
{
    public class Options
    {
        [Option("dbfile", Default = "resources.db")]
        public string DbFile { get; set; }
    }

    [Verb("import")]
    public class ImportOptions
    {
        [Option("tag", Required = true)]
        public string Tag { get; set; }
    }
}
