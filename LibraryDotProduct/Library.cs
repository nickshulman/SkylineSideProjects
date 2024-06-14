using System.Data;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace LibraryDotProduct
{
    public class Library
    {
        public Library(IList<LibraryEntry> entries)
        {
            Entries = entries;
        }
        public IList<LibraryEntry> Entries { get; }

        public static Library ReadLibrary(string path)
        {
            var connectionStringBuilder = new SqliteConnectionStringBuilder();
            connectionStringBuilder.DataSource = path;
            using var connection = new SqliteConnection(connectionStringBuilder.ToString());
            connection.Open();
            IList<LibraryEntry> entries;
            if (".midas".Equals(Path.GetExtension(path), StringComparison.InvariantCultureIgnoreCase))
            {
                entries = ReadMidas(connection).ToList();
            }
            else
            {
                entries = ReadBlib(connection).ToList();
            }

            return new Library(entries);
        }

        public static IEnumerable<LibraryEntry> ReadMidas(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT s.PrecursorMz, s.DocumentPeptide, s.DocumentPrecursorCharge, s.RetentionTime, s.MzBytes, s.IntensityBytes, f.FilePath FROM Spectrum s INNER JOIN ResultsFile f ON s.ResultsFileId = f.Id";
            using var rs = cmd.ExecuteReader();
            while (rs.Read())
            {
                var entry = new LibraryEntry
                {
                    FilePath = rs.GetString(6),
                    PeptideModSeq = rs.GetString(1),
                    PrecursorCharge = rs.GetInt32(2),
                    PrecursorMz = rs.GetDouble(0),
                    RetentionTime = rs.GetDouble(3),
                    Mzs = UncompressMidasDoubles(GetBytes(rs, 4)),
                    Intensities = UncompressMidasDoubles(GetBytes(rs, 5))
                };
                yield return entry;
            }
        }

        private static double[] UncompressMidasDoubles(byte[] bytes)
        {
            byte[] uncompressed;
            try
            {
                uncompressed = Uncompress(bytes, -1);
            }
            catch (Exception)
            {
                uncompressed = bytes;
            }

            return ToDoubles(uncompressed);
        }

        public static IEnumerable<LibraryEntry> ReadBlib(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT s.precursorMZ, s.peptideModSeq, s.precursorCharge, s.RetentionTime, s.NumPeaks, p.peakMZ, p.peakIntensity, f.fileName FROM RefSpectra s INNER JOIN RefSpectraPeaks p ON s.Id = p.RefSpectraId INNER JOIN SpectrumSourceFiles f ON s.fileId = f.Id";
            using var rs = cmd.ExecuteReader();
            while (rs.Read())
            {
                int numPeaks = rs.GetInt32(4);
                var entry = new LibraryEntry
                {
                    FilePath = rs.GetString(7),
                    PeptideModSeq = rs.GetString(1),
                    PrecursorCharge = rs.GetInt32(2),
                    PrecursorMz = rs.GetDouble(0),
                    RetentionTime = rs.GetDouble(3),
                    Mzs = ToDoubles(Uncompress(GetBytes(rs, 5), numPeaks * sizeof(double))),
                    Intensities = ToFloats(Uncompress(GetBytes(rs, 6), numPeaks * sizeof(float))).Select(f => (double)f).ToArray()

                };
                yield return entry;
            }
        }

        private static byte[]? GetBytes(IDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            var length = reader.GetBytes(ordinal, 0, null, 0, 0);
            var bytes = new byte[length];
            reader.GetBytes(ordinal, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] Uncompress(byte[] compressedBytes, int uncompressedLength)
        {
            if (compressedBytes.Length == uncompressedLength)
            {
                return compressedBytes;
            }

            var zlibStream = new ZLibStream(new MemoryStream(compressedBytes), CompressionMode.Decompress);
            var uncompressedBytes = new List<byte>();
            var buffer = new byte[1024];
            int count;
            while ((count = zlibStream.Read(buffer, 0, buffer.Length)) != 0)
            {
                uncompressedBytes.AddRange(buffer.Take(count));
            }
            return uncompressedBytes.ToArray();
        }

        private static double[] ToDoubles(byte[] bytes)
        {
            var doubles = new double[bytes.Length/sizeof(double)];
            for (int i = 0; i < doubles.Length; i++)
            {
                doubles[i] = BitConverter.ToDouble(bytes, i * sizeof(double));
            }

            return doubles;
        }

        private static float[] ToFloats(byte[] bytes)
        {
            var floats = new float[bytes.Length/sizeof(float)];
            for (int i = 0; i < floats.Length; i++)
            {
                floats[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
            }

            return floats;
        }
    }
}
