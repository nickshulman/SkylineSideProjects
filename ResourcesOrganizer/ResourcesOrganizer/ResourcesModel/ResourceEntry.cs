using System.Collections.Immutable;

namespace ResourcesOrganizer.ResourcesModel
{
    public record ResourceEntry(string name, InvariantResourceKey key)
    {
        public string Name { get; init; } = name;
        public InvariantResourceKey Invariant { get; init; } = key;
        public string? MimeType { get; init; }
        public string? XmlSpace { get; init; }
        public ImmutableDictionary<string, string> LocalizedValues { get; init; } 
            = ImmutableDictionary<string, string>.Empty;
    }
}
