using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using RemoteReports;

const string sourceDescription =
    "A local path or http(s) URL (e.g. a Panorama WebDAV link) to a .sky file or a zip containing one. " +
    "Remote zips are read with HTTP Range requests, fetching only the zip directory and the entry being read. " +
    "Set LABKEY_API_KEY for non-public folders.";

var sourceArgument = new Argument<string>("source") { Description = sourceDescription };
var entryOption = new Option<string?>("--entry") { Description = "Zip entry to read (default: the first .sky file)" };

// report
var outputArgument = new Argument<FileInfo>("output") { Description = "The .parquet file to write" };
var skyrOption = new Option<FileInfo>("--report", "-r")
{
    Description = "A Skyline .skyr report definition (default: the built-in PRISM report). " +
                  "It must use the Transition row source with the Results!* sublist, and only supported columns.",
};
skyrOption.AcceptExistingOnly();
var reportNameOption = new Option<string?>("--report-name")
{
    Description = "Which report to use when the .skyr file contains more than one",
};
var rowsPerGroupOption = new Option<int>("--rows-per-group")
{
    Description = "Rows buffered and written per Parquet row group",
    DefaultValueFactory = _ => ParquetReportWriter.DefaultRowsPerGroup,
};
var reportCommand = new Command("report", "Stream a Skyline document and write a report as Parquet")
{
    sourceArgument, outputArgument, skyrOption, reportNameOption, entryOption, rowsPerGroupOption,
};
reportCommand.SetAction(async (parseResult, ct) =>
{
    var skyr = parseResult.GetValue(skyrOption);
    var report = skyr != null
        ? ReportDefinition.Load(skyr.FullName, parseResult.GetValue(reportNameOption))
        : ReportDefinition.Prism;
    var output = parseResult.GetValue(outputArgument)!;
    int rowsPerGroup = parseResult.GetValue(rowsPerGroupOption);

    await using var source = await OpenSourceAsync(parseResult.GetValue(sourceArgument)!, ct);
    await using var skyStream = source.OpenSky(parseResult.GetValue(entryOption));
    await using var outputStream = output.Create();
    var writer = await ParquetReportWriter.CreateAsync(outputStream, report.Columns, rowsPerGroup);
    await using (writer)
    {
        foreach (var row in SkyDocumentReader.ReadRows(skyStream))
        {
            ct.ThrowIfCancellationRequested();
            await writer.AddAsync(row);
        }
    }
    Console.WriteLine($"Wrote {writer.RowsWritten:N0} rows of report '{report.Name}' to {output.FullName}");
    source.PrintStats();
    return 0;
});

// list
var listCommand = new Command("list", "List the entries of a zip") { sourceArgument };
listCommand.SetAction(async (parseResult, ct) =>
{
    await using var source = await OpenSourceAsync(parseResult.GetValue(sourceArgument)!, ct);
    var zip = source.Zip ?? throw new InvalidDataException("The source is not a zip file");
    Console.WriteLine($"{zip.Entries.Count} entries:");
    foreach (var e in zip.Entries)
        Console.WriteLine($"  {e.Length,15:N0}  {e.CompressedLength,15:N0}  {e.FullName}");
    source.PrintStats();
    return 0;
});

// extract
var extractOutputOption = new Option<FileInfo>("--out") { Description = "Where to write the entry (default: its file name)" };
var extractCommand = new Command("extract", "Copy one zip entry to a local file") { sourceArgument, entryOption, extractOutputOption };
extractCommand.SetAction(async (parseResult, ct) =>
{
    await using var source = await OpenSourceAsync(parseResult.GetValue(sourceArgument)!, ct);
    var zip = source.Zip ?? throw new InvalidDataException("The source is not a zip file");
    var entry = RemoteSource.FindSkyEntry(zip, parseResult.GetValue(entryOption));
    var output = parseResult.GetValue(extractOutputOption) ?? new FileInfo(Path.GetFileName(entry.FullName));
    await using (var entryStream = await entry.OpenAsync(ct))
    await using (var target = output.Create())
    {
        await entryStream.CopyToAsync(target, ct);
    }
    Console.WriteLine($"Wrote {entry.Length:N0} bytes of '{entry.FullName}' to {output.FullName}");
    source.PrintStats();
    return 0;
});

// compare
var expectedArgument = new Argument<FileInfo>("expected") { Description = "Reference .parquet file" };
var actualArgument = new Argument<FileInfo>("actual") { Description = ".parquet file to check" };
expectedArgument.AcceptExistingOnly();
actualArgument.AcceptExistingOnly();
var compareCommand = new Command("compare", "Compare two Parquet files column by column") { expectedArgument, actualArgument };
compareCommand.SetAction((parseResult, ct) => ParquetCompare.RunAsync(
    parseResult.GetValue(expectedArgument)!.FullName, parseResult.GetValue(actualArgument)!.FullName));

var root = new RootCommand("Writes Parquet reports from Skyline documents, locally or on a WebDAV server")
{
    reportCommand, listCommand, extractCommand, compareCommand,
};

try
{
    // Our own handler below prints known errors as one line instead of a stack trace.
    return await root.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (Exception e) when (e is ReportDefinitionException or InvalidDataException or FileNotFoundException
                              or HttpRequestException or IOException)
{
    Console.Error.WriteLine("Error: " + e.Message);
    return 1;
}

static async Task<RemoteSource> OpenSourceAsync(string path, CancellationToken ct)
{
    Stream stream;
    if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        var client = new HttpClient();
        string? apiKey = Environment.GetEnvironmentVariable("LABKEY_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
        {
            // LabKey accepts an API key as the password for the literal user name "apikey".
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("apikey:" + apiKey)));
        }
        stream = await HttpRangeStream.OpenAsync(client, new Uri(path), ct);
    }
    else
    {
        stream = File.OpenRead(path);
    }
    return new RemoteSource(stream);
}

/// <summary>An opened source: a .sky stream, or a zip that contains one.</summary>
sealed class RemoteSource : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public ZipArchive? Zip { get; }

    public RemoteSource(Stream stream)
    {
        _stream = stream;
        // Recognize a zip by its content rather than its name: the end-of-central-directory record
        // "PK\x05\x06" near the end. Looking at the end rather than the start keeps a remote zip to
        // the tail that HttpRangeStream has already fetched.
        int tailLength = (int)Math.Min(stream.Length, 64 * 1024);
        var tail = new byte[tailLength];
        stream.Seek(-tailLength, SeekOrigin.End);
        stream.ReadExactly(tail);
        stream.Position = 0;
        if (tail.AsSpan().LastIndexOf("PK\x05\x06"u8) >= 0)
            Zip = new ZipArchive(stream, ZipArchiveMode.Read);
    }

    public Stream OpenSky(string? entryName)
    {
        if (Zip == null)
        {
            if (entryName != null)
                throw new InvalidDataException("--entry was given, but the source is not a zip file");
            return new NonClosingStream(_stream);
        }
        return FindSkyEntry(Zip, entryName).Open();
    }

    public static ZipArchiveEntry FindSkyEntry(ZipArchive zip, string? name)
    {
        var entry = name != null
            ? zip.GetEntry(name)
            : zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".sky", StringComparison.OrdinalIgnoreCase));
        return entry ?? throw new FileNotFoundException(name != null ? $"No entry named '{name}' in the zip" : "No .sky entry in the zip");
    }

    public void PrintStats()
    {
        Console.WriteLine(_stream is HttpRangeStream remote
            ? $"{remote.RequestCount} requests, {remote.BytesFetched:N0} bytes fetched " +
              $"({100.0 * remote.BytesFetched / remote.Length:F1}% of {remote.Length:N0}), {_stopwatch.Elapsed.TotalSeconds:F1}s"
            : $"{_stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    public async ValueTask DisposeAsync()
    {
        Zip?.Dispose();
        await _stream.DisposeAsync();
    }

    /// <summary>Lets the .sky stream be disposed by its reader while the source still owns it.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
