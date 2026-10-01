using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Xml;
using System.Xml.Linq;
using static RemoteReports.SkyXml;

namespace RemoteReports;

/// <summary>
/// Streams a Skyline .sky document with an <see cref="XmlReader"/> and produces the rows of the PRISM
/// report (row source Transition, sublist Results!*): one row per transition per peak, or a single
/// row with empty result columns for a transition with no peaks.
///
/// The work is split the way Skyline's DocumentReader.PeptideProcessor splits it: one thread reads the
/// XML, loading each &lt;peptide&gt; as an <see cref="XElement"/>, and a pool of worker threads turns
/// those into rows (<see cref="PeptideReader"/>). The rows come back in document order. At most
/// <c>4 × threads</c> peptides are in flight, so memory stays bounded regardless of document size.
///
/// This reimplements the relevant parts of Skyline's DocumentReader and report entities without using
/// Skyline code. Not handled: small molecules (&lt;molecule&gt; is skipped), crosslinked peptides
/// (linked peptides and crosslinkers are left out of Peptide and UnimodIds), and custom ion names.
/// </summary>
public sealed class SkyDocumentReader
{
    private readonly XmlReader _reader;
    private SkyDocumentSettings _settings = SkyDocumentSettings.Empty;

    private SkyDocumentReader(XmlReader reader) => _reader = reader;

    /// <summary>Yields each peptide's rows, in document order.</summary>
    /// <param name="skyStream">The .sky XML; read on a dedicated thread with blocking reads.</param>
    /// <param name="threadCount">The number of worker threads that make rows from peptides.</param>
    public static async IAsyncEnumerable<List<ReportRow>> ReadPeptideRowsAsync(Stream skyStream, int threadCount,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = cancel.Token;
        // The parser adds each peptide to both queues: workers take them from "work" in any order, and
        // the caller takes them from "ordered" in document order. "ordered" being bounded is what
        // keeps the parser from running ahead of the Parquet writer.
        var work = new BlockingCollection<PeptideWorkItem>();
        var ordered = Channel.CreateBounded<PeptideWorkItem>(new BoundedChannelOptions(Math.Max(16, threadCount * 4))
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var parser = Task.Factory.StartNew(() => Parse(skyStream, work, ordered.Writer, token),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var workers = Enumerable.Range(0, threadCount).Select(_ => Task.Factory.StartNew(() =>
        {
            foreach (var item in work.GetConsumingEnumerable(token))
                item.Run();
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        try
        {
            await foreach (var item in ordered.Reader.ReadAllAsync(token))
                yield return await item.Rows.WaitAsync(token);
            // The parser completes "ordered" whether or not it succeeded; this rethrows its error, such
            // as a download failure or bad XML, after the rows before it have been handed out.
            await parser;
        }
        finally
        {
            // Stop everything on an early exit: a blocked parser or worker sees the cancellation. The
            // parser's stream keeps delivering data (or ends) until disposed, so this wait is short.
            cancel.Cancel();
            try
            {
                await Task.WhenAll(workers.Append(parser));
            }
            catch
            {
                // Already reported above, or a consequence of the cancellation.
            }
            work.Dispose();
        }
    }

    private static void Parse(Stream skyStream, BlockingCollection<PeptideWorkItem> work,
        ChannelWriter<PeptideWorkItem> ordered, CancellationToken token)
    {
        try
        {
            var xmlSettings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Prohibit,
            };
            using var xmlReader = XmlReader.Create(skyStream, xmlSettings);
            foreach (var item in new SkyDocumentReader(xmlReader).ReadDocument())
            {
                // Wait for room in "ordered" first, so that "work" never holds more than it does.
                ordered.WriteAsync(item, token).AsTask().GetAwaiter().GetResult();
                work.Add(item, token);
            }
        }
        finally
        {
            work.CompleteAdding();
            ordered.Complete();
        }
    }

    /// <summary>Yields a work item for each peptide, loaded as an XElement.</summary>
    private IEnumerable<PeptideWorkItem> ReadDocument()
    {
        var r = _reader;
        r.MoveToContent();
        if (r.LocalName != "srm_settings")
            throw new InvalidDataException($"Not a Skyline document: root element is <{r.LocalName}>");
        if (!EnterElement(r))
            yield break;
        while (NextChild(r))
        {
            switch (r.LocalName)
            {
                case "settings_summary":
                    _settings = SkyDocumentSettings.FromXml((XElement)XNode.ReadFrom(r));
                    break;
                case "protein":
                case "peptide_list":
                case "protein_group":
                    foreach (var item in ReadPeptideGroup())
                        yield return item;
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private IEnumerable<PeptideWorkItem> ReadPeptideGroup()
    {
        var r = _reader;
        string elementName = r.LocalName;
        string? groupName = null;
        ProteinInfo? protein = null;
        if (elementName == "protein_group")
        {
            // Filled in from the member proteins, which come before the peptides.
            groupName = r.GetAttribute("name");
        }
        else
        {
            string? name = elementName == "protein"
                ? r.GetAttribute("label_name") ?? r.GetAttribute("name")
                : r.GetAttribute("label_name") ?? "";
            protein = new ProteinInfo(name, r.GetAttribute("accession"), r.GetAttribute("gene"));
        }
        var members = new List<ProteinInfo>();

        if (!EnterElement(r))
            yield break;
        while (NextChild(r))
        {
            switch (r.LocalName)
            {
                case "protein" when elementName == "protein_group":
                    members.Add(new ProteinInfo(r.GetAttribute("label_name") ?? r.GetAttribute("name"),
                        r.GetAttribute("accession"), r.GetAttribute("gene")));
                    r.Skip();
                    break;
                case "peptide":
                    protein ??= GetProteinGroupInfo(groupName, members);
                    yield return new PeptideWorkItem((XElement)XNode.ReadFrom(r), protein, _settings);
                    break;
                default:
                    // <molecule>, <sequence>, notes, annotations
                    r.Skip();
                    break;
            }
        }
    }

    /// <summary>Mirrors ProteinGroupMetadata: member values joined with " / ".</summary>
    private static ProteinInfo GetProteinGroupInfo(string? groupName, List<ProteinInfo> members)
    {
        string name = members.All(m => string.IsNullOrEmpty(m.Name))
            ? groupName ?? ""
            : string.Join(" / ", members.Select(m => m.Name ?? ""));
        return new ProteinInfo(name, JoinMembers(members.Select(m => m.Accession)),
            JoinMembers(members.Select(m => m.Gene)));
    }

    private static string? JoinMembers(IEnumerable<string?> values)
    {
        var list = values.ToList();
        if (list.All(string.IsNullOrEmpty))
            return null;
        return string.Join(" / ", list.Select(v => string.IsNullOrEmpty(v) ? "Unknown" : v));
    }

    /// <summary>One peptide, waiting for a worker to turn it into rows.</summary>
    private sealed class PeptideWorkItem(XElement element, ProteinInfo protein, SkyDocumentSettings settings)
    {
        private XElement? _element = element;
        private readonly TaskCompletionSource<List<ReportRow>> _rows =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<List<ReportRow>> Rows => _rows.Task;

        public void Run()
        {
            try
            {
                _rows.SetResult(PeptideReader.ReadRows(_element!, protein, settings));
            }
            catch (Exception e)
            {
                _rows.SetException(e);
            }
            // The rows may wait a while for the writer; the XML is no longer needed.
            _element = null;
        }
    }
}

/// <summary>The protein columns of the rows for the peptides in one protein, peptide list, or group.</summary>
public sealed record ProteinInfo(string? Name, string? Accession, string? Gene);

/// <summary>Helpers for walking a .sky document one element at a time with an <see cref="XmlReader"/>.</summary>
internal static class SkyXml
{
    /// <summary>
    /// Moves inside the current element. Returns false (having consumed it) if it is empty.
    /// </summary>
    public static bool EnterElement(XmlReader r)
    {
        if (r.IsEmptyElement)
        {
            r.Read();
            return false;
        }
        r.Read();
        return true;
    }

    /// <summary>
    /// Advances to the next child element of the element entered with <see cref="EnterElement"/>.
    /// Returns false after consuming the parent's end tag. The caller must consume each child
    /// (read it or Skip()) before calling again.
    /// </summary>
    public static bool NextChild(XmlReader r)
    {
        while (true)
        {
            switch (r.MoveToContent())
            {
                case XmlNodeType.Element:
                    return true;
                case XmlNodeType.EndElement:
                    r.Read();
                    return false;
                case XmlNodeType.None:
                    return false;
                default:
                    r.Read();
                    break;
            }
        }
    }
}
