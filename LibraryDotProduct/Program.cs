using CommandLine;
using CommandLine.Text;

namespace LibraryDotProduct
{
    internal class Program
    {
        class Options
        {
            [Option(Default = .1, HelpText = "m/z tolerance to use when performing dot product between spectra")]
            
            public double MzTolerance { get; set; }
            [Value(0, Required = true, MetaName = "Primary Library File", HelpText = "Library file (.blib or .midas) containing spectra to be compared against")]
            public string Library1 { get; set; }

            [Value(1, Required = true, MetaName = "Second Library File", HelpText = "Library file (.blib or .midas) containing spectra to compare")]
            public string Library2 { get; set; }
        }
        static void Main(string[] args)
        {
            var parsedArguments = Parser.Default.ParseArguments<Options>(args);
            parsedArguments
                .WithParsed(Run)
                .WithNotParsed(errors => HandleParseError(errors, parsedArguments));
        }

        private static void Run(Options options)
        {
            Console.Out.WriteLine("Peptide\tCharge\tFile1\tRetentionTime1\tFile2\tRetentionTime2\tDotProduct");
            var file1 = options.Library1;
            var file2 = options.Library2;
            var library1 = Library.ReadLibrary(file1);
            var library2 = Library.ReadLibrary(file2);
            var entryLookup = library2.Entries.ToLookup(entry => entry.Key);
            foreach (var group in library1.Entries.GroupBy(entry => entry.Key))
            {
                foreach (var entry1 in group)
                {
                    foreach (var entry2 in entryLookup[entry1.Key])
                    {
                        var dotProduct = CalculateSpectrumDotpMzMatch(entry1.Mzs, entry1.Intensities, entry2.Mzs,
                            entry2.Intensities, options.MzTolerance);
                        Console.Out.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}",
                            group.Key.Sequence, group.Key.Charge, entry1.FilePath, entry1.RetentionTime,
                            entry2.FilePath, entry2.RetentionTime, dotProduct);
                    }
                }
            }

        }

        public static double CalculateSpectrumDotpMzMatch(IList<double> mzs1, IList<double> intensities1, IList<double> mzs2, IList<double> intensities2, double tolerance)
        {
            var matchedIntensities = new List<double>();
            foreach (var mz in mzs1)
            {
                var intensity = Enumerable.Range(0, mzs2.Count).Where(i => Math.Abs(mzs2[i] - mz) <= tolerance)
                    .Sum(i => intensities2[i]);
                matchedIntensities.Add(intensity);
            }

            var sumCross = intensities1.Zip(matchedIntensities, (i1, i2) => i1 * i2).Sum();
            var dotProduct = sumCross / Math.Sqrt(intensities1.Sum(i => i * i) * matchedIntensities.Sum(i => i * i));
            dotProduct = Math.Min(1, dotProduct);
            return 1 - Math.Acos(dotProduct) * 2 / Math.PI;
        }

        static void HandleParseError(IEnumerable<Error> errors, ParserResult<Options> result)
        {
            var helpText = HelpText.AutoBuild(result, h =>
            {
                h.AddPreOptionsLine("Calculates the dot product between spectra in two different libraries (.blib or .midas)");
                h.AddPreOptionsLine(
                    "The dot product calculation will use the m/z values from the first library which have a non-zero intensity.");
                h.AddPreOptionsLine(
                    "This results in slightly different dot product values compared to Skyline which uses the m/z values of all predicted fragment ions.");
                return h;
            }, e=>e);
            Console.WriteLine(helpText);
            foreach (var e in errors)
            {
                if (e is HelpRequestedError || e is VersionRequestedError)
                {
                    continue;
                }
                Console.WriteLine($"Error: {e}");
            }
        }
    }
}
