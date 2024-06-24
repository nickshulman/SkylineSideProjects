using System.Text;
using CommandLine;
using ResourcesOrganizer.ResourcesModel;

namespace ResourcesOrganizer
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            return Parser.Default.ParseArguments<AddOptions, SubtractOptions, IntersectOptions, ExportOptions>(args)
                .MapResult<AddOptions, SubtractOptions, IntersectOptions, ExportOptions, int>(
                    DoAdd, 
                    DoSubtract,
                    DoIntersect,
                    DoExport,
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

        static int DoAdd(AddOptions options)
        {
            var database = GetDatabase(options);
            foreach (var file in options.Files)
            {
                var otherDb = ResourcesDatabase.ReadFile(file);
                database = database.Add(otherDb);
            }
            database.SaveAtomic(options.DbFile);
            return 0;
        }

        static int DoSubtract(SubtractOptions options)
        {
            var database = GetDatabase(options);
            foreach (var file in options.Files)
            {
                var otherDb = ResourcesDatabase.ReadFile(file);
                database = database.Subtract(otherDb);
            }
            database.SaveAtomic(options.DbFile);
            return 0;

        }

        static int DoIntersect(IntersectOptions options)
        {
            var database = GetDatabase(options);
            var otherDb = new ResourcesDatabase();
            foreach (var file in options.Files)
            {
                otherDb = otherDb.Add(ResourcesDatabase.ReadFile(file));
            }
            database = database.Intersect(otherDb);
            using var fileSaver = new FileSaver(options.DbFile);
            database.Save(fileSaver.SafeName);
            fileSaver.Commit();
            return 0;
        }

        static int DoExport(ExportOptions options)
        {
            var database = GetDatabase(options);
            using var fileSaver = new FileSaver(options.OutputFile);
            database.Export(fileSaver.SafeName);
            fileSaver.Commit();
            return 0;
        }

        private static ResourcesDatabase GetDatabase(Options options)
        {
            var path = options.DbFile;
            if (File.Exists(path))
            {
                return ResourcesDatabase.ReadDatabase(path);
            }
            return ResourcesDatabase.EMPTY;
        }
    }
}
