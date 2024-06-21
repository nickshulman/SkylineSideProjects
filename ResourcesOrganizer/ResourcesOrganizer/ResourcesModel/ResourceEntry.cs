namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourceEntry(string name, InvariantResourceKey key)
    {
        public string Name { get; init; } = name;
        public InvariantResourceKey Invariant { get; init; } = key;
        public Dictionary<string, string> LocalizedValues { get; init; } = new Dictionary<string, string>();
    }
}
