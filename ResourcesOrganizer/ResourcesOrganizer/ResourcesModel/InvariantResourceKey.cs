﻿namespace ResourcesOrganizer.ResourcesModel
{
    public sealed record InvariantResourceKey : IComparable<InvariantResourceKey>, IComparable
    {
        public string? Name { get; init; }
        public string? File { get; init; }
        public string? Type { get; init; }
        public string Value { get; init; } = string.Empty;
        public string? Comment { get; init; }

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
            var fileComparison = string.Compare(File, other.File, StringComparison.OrdinalIgnoreCase);
            if (fileComparison != 0) return fileComparison;
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
                parts.Add($"Comment:{TextUtil.Quote(Comment)}");
            }

            if (File != null)
            {
                parts.Add($"File:{File}");
            }
            parts.Add($"Value:{TextUtil.Quote(Value)}");
            return string.Join(" ", parts);
        }
    }
}
