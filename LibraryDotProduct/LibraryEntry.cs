using System.Globalization;
using System.Text;

namespace LibraryDotProduct
{
    public class LibraryEntry
    {
        public int PrecursorCharge { get; init; }
        public double PrecursorMz { get; set; }
        public string PeptideModSeq { get; init; }
        public double RetentionTime { get; init; }
        public double[] Mzs { get; init; }
        public double[] Intensities { get; init; }
        public string FilePath { get; init; }

        public LibraryKey Key
        {
            get
            {
                return new LibraryKey(NormalizeSequence(PeptideModSeq), PrecursorCharge);
            }
        }

        public static string NormalizeSequence(string sequence)
        {
            var result = new StringBuilder();
            int ichLast = 0;
            for(;;)
            {
                int ichNext = sequence.IndexOf('[', ichLast);
                if (ichNext < 0)
                {
                    result.Append(sequence.Substring(ichLast));
                    return result.ToString();
                }

                int ichEnd = sequence.IndexOf(']', ichNext);
                if (ichEnd < 0)
                {
                    result.Append(sequence.Substring(ichLast));
                    return result.ToString();
                }

                string strMod = sequence.Substring(ichNext + 1, ichEnd - ichNext - 1);
                double massMod = Math.Round(double.Parse(strMod), 1);
                result.Append(sequence.Substring(ichLast, ichNext - ichLast));
                if (massMod == 0)
                {
                    continue;
                }

                result.Append("[");
                if (massMod >= 0)
                {
                    result.Append("+");
                }

                result.Append(massMod.ToString("0.0", CultureInfo.InvariantCulture));
                result.Append("]");
                ichLast = ichEnd + 1;
            }
        }
    }

    public class LibraryKey
    {
        public LibraryKey(string sequence, int charge)
        {
            Sequence = sequence;
            Charge = charge;
        }
        public string Sequence { get; }
        public int Charge { get; }

        protected bool Equals(LibraryKey other)
        {
            return Sequence == other.Sequence && Charge == other.Charge;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((LibraryKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Sequence, Charge);
        }
    }
}
