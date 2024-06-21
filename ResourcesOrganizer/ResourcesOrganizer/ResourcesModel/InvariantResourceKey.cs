namespace ResourcesOrganizer.ResourcesModel
{
    public sealed class InvariantResourceKey : IComparable<InvariantResourceKey>, IComparable
    {
        public string? Name { get; init; }
        public string? File { get; init; }
        public string? Type { get; init; }
        public string Value { get; init; } = string.Empty;
        public string? Comment { get; init; }

        private bool Equals(InvariantResourceKey other)
        {
            return Name == other.Name && File == other.File && Type == other.Type && Value == other.Value && Comment == other.Comment;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is InvariantResourceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, File, Type, Value, Comment);
        }

        public int CompareTo(InvariantResourceKey? other)
        {
            if (ReferenceEquals(this, other)) return 0;
            if (ReferenceEquals(null, other)) return 1;
            var nameComparison = string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
            if (nameComparison != 0) return nameComparison;
            var commentComparison = string.Compare(Comment, other.Comment, StringComparison.OrdinalIgnoreCase);
            if (commentComparison != 0) return commentComparison;
            var valueComparison = string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);
            if (valueComparison != 0) return valueComparison;
            return string.Compare(Type, other.Type, StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object? obj)
        {
            if (ReferenceEquals(null, obj)) return 1;
            if (ReferenceEquals(this, obj)) return 0;
            return obj is InvariantResourceKey other
                ? CompareTo(other)
                : throw new ArgumentException($"Object must be of type {nameof(InvariantResourceKey)}");
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Name != null)
            {
                parts.Add($"Name:{Name}");
            }

            if (Type != null)
            {
                parts.Add($"Type:{Type}");
            }

            if (Comment != null)
            {
                parts.Add($"Comment:{Comment}");
            }
            parts.Add(Value);
            return string.Join(Environment.NewLine, parts);
        }
    }
}
