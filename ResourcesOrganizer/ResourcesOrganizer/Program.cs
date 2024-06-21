using CommandLine.Text;
using CommandLine;
using NHibernate.Mapping.ByCode;

namespace ResourcesOrganizer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            Parser.Default.ParseArguments<ImportOptions>(args)
                .WithParsed(DoImport)
                .WithNotParsed(HandleParseError);
        }

        static void HandleParseError(IEnumerable<Error> errors)
        {
            foreach (var e in errors)
            {
                if (e is HelpRequestedError || e is VersionRequestedError)
                {
                    continue;
                }
                Console.WriteLine($"Error: {e}");
            }
        }

        static void DoImport(ImportOptions options)
        {

        }
    }
}
