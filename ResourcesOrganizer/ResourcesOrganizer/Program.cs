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
            return Parser.Default.ParseArguments<AddOptions, SubtractOptions, IntersectOptions>(args)
                .MapResult<AddOptions, SubtractOptions, IntersectOptions, int>(
                    DoAdd, 
                    DoSubtract,
                    DoIntersect,
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
                database.Add(otherDb);
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
                database.Subtract(otherDb);
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
                otherDb.Add(ResourcesDatabase.ReadFile(file));
            }
            database.Intersect(otherDb);
            database.SaveAtomic(options.DbFile);
            return 0;
        }

        private static ResourcesDatabase GetDatabase(Options options)
        {
            var path = options.DbFile;
            var database = new ResourcesDatabase();
            if (File.Exists(path))
            {
                database.ReadDatabase(path);
            }

            return database;
        }
    }
}
