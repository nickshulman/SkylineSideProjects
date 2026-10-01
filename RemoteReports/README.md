# RemoteReports

A command-line tool that reads a Skyline document (`.sky`) and writes a report as a
[Parquet](https://parquet.apache.org/) file, without Skyline and without loading the whole
document into memory.

The document can be a local `.sky` file, a `.sky.zip`, or a `.sky.zip` on a Panorama server.
For a server, the tool reads just the `.sky` out of the zip with HTTP Range requests, skipping the
much larger `.skyd` chromatogram and `.blib` library files. For example, for a 35 GB `.sky.zip` it downloads
the 4.9 GB compressed `.sky`, and nothing else.

## Getting it

### Downloading a release

Releases are on the [Releases page](https://github.com/nickshulman/SkylineSideProjects/releases)
(tags starting with `RemoteReports-`). Each has:

| File | |
|---|---|
| `RemoteReports-<version>-win-x64.exe` | Windows, a single executable that includes .NET. Windows may warn that it is from an unknown publisher: choose "More info", then "Run anyway". |
| `RemoteReports-<version>-linux-x64` | Linux, a single executable that includes .NET. Make it executable first: `chmod +x RemoteReports-<version>-linux-x64`. |
| `RemoteReports-<version>-portable.zip` | Any platform with the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed (including macOS). Unzip it and run `dotnet RemoteReports.dll`. |

The examples below write `RemoteReports` for whichever of these you use. `RemoteReports --version`
shows the version and the git commit it was built from.

### Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). From this folder:

```
dotnet build RemoteReports.csproj -c Release
```

To make builds to give to other people:

```
# Self-contained: no .NET needed on the other computer (about 80 MB). One per platform.
dotnet publish RemoteReports.csproj -c Release -r win-x64   --self-contained -o publish/win-x64
dotnet publish RemoteReports.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
dotnet publish RemoteReports.csproj -c Release -r osx-arm64 --self-contained -o publish/osx-arm64

# Portable: a few MB, runs on any platform with the .NET 10 runtime.
dotnet publish RemoteReports.csproj -c Release -o publish/portable

# Single file: one self-contained executable per platform (about 40 MB; the .pdb and .xml
# files written next to it are not needed). This is how the release executables are built.
dotnet publish RemoteReports.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/linux-x64-single
```

Zip the output folder and send that, or for a single-file build, just the executable. The
version is set in `RemoteReports.csproj`.

## Writing a report

```
RemoteReports report <source> <output.parquet> [options]
```

`<source>` can be:

| Source | Example |
|---|---|
| A local `.sky` file | `MyExperiment.sky` |
| A local zip containing a `.sky` | `MyExperiment.sky.zip` |
| The URL of a `.sky` or `.sky.zip` on a server | `https://panoramaweb.org/_webdav/MyLab/MyFolder/%40files/MyExperiment.sky.zip` |
| A `.skyp` file (a pointer to a `.sky.zip` on Panorama) | `MyExperiment.skyp` |

A Panorama WebDAV URL is the link to the file in the folder's file browser, with `_webdav` in it.
A `.skyp` is a small text file with that URL in it, which Panorama offers for opening a document
in Skyline.
`Examples/` has one for a document on Panorama Public:

```
RemoteReports report "Examples/Legrand et al. Effects of Taurine and 3-SPA on ApoE4 aggregation, AD_targeted proteomics_data_2025-01-03_12-00-39.skyp" legrand.parquet
```

```
Wrote 3,720 rows of report 'PRISM' to legrand.parquet
3 requests, 233,371 bytes fetched (0.1% of 250,260,791), 0.3s
```

When a zip has more than one `.sky`, choose one with `--entry <name>` (`list` shows the names).

### Options

| Option | |
|---|---|
| `-r, --report <file.skyr>` | A report definition exported from Skyline. Default: the PRISM report. See [Reports](#reports). |
| `--report-name <name>` | Which report to use when the `.skyr` file has more than one. |
| `--entry <name>` | The zip entry to read. Default: the first `.sky` file. |
| `--rows-per-group <n>` | Rows per Parquet row group. Default 500,000. Two row groups are held in memory (about 130 MB each at the default). |
| `--threads <n>` | Worker threads. Default 4; more makes no difference or is slower. |
| `-u, --username`, `--password`, `--api-key` | Panorama sign-in. See below. |

## Signing in to Panorama

Public folders (such as Panorama Public) need no sign-in. For other folders, give either a
username and password, or an API key (which you can make on Panorama under your account's
"API Keys" page).

| | Command line | Environment variable |
|---|---|---|
| Username (your email) | `-u you@example.org` | `LABKEY_USERNAME` |
| Password | `--password ...` | `LABKEY_PASSWORD` |
| API key | `--api-key ...` | `LABKEY_API_KEY` |

- With a username but no password, the tool asks for the password (it is not echoed).
- With a password but no username, a `.skyp` file's `DownloadingUser` is the username.
- With no credentials at all, if the server refuses access, the tool asks for a username and
  password when run in a terminal.
- Prefer the prompt or an environment variable to `--password`, which other users of the
  computer can see in the process list, and which is saved in your shell history.
- Credentials are only sent over `https`.

For example, in bash:

```
export LABKEY_PASSWORD='...'
RemoteReports report MyExperiment.skyp out.parquet
```

or in PowerShell:

```
$env:LABKEY_API_KEY = '...'
RemoteReports report https://panoramaweb.org/_webdav/.../MyExperiment.sky.zip out.parquet
```

## Reports

Without `--report`, the tool writes Skyline's PRISM report: one row per transition per replicate
(a transition with no results gets one row with empty result columns). Its columns are:

| Column | Skyline property |
|---|---|
| Protein, ProteinAccession, ProteinGene | `Precursor.Peptide.Protein` (`.Accession`, `.Gene`) |
| Peptide | `Precursor.Peptide` |
| PeptideModifiedSequenceUnimodIds | `Precursor.Peptide.ModifiedSequence.UnimodIds` |
| PrecursorCharge, PrecursorMz | `Precursor.Charge`, `Precursor.Mz` |
| IsotopeDotProduct, DetectionQValue | `Results!*.Value.PrecursorResult.IsotopeDotProduct`, `.DetectionQValue` |
| FragmentIon, ProductCharge, ProductMz | `FragmentIon`, `ProductCharge`, `ProductMz` |
| Area, Background, RetentionTime, StartTime, EndTime, Fwhm, ShapeCorrelation, Truncated | `Results!*.Value.*` |
| ReplicateName, FileName, TicArea, AcquiredTime | `Results!*.Value.PrecursorResult.PeptideResult.ResultFile.*` |

A `.skyr` file exported from Skyline (Export Report > Edit List > Share) can choose and order any
of these columns. It must use the Transition row source with the `Results!*` sublist. The tool
refuses a report that uses other columns, filters, sorting, or custom column captions, rather
than produce something different from what Skyline would.

### Output format

The file is written the way Skyline's own Parquet export writes it: invariant (English) column
names, every column optional (nullable), Zstd compression, and dictionary encoding for text
columns. AcquiredTime is a Parquet `TIMESTAMP` in milliseconds, marked as adjusted to UTC, as Skyline
writes every date and time column. The `.sky` file does not record a time zone, so the time is
written as it appears there (see below).

### Differences from Skyline's export

- **AcquiredTime** comes from the `.sky` file, which has the acquisition time in the time zone
  of the computer that saved the document, but not which time zone that was. Skyline reads the
  exact time from the `.skyd` file instead, so the two can differ by that computer's UTC offset.
- **Not supported:** small molecules (they are left out), crosslinked peptides (the linked
  peptides and crosslinkers are left out of Peptide and UnimodIds), and custom ion names.

## Other commands

| Command | |
|---|---|
| `RemoteReports list <source>` | List the files in a zip, local or remote. |
| `RemoteReports extract <source> [--entry <name>] [--out <file>]` | Copy one file out of a zip, local or remote. |
| `RemoteReports compare <expected.parquet> <actual.parquet>` | Compare two Parquet files column by column (schema, then every value) and print the differences. |

`list` and `extract` take the same sign-in options as `report`.

## Performance

| Document | Rows | Time |
|---|---|---|
| 2.4 GB `.sky` on a local disk | 15.1M | ~21 s |
| The same, in a 5.5 GB `.sky.zip` on a local disk | 15.1M | ~23 s |
| 35 GB `.sky.zip` on panoramaweb.org (4.9 GB downloaded) | 81.8M | ~8.5 min, limited by the download (~10 MB/s) |

A server document is read with one long download (reconnecting where it left off if the
connection drops or stalls). Downloading and unzipping, reading the XML, turning each peptide into rows,
and writing Parquet each run on their own threads. Writing Parquet takes the most time.

## How it works

| File | |
|---|---|
| `Program.cs` | The commands and their options; opening a local or remote source. |
| `HttpRangeStream.cs` | A seekable stream over a file on a web server, using HTTP Range requests, so `ZipArchive` can read a remote zip. |
| `ReadAheadStream.cs` | Downloads and unzips on its own thread, ahead of the XML reader. |
| `SkyDocumentReader.cs` | Reads the `.sky` XML, hands each `<peptide>` to worker threads, and returns their rows in document order (like Skyline's `DocumentReader`). |
| `PeptideReader.cs` | Turns one `<peptide>` into report rows. |
| `SkyDocumentSettings.cs` | The modifications and replicates from `<settings_summary>`. |
| `ReportDefinition.cs`, `ReportColumns.cs` | `.skyr` parsing and the supported columns. |
| `ParquetReportWriter.cs` | Writes the rows, one row group at a time. |
| `RemoteCredentials.cs`, `SkypFile.cs` | Panorama sign-in, and `.skyp` files. |
| `Lib/Parquet.dll` | A copy of the patched Parquet.Net 6.1 fork that Skyline uses (`pwiz_tools/Shared/Lib/Parquet/Parquet.dll`), with its `.xml`. `RemoteReports.csproj` references it and the packages it depends on in place of the `Parquet.Net` package, so that publish and single-file builds include it. |
| `Protos/SkylineDocument.proto` | Copied from Skyline, to read the compressed `<transition_data>` that large documents use. |
