using System.Runtime.Serialization;
using CommandLine;
using ResourcesOrganizer.ResourcesModel;

namespace ResourcesOrganizer
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            return Parser.Default.ParseArguments<ImportOptions, ExportNew>(args)
                .MapResult<ImportOptions, ExportNew, int>(
                    DoImport, 
                    DoExportNew, 
                    HandleParseError);
        }

        static int HandleParseError(IEnumerable<Error> errors)
        {
            foreach (var e in errors)
            {
                if (e is HelpRequestedError || e is VersionRequestedError)
                {
                    continue;
                }
                Console.WriteLine($"Error: {e}");
            }

            return 1;
        }

        static int DoImport(ImportOptions options)
        {
            var database = GetDatabase(options);

            var tempFile = Path.GetTempFileName();
            database.Save(tempFile);
            File.Replace(tempFile, options.DbFile, null);
            return 0;
        }

        static int DoExportNew(ExportNew exportOptions)
        {
            var database = GetDatabase(exportOptions);
            return 0;
        }

        private static ResourcesDatabase GetDatabase(Options options)
        {
            var path = options.DbFile;
            var database = new ResourcesDatabase();
            if (File.Exists(path))
            {
                database.Read(path);
            }

            return database;
        }
    }
}
